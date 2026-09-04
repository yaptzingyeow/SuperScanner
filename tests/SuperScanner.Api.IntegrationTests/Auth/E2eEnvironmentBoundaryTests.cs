using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SuperScanner.Application.Abstractions;

namespace SuperScanner.Api.IntegrationTests.Auth;

public sealed class E2eEnvironmentBoundaryTests
{
    public static TheoryData<string> NonE2eEnvironments => new()
    {
        Environments.Development,
        Environments.Staging,
        Environments.Production
    };

    [Theory]
    [MemberData(nameof(NonE2eEnvironments))]
    public async Task IdentityFixture_IsAbsentOutsideE2e(string environment)
    {
        using var factory = CreateFactory(environment);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/e2e/identity?user=user-a");
        var verifiers = factory.Services.GetServices<IRequestIdentityVerifier>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(verifiers, verifier => verifier.GetType().Name == "E2eRequestIdentityVerifier");
    }

    [Theory]
    [MemberData(nameof(NonE2eEnvironments))]
    public void IdentityFixtureConfiguration_FailsStartupOutsideE2e(string environment)
    {
        using var factory = CreateFactory(environment, enableFixture: true);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("E2E identity configuration", exception.ToString(), StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string environment,
        bool enableFixture = false) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.ConfigureLogging(logging => logging.ClearProviders());
                builder.UseSetting("Firebase:ProjectId", "boundary-test");
                builder.UseSetting("Firebase:ProjectNumber", "123456789");
                builder.UseSetting("Firebase:AllowedAppIds:0", "1:123456789:web:boundary");
                if (enableFixture)
                {
                    builder.UseSetting("E2E:IdentityFixtureEnabled", "true");
                }
            });
}
