using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SuperScanner.Application.Abstractions;

namespace SuperScanner.Api.IntegrationTests.Auth;

/// <summary>Guests (anonymous Firebase sign-in) must log in before editing page text, signatures or marks.</summary>
public sealed class GuestAccessTests : IDisposable
{
    private const string Page = "/api/documents/11111111-1111-1111-1111-111111111111/pages/22222222-2222-2222-2222-222222222222";
    private readonly WebApplicationFactory<Program> _factory;

    public GuestAccessTests()
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

    [Theory]
    [InlineData("GET", "/text-edits/history")]
    [InlineData("POST", "/text-edits/style-proposal")]
    [InlineData("POST", "/text-edits/preview")]
    [InlineData("POST", "/text-edits")]
    [InlineData("GET", "/signatures")]
    [InlineData("POST", "/signatures")]
    [InlineData("GET", "/marks")]
    [InlineData("POST", "/marks")]
    public async Task Guests_are_refused_page_editing(string method, string path)
    {
        using var guest = CreateClient("guest");

        using var response = await guest.SendAsync(new HttpRequestMessage(new HttpMethod(method), Page + path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/text-edits/history")]
    [InlineData("GET", "/signatures")]
    [InlineData("GET", "/marks")]
    public async Task Signed_in_accounts_pass_the_guest_check(string method, string path)
    {
        using var member = CreateClient("member");

        using var response = await member.SendAsync(new HttpRequestMessage(new HttpMethod(method), Page + path));

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Guests_can_still_use_the_rest_of_the_app()
    {
        using var guest = CreateClient("guest");

        using var response = await guest.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient(string idToken)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        client.DefaultRequestHeaders.Add("X-Firebase-AppCheck", "valid-app");
        return client;
    }

    private sealed class FakeRequestIdentityVerifier : IRequestIdentityVerifier
    {
        public Task<VerifiedRequestIdentity> VerifyAsync(string idToken, string appCheckToken, CancellationToken cancellationToken) =>
            idToken switch
            {
                "guest" => Task.FromResult(new VerifiedRequestIdentity("guest-uid", null, IsGuest: true)),
                "member" => Task.FromResult(new VerifiedRequestIdentity("member-uid", "member@example.test")),
                _ => Task.FromException<VerifiedRequestIdentity>(new UnauthorizedAccessException()),
            };
    }
}
