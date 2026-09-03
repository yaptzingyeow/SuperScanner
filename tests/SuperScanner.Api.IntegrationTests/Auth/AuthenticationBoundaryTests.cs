using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SuperScanner.Application.Abstractions;

namespace SuperScanner.Api.IntegrationTests.Auth;

public sealed class AuthenticationBoundaryTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthenticationBoundaryTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureLogging(logging => logging.ClearProviders());
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IRequestIdentityVerifier>();
                    services.AddSingleton<IRequestIdentityVerifier, FakeRequestIdentityVerifier>();
                });
            });
    }

    [Fact]
    public async Task Me_RejectsMissingAppCheckToken()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "valid-user-a");

        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_ReturnsVerifiedFirebaseUid()
    {
        using var client = CreateClient("valid-user-a", "valid-app");

        var body = await client.GetFromJsonAsync<MeResponse>("/api/me");

        Assert.Equal("user-a", body!.FirebaseUid);
    }

    [Theory]
    [InlineData(null, "valid-app")]
    [InlineData("expired-user-token", "valid-app")]
    [InlineData("malformed-user-token", "valid-app")]
    [InlineData("valid-user-a", "expired-app-token")]
    [InlineData("valid-user-a", "valid-app-other-project")]
    public async Task Me_RejectsUnverifiedTokenPairs(string? idToken, string appCheckToken)
    {
        using var client = CreateClient(idToken, appCheckToken);

        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient(string? idToken, string appCheckToken)
    {
        var client = _factory.CreateClient();
        if (idToken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        }

        client.DefaultRequestHeaders.Add("X-Firebase-AppCheck", appCheckToken);
        return client;
    }

    private sealed record MeResponse(string FirebaseUid);

    private sealed class FakeRequestIdentityVerifier : IRequestIdentityVerifier
    {
        public Task<VerifiedRequestIdentity> VerifyAsync(
            string idToken,
            string appCheckToken,
            CancellationToken cancellationToken)
        {
            if (idToken == "valid-user-a" && appCheckToken == "valid-app")
            {
                return Task.FromResult(new VerifiedRequestIdentity("user-a", "user-a@example.test"));
            }

            return Task.FromException<VerifiedRequestIdentity>(
                new UnauthorizedAccessException("The test token pair is invalid."));
        }
    }
}
