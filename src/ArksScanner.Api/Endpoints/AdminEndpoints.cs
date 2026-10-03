using ArksScanner.Api.Auth;

namespace ArksScanner.Api.Endpoints;

public static class AdminEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin")
            .RequireAuthorization() // the admin filter turns guests and non-admins away with 404
            .AddEndpointFilter<AdminOnlyFilter>();
        // Placeholder until the admin API (Task 7) replaces it.
        group.MapGet("/ping", () => Results.Ok(new { ok = true }));
    }
}
