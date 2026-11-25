using ArksScanner.Api.Auth;
using ArksScanner.Infrastructure.Ads;

namespace ArksScanner.Api.Endpoints;

/// <summary>Rewarded ads (mobile, Free accounts): issue a reward, then AdMob's signed callback grants it.</summary>
public static class AdEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var me = endpoints.MapGroup("/api/me/ad-rewards").RequireAuthorization();

        me.MapPost("", async (ICurrentUser user, AdRewardService rewards, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            try
            {
                var reward = await rewards.IssueAsync(user.FirebaseUid, ct);
                return Results.Ok(new { id = reward.Id, pages = reward.Pages });
            }
            catch (AdRewardException refused) { return Refused(refused); }
        });

        me.MapGet("/{id:guid}", async (Guid id, ICurrentUser user, AdRewardService rewards, HttpContext context,
            CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            return Results.Ok(new { granted = await rewards.IsGrantedAsync(user.FirebaseUid, id, ct) });
        });

        me.MapPost("/{id:guid}/claim", async (Guid id, ICurrentUser user, AdRewardService rewards, CancellationToken ct) =>
        {
            try
            {
                await rewards.ClaimUnverifiedAsync(user.FirebaseUid, id, ct);
                return Results.NoContent();
            }
            catch (AdRewardException refused) { return Refused(refused); }
        });

        // Called by Google, not the app: no sign-in, so the request is trusted only through its ECDSA signature.
        endpoints.MapGet("/api/ads/admob/verify", async (HttpContext context, AdMobRewardVerifier verifier,
            AdRewardService rewards, ILoggerFactory logs, CancellationToken ct) =>
        {
            var reward = await verifier.VerifyAsync(context.Request.QueryString.Value ?? string.Empty, ct);
            if (reward is null)
            {
                logs.CreateLogger("AdMob").LogWarning("Rejected an AdMob reward callback with an invalid signature.");
                return Results.BadRequest();
            }
            // AdMob only needs a 200; an unknown or expired reward is acknowledged so Google stops retrying.
            await rewards.GrantVerifiedAsync(reward, ct);
            return Results.Ok();
        });
    }

    private static IResult Refused(AdRewardException refused) => Results.Problem(
        statusCode: refused.Code == "ad_reward_not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict,
        title: "Ad reward unavailable.",
        extensions: new Dictionary<string, object?> { ["code"] = refused.Code });
}
