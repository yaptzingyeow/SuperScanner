using Microsoft.AspNetCore.Authorization;

namespace SuperScanner.Api.Auth;

public sealed class AppCheckMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var requiresAuthorization = context.GetEndpoint()?.Metadata.GetMetadata<IAuthorizeData>() is not null;
        if (requiresAuthorization && !HasSingleNonEmptyAppCheckHeader(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }

    private static bool HasSingleNonEmptyAppCheckHeader(HttpRequest request) =>
        request.Headers.TryGetValue(FirebaseAuthenticationHandler.AppCheckHeaderName, out var values) &&
        values.Count == 1 &&
        !string.IsNullOrWhiteSpace(values[0]);
}
