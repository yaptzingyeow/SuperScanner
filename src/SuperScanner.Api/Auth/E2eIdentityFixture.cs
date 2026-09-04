using SuperScanner.Application.Abstractions;

namespace SuperScanner.Api.Auth;

public sealed class E2eRequestIdentityVerifier : IRequestIdentityVerifier
{
    internal const string AppCheckToken = "e2e-app-check";

    public Task<VerifiedRequestIdentity> VerifyAsync(
        string idToken,
        string appCheckToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(appCheckToken, AppCheckToken, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("The E2E token pair is invalid.");
        }

        var user = idToken switch
        {
            "e2e-user-a" => "user-a",
            "e2e-user-b" => "user-b",
            _ => throw new UnauthorizedAccessException("The E2E token pair is invalid.")
        };

        return Task.FromResult(
            new VerifiedRequestIdentity(user, $"{user}@e2e.invalid"));
    }
}

public static class E2eIdentityFixture
{
    public const string ConfigurationKey = "E2E:IdentityFixtureEnabled";

    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/e2e/identity", (string user) => user switch
        {
            "user-a" => Results.Ok(CreateTokens("e2e-user-a")),
            "user-b" => Results.Ok(CreateTokens("e2e-user-b")),
            _ => Results.BadRequest()
        });

    private static object CreateTokens(string identityToken) => new
    {
        identityToken,
        appCheckToken = E2eRequestIdentityVerifier.AppCheckToken
    };
}
