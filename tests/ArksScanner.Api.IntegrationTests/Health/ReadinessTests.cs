using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using ArksScanner.Application.Abstractions;
using ArksScanner.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace ArksScanner.Api.IntegrationTests.Health;

public sealed class ReadinessTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    [Fact]
    public async Task Ready_when_database_is_reachable_and_fully_migrated()
    {
        await using (var db = Db()) await db.Database.MigrateAsync();
        await using var factory = Factory(postgres.GetConnectionString());
        var response = await factory.CreateClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Not_ready_while_migrations_are_pending()
    {
        await using (var db = Db())
            await db.GetService<IMigrator>().MigrateAsync("20261003163640_PlansAndAdmin");
        await using var factory = Factory(postgres.GetConnectionString());
        var response = await factory.CreateClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("pending", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Degraded_but_up_when_the_app_role_cannot_read_migration_history()
    {
        await using (var db = Db())
        {
            await db.Database.MigrateAsync();
            // New roles can connect (PUBLIC has CONNECT) but cannot read any table.
            await db.Database.ExecuteSqlRawAsync("CREATE ROLE limited_app LOGIN PASSWORD 'limited'");
        }
        var limited = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
            { Username = "limited_app", Password = "limited" }.ConnectionString;
        await using var factory = Factory(limited);
        var response = await factory.CreateClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Degraded", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Not_ready_when_database_is_unreachable()
    {
        await using var factory = Factory("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2");
        var response = await factory.CreateClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(postgres.GetConnectionString()).Options);

    private static WebApplicationFactory<Program> Factory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Audit:SigningKeyBase64",
                Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()));
            builder.UseSetting("Audit:SigningKeyId", "api-test-key");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRequestIdentityVerifier>();
                services.AddSingleton<IRequestIdentityVerifier, FakeRequestIdentityVerifier>();
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
            });
        });

    public Task InitializeAsync() => postgres.StartAsync();

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    private sealed class FakeRequestIdentityVerifier : IRequestIdentityVerifier
    {
        public Task<VerifiedRequestIdentity> VerifyAsync(string idToken, string appCheckToken, CancellationToken cancellationToken) =>
            Task.FromException<VerifiedRequestIdentity>(new UnauthorizedAccessException());
    }
}
