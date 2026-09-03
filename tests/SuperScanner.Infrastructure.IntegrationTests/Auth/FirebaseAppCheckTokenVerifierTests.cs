using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SuperScanner.Infrastructure.Auth;

namespace SuperScanner.Infrastructure.IntegrationTests.Auth;

public sealed class FirebaseAppCheckTokenVerifierTests : IDisposable
{
    private const string ProjectNumber = "1234567890";
    private const string AppId = "1:1234567890:web:abc123";
    private const string KeyId = "test-key";
    private readonly RSA _rsa = RSA.Create(2048);

    [Fact]
    public async Task VerifyAsync_ReturnsAllowedAppIdForValidSignedToken()
    {
        var verifier = CreateVerifier();
        var token = CreateToken();

        var verifiedAppId = await verifier.VerifyAsync(token, CancellationToken.None);

        Assert.Equal(AppId, verifiedAppId);
    }

    [Theory]
    [InlineData(InvalidToken.Issuer)]
    [InlineData(InvalidToken.Audience)]
    [InlineData(InvalidToken.Expired)]
    [InlineData(InvalidToken.AppId)]
    [InlineData(InvalidToken.Type)]
    [InlineData(InvalidToken.Algorithm)]
    public async Task VerifyAsync_RejectsTokenOutsideConfiguredFirebaseApp(InvalidToken invalidToken)
    {
        var verifier = CreateVerifier();
        var token = CreateToken(invalidToken);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => verifier.VerifyAsync(token, CancellationToken.None));
    }

    [Fact]
    public async Task VerifyAsync_RejectsTokenSignedByUnknownKey()
    {
        var verifier = CreateVerifier();
        using var otherKey = RSA.Create(2048);
        var token = CreateToken(signingKey: otherKey);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => verifier.VerifyAsync(token, CancellationToken.None));
    }

    public void Dispose() => _rsa.Dispose();

    private FirebaseAppCheckTokenVerifier CreateVerifier()
    {
        var handler = new StaticJwksHandler(CreateJwks());
        var options = Options.Create(new FirebaseAuthOptions
        {
            ProjectNumber = ProjectNumber,
            AllowedAppIds = [AppId]
        });

        return new FirebaseAppCheckTokenVerifier(new HttpClient(handler), options);
    }

    private string CreateToken(InvalidToken invalidToken = InvalidToken.None, RSA? signingKey = null)
    {
        var now = DateTime.UtcNow;
        var key = new RsaSecurityKey(signingKey ?? _rsa) { KeyId = KeyId };
        SigningCredentials credentials = invalidToken == InvalidToken.Algorithm
            ? new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("a-test-key-that-is-at-least-thirty-two-bytes")) { KeyId = KeyId },
                SecurityAlgorithms.HmacSha256)
            : new SigningCredentials(key, SecurityAlgorithms.RsaSha256);

        var header = new JwtHeader(credentials)
        {
            [JwtHeaderParameterNames.Typ] = invalidToken == InvalidToken.Type ? "NOT-JWT" : "JWT"
        };
        var payload = new JwtPayload(
            invalidToken == InvalidToken.Issuer
                ? "https://firebaseappcheck.googleapis.com/9999999999"
                : $"https://firebaseappcheck.googleapis.com/{ProjectNumber}",
            invalidToken == InvalidToken.Audience ? "projects/9999999999" : $"projects/{ProjectNumber}",
            [new Claim(JwtRegisteredClaimNames.Sub, invalidToken == InvalidToken.AppId ? "other-app" : AppId)],
            invalidToken == InvalidToken.Expired ? now.AddMinutes(-10) : now.AddMinutes(-1),
            invalidToken == InvalidToken.Expired ? now.AddMinutes(-1) : now.AddMinutes(30),
            now);

        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
    }

    private string CreateJwks()
    {
        var parameters = _rsa.ExportParameters(false);
        return JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    alg = "RS256",
                    kid = KeyId,
                    n = Base64UrlEncoder.Encode(parameters.Modulus),
                    e = Base64UrlEncoder.Encode(parameters.Exponent)
                }
            }
        });
    }

    public enum InvalidToken
    {
        None,
        Issuer,
        Audience,
        Expired,
        AppId,
        Type,
        Algorithm
    }

    private sealed class StaticJwksHandler(string jwks) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jwks, Encoding.UTF8, "application/json")
            });
    }
}
