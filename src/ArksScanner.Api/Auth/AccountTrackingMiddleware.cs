using System.Security.Claims;
using ArksScanner.Application.Plans;

namespace ArksScanner.Api.Auth;

/// <summary>Records signed-in visitors as accounts. Best effort: a failure never blocks the request.</summary>
public sealed class AccountTrackingMiddleware(RequestDelegate next, ILogger<AccountTrackingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IAccountDirectory accounts)
    {
        var user = context.User;
        var uid = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(uid))
        {
            try
            {
                // Only a verified email is recorded: admin rights (owner seed, Add admin) match on it.
                var email = user.HasClaim(FirebaseAuthenticationHandler.EmailVerifiedClaimType, "true")
                    ? user.FindFirstValue(ClaimTypes.Email) : null;
                await accounts.TouchAsync(uid, email,
                    user.FindFirstValue(FirebaseAuthenticationHandler.ProviderClaimType),
                    user.HasClaim(FirebaseAuthenticationHandler.GuestClaimType, "true"), context.RequestAborted);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning("Account tracking failed. ErrorCode={ErrorCode}", "account_tracking_failed");
            }
        }

        await next(context);
    }
}

/// <summary>Admin routes answer 404 to anyone who is not an admin, so the portal is not discoverable.</summary>
public sealed class AdminOnlyFilter(IAccountDirectory accounts) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.User;
        var uid = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(uid) || user.HasClaim(FirebaseAuthenticationHandler.GuestClaimType, "true") ||
            !await accounts.IsAdminAsync(uid, context.HttpContext.RequestAborted))
            return Results.NotFound();
        return await next(context);
    }
}
