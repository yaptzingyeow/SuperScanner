using ArksScanner.Api.Auth;
using ArksScanner.Application.Plans;

namespace ArksScanner.Api.Endpoints;

public static class PlanEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me/plan", async (ICurrentUser user, PlanService plans, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var entitlements = await plans.GetEntitlementsAsync(user.FirebaseUid, ct);
            var usage = await plans.GetUsageAsync(user.FirebaseUid, ct);
            return Results.Ok(new
            {
                plan = entitlements.Plan.ToString(),
                phase = entitlements.Phase.ToString(),
                proUntil = entitlements.ProUntil,
                proForever = entitlements.ProForever,
                brandStamp = entitlements.BrandStamp,
                limits = new
                {
                    ocrPagesPerDay = entitlements.OcrPagesPerDay,
                    watermarkExportsPerDay = entitlements.WatermarkExportsPerDay,
                    maxDocuments = entitlements.MaxDocuments,
                    retentionDays = entitlements.RetentionDays,
                },
                usage = new
                {
                    ocrPages = usage.OcrPages,
                    bonusOcrPages = usage.BonusOcrPages,
                    watermarkExports = usage.WatermarkExports,
                    resetsAt = usage.ResetsAt,
                },
                documentCount = await plans.CountActiveDocumentsAsync(user.FirebaseUid, ct),
            });
        }).RequireAuthorization();
    }
}

/// <summary>The 429 "plan_limit_reached" problem shared by every limited endpoint.</summary>
public static class PlanProblem
{
    public static IResult From(PlanLimitExceededException limit) => Results.Problem(
        statusCode: StatusCodes.Status429TooManyRequests,
        title: "Plan limit reached.",
        extensions: new Dictionary<string, object?>
        {
            ["code"] = "plan_limit_reached",
            ["kind"] = limit.KindCode,
            ["limit"] = limit.Limit,
            ["used"] = limit.Used,
            ["resetsAt"] = limit.ResetsAt,
        });
}
