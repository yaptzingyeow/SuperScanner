using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using SuperScanner.Application.Abstractions;

namespace SuperScanner.Api.Auth;

public sealed class FirebaseAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IRequestIdentityVerifier identityVerifier)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Firebase";
    public const string AppCheckHeaderName = "X-Firebase-AppCheck";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!TryReadBearerToken(out var idToken) || !TryReadSingleHeader(AppCheckHeaderName, out var appCheckToken))
        {
            return AuthenticateResult.Fail("Valid Firebase identity and App Check tokens are required.");
        }

        try
        {
            var identity = await identityVerifier.VerifyAsync(idToken, appCheckToken, Context.RequestAborted);
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, identity.FirebaseUid) };

            if (!string.IsNullOrWhiteSpace(identity.Email))
            {
                claims.Add(new Claim(ClaimTypes.Email, identity.Email));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
        }
        catch (OperationCanceledException) when (Context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or UnauthorizedAccessException)
        {
            return AuthenticateResult.Fail("Valid Firebase identity and App Check tokens are required.");
        }
    }

    private bool TryReadBearerToken(out string token)
    {
        token = string.Empty;
        if (!TryReadSingleHeader(HeaderNames.Authorization, out var authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        token = authorization["Bearer ".Length..].Trim();
        return token.Length > 0 && !token.Contains(' ');
    }

    private bool TryReadSingleHeader(string name, out string value)
    {
        value = string.Empty;
        if (!Request.Headers.TryGetValue(name, out var values) || values.Count != 1)
        {
            return false;
        }

        value = values[0]?.Trim() ?? string.Empty;
        return value.Length > 0;
    }
}
