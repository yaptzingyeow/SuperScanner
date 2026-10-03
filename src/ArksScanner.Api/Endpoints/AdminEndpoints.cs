using ArksScanner.Api.Auth;
using ArksScanner.Domain.Plans;
using ArksScanner.Infrastructure.Admin;

namespace ArksScanner.Api.Endpoints;

public static class AdminEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin")
            .RequireAuthorization() // the admin filter turns guests and non-admins away with 404
            .AddEndpointFilter<AdminOnlyFilter>()
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "private, no-store";
                try { return await next(context); }
                catch (AdminNotFoundException) { return Results.NotFound(); }
                catch (LastAdminException)
                {
                    return Results.Conflict(new { code = "last_admin", title = "At least one admin must remain." });
                }
                catch (InvalidOperationException ex) { return Results.Conflict(new { code = "invalid_state", title = ex.Message }); }
                catch (ArgumentException ex) { return Results.BadRequest(new { code = "invalid_request", title = ex.Message }); }
            });

        group.MapGet("/dashboard", (AdminService admin, CancellationToken ct) => admin.DashboardAsync(ct));
        group.MapGet("/users", (string? query, int? page, AdminService admin, CancellationToken ct) =>
            admin.UsersAsync(query, page ?? 1, ct));
        group.MapGet("/users/{uid}", async (string uid, AdminService admin, CancellationToken ct) =>
        {
            var detail = await admin.UserAsync(uid, ct);
            return Results.Ok(new
            {
                detail.User.Uid, detail.User.Email, detail.User.Provider, detail.User.IsGuest, detail.User.CreatedAt,
                detail.User.LastSeenAt, detail.User.Plan, detail.IsAdmin, detail.DocumentCount, detail.Usage, detail.Subscriptions,
            });
        });
        group.MapPost("/users/{uid}/subscriptions", (string uid, GrantRequest request, ICurrentUser user, AdminService admin,
            CancellationToken ct) => admin.GrantAsync(user.FirebaseUid, uid, request.Duration ?? string.Empty, request.Note, ct));
        group.MapGet("/subscriptions", (int? page, AdminService admin, CancellationToken ct) => admin.SubscriptionsAsync(page ?? 1, ct));
        group.MapPost("/subscriptions/{id:guid}/revoke", async (Guid id, ICurrentUser user, AdminService admin, CancellationToken ct) =>
        {
            await admin.RevokeAsync(user.FirebaseUid, id, ct);
            return Results.NoContent();
        });
        group.MapPost("/subscriptions/{id:guid}/extend", (Guid id, ExtendRequest request, ICurrentUser user, AdminService admin,
            CancellationToken ct) => admin.ExtendAsync(user.FirebaseUid, id, request.Duration ?? string.Empty, ct));
        group.MapGet("/payments", (int? page, AdminService admin, CancellationToken ct) => admin.PaymentsAsync(page ?? 1, ct));
        group.MapGet("/settings", async (AdminService admin, CancellationToken ct) => SettingsDto.From(await admin.SettingsAsync(ct)));
        group.MapPut("/settings", async (SettingsDto request, ICurrentUser user, AdminService admin, CancellationToken ct) =>
            SettingsDto.From(await admin.UpdateSettingsAsync(user.FirebaseUid, request.ToValues(), ct)));
        group.MapGet("/admins", (AdminService admin, CancellationToken ct) => admin.AdminsAsync(ct));
        group.MapPost("/admins", (AddAdminRequest request, ICurrentUser user, AdminService admin, CancellationToken ct) =>
            admin.AddAdminAsync(user.FirebaseUid, request.Email ?? string.Empty, ct));
        group.MapDelete("/admins/{uid}", async (string uid, ICurrentUser user, AdminService admin, CancellationToken ct) =>
        {
            await admin.RemoveAdminAsync(user.FirebaseUid, uid, ct);
            return Results.NoContent();
        });
        group.MapGet("/audit", async (int? limit, AdminService admin, CancellationToken ct) =>
            Results.Ok(new { items = await admin.AuditAsync(limit ?? 20, ct) }));
    }

    private sealed record GrantRequest(string? Duration, string? Note);
    private sealed record ExtendRequest(string? Duration);
    private sealed record AddAdminRequest(string? Email);

    private sealed record SettingsDto(string Phase, DateTimeOffset? EnforceFromUtc, int FreeOcrPagesPerDay,
        int FreeWatermarkExportsPerDay, int FreeMaxDocuments, int FreeRetentionDays, string UsageTimeZone,
        decimal OcrCostPerThousandPages)
    {
        public static SettingsDto From(PlanSettingsValues v) => new(v.Phase.ToString(), v.EnforceFromUtc, v.FreeOcrPagesPerDay,
            v.FreeWatermarkExportsPerDay, v.FreeMaxDocuments, v.FreeRetentionDays, v.UsageTimeZone, v.OcrCostPerThousandPages);

        public PlanSettingsValues ToValues() => new(
            Enum.TryParse<PlanPhase>(Phase, true, out var phase) ? phase : throw new ArgumentException("Unknown phase."),
            EnforceFromUtc, FreeOcrPagesPerDay, FreeWatermarkExportsPerDay, FreeMaxDocuments, FreeRetentionDays,
            UsageTimeZone, OcrCostPerThousandPages);
    }
}
