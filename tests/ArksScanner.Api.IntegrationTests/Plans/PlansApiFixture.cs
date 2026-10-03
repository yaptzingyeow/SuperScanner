using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ArksScanner.Application.Abstractions;
using ArksScanner.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace ArksScanner.Api.IntegrationTests.Plans;

/// <summary>An API wired to a real PostgreSQL with three test identities: owner (admin email), member and guest.</summary>
public sealed class PlansApiFixture : IAsyncDisposable
{
    public const string OwnerEmail = "owner@example.test";
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    private string connection = string.Empty;

    public static async Task<PlansApiFixture> StartAsync(Action<IWebHostBuilder>? configure = null)
    {
        var fixture = new PlansApiFixture();
        await fixture.postgres.StartAsync();
        fixture.connection = fixture.postgres.GetConnectionString();
        fixture.Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("Audit:SigningKeyBase64", Convert.ToBase64String(new byte[32]));
            builder.UseSetting("Audit:SigningKeyId", "plans-tests");
            builder.UseSetting("Admin:OwnerEmail", OwnerEmail);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRequestIdentityVerifier>();
                services.AddSingleton<IRequestIdentityVerifier, Identities>();
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options => options.UseNpgsql(fixture.connection));
            });
            configure?.Invoke(builder);
        });
        await using var db = fixture.Db();
        await db.Database.MigrateAsync();
        return fixture;
    }

    public AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);

    public HttpClient Client(string user)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user);
        client.DefaultRequestHeaders.Add("X-Firebase-AppCheck", "valid-app");
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await postgres.DisposeAsync();
    }

    private sealed class Identities : IRequestIdentityVerifier
    {
        public Task<VerifiedRequestIdentity> VerifyAsync(string idToken, string appCheckToken, CancellationToken ct) =>
            appCheckToken != "valid-app" ? Task.FromException<VerifiedRequestIdentity>(new UnauthorizedAccessException()) : idToken switch
            {
                "owner" => Task.FromResult(new VerifiedRequestIdentity("owner-uid", OwnerEmail, SignInProvider: "google.com", EmailVerified: true)),
                "member" => Task.FromResult(new VerifiedRequestIdentity("member-uid", "member@example.test", SignInProvider: "password", EmailVerified: true)),
                "impostor" => Task.FromResult(new VerifiedRequestIdentity("impostor-uid", OwnerEmail, SignInProvider: "password", EmailVerified: false)),
                "guest" => Task.FromResult(new VerifiedRequestIdentity("guest-uid", null, IsGuest: true, SignInProvider: "anonymous")),
                _ => Task.FromException<VerifiedRequestIdentity>(new UnauthorizedAccessException()),
            };
    }
}
