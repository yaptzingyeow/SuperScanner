using Microsoft.EntityFrameworkCore;
using ArksScanner.Infrastructure.Privacy;
using ArksScanner.Api.Auth;
using ArksScanner.Application.Plans;

namespace ArksScanner.Api.Endpoints;

public static class PlanEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me/plan", async (ICurrentUser user, PlanService plans, IAccountDirectory accounts,
            ArksScanner.Infrastructure.Persistence.AppDbContext db, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            var entitlements = await plans.GetEntitlementsAsync(user.FirebaseUid, ct);
            var usage = await plans.GetUsageAsync(user.FirebaseUid, ct);
            var region = await plans.GetRegionAsync(user.FirebaseUid, ct);
            var consent = await db.Accounts.AsNoTracking().Where(a => a.FirebaseUid == user.FirebaseUid)
                .Select(a => a.PrivacyConsentVersion).SingleOrDefaultAsync(ct);
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
                isAdmin = await accounts.IsAdminAsync(user.FirebaseUid, ct),
                timeZone = region.TimeZone,
                privacyConsentVersion = consent,
                currentPrivacyVersion = PrivacyNotice.CurrentVersion,
                locale = region.Locale,
            });
        }).RequireAuthorization();

        endpoints.MapGet("/api/me/export", async (ICurrentUser user, PrivacyService privacy, HttpContext context,
            CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.ContentDisposition = "attachment; filename=\"arks-scanner-my-data.json\"";
            return Results.Json(await privacy.ExportAsync(user.FirebaseUid, ct));
        }).RequireAuthorization();

        endpoints.MapPost("/api/me/consent", async (ConsentRequest request, ICurrentUser user, PrivacyService privacy,
            CancellationToken ct) =>
        {
            if (request.Version != PrivacyNotice.CurrentVersion)
                return Results.BadRequest(new { code = "privacy_version_outdated" });
            await privacy.RecordConsentAsync(user.FirebaseUid, request.Version, ct);
            return Results.NoContent();
        }).RequireAuthorization();

        endpoints.MapDelete("/api/me", async ([Microsoft.AspNetCore.Mvc.FromBody] DeleteAccountRequest request, ICurrentUser user, PrivacyService privacy,
            CancellationToken ct) =>
        {
            if (request.Confirm != "DELETE") return Results.BadRequest(new { code = "confirmation_required" });
            try { await privacy.DeleteAccountAsync(user.FirebaseUid, ct); }
            catch (AccountDeletionBlockedException)
            {
                return Results.Conflict(new { code = "last_admin", title = "Add another admin before deleting your account." });
            }
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private sealed record ConsentRequest(string? Version);
    private sealed record DeleteAccountRequest(string? Confirm);
}

/// <summary>The privacy notice people accept; bump the version when the notice changes so everyone is asked again.</summary>
public static class PrivacyNotice
{
    public const string CurrentVersion = "2026-10";
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
