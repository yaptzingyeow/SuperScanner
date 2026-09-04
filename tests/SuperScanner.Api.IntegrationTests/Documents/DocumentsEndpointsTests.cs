using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SuperScanner.Application.Abstractions;
using SuperScanner.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SuperScanner.Api.IntegrationTests.Documents;

public sealed class DocumentsEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program>? _factory;

    public static TheoryData<string?> InvalidTitles => new()
    {
        null,
        "",
        "   ",
        new string('x', 201)
    };

    [Fact]
    public async Task List_ReturnsOnlyCurrentUsersDocuments()
    {
        using var userA = CreateAuthenticatedClient("user-a");
        using var userB = CreateAuthenticatedClient("user-b");
        var userACreate = await userA.PostAsJsonAsync("/api/documents", new { title = "A form" });
        var userBCreate = await userB.PostAsJsonAsync("/api/documents", new { title = "B form" });
        userACreate.EnsureSuccessStatusCode();
        userBCreate.EnsureSuccessStatusCode();

        var documents = await userA.GetFromJsonAsync<List<DocumentSummary>>("/api/documents");

        var document = Assert.Single(documents!);
        Assert.Equal("A form", document.Title);
    }

    [Theory]
    [MemberData(nameof(InvalidTitles))]
    public async Task Create_RejectsTitleOutsideOneToTwoHundredTrimmedCharacters(string? title)
    {
        using var client = CreateAuthenticatedClient("user-a");

        var response = await client.PostAsJsonAsync("/api/documents", new { title });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Create_TrimsTitleBeforeApplyingTheLengthLimit()
    {
        using var client = CreateAuthenticatedClient("user-a");
        var expectedTitle = new string('x', 200);

        var response = await client.PostAsJsonAsync(
            "/api/documents",
            new { title = $"  {expectedTitle}  " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<DocumentSummary>();
        Assert.Equal(expectedTitle, document!.Title);
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    "Audit:SigningKeyBase64",
                    Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()));
                builder.UseSetting("Audit:SigningKeyId", "api-test-key");
                builder.ConfigureLogging(logging => logging.ClearProviders());
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IRequestIdentityVerifier>();
                    services.AddSingleton<IRequestIdentityVerifier, FakeRequestIdentityVerifier>();
                    services.RemoveAll<DbContextOptions<AppDbContext>>();
                    services.RemoveAll<AppDbContext>();
                    services.AddDbContext<AppDbContext>(dbOptions => dbOptions.UseNpgsql(connectionString));
                });
            });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    private HttpClient CreateAuthenticatedClient(string firebaseUid)
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firebaseUid);
        client.DefaultRequestHeaders.Add("X-Firebase-AppCheck", "valid-app");
        return client;
    }

    private sealed record DocumentSummary(Guid Id, string Title, string Status, int PageCount, DateTimeOffset UpdatedAt);

    private sealed class FakeRequestIdentityVerifier : IRequestIdentityVerifier
    {
        public Task<VerifiedRequestIdentity> VerifyAsync(
            string idToken,
            string appCheckToken,
            CancellationToken cancellationToken) =>
            appCheckToken == "valid-app" && !string.IsNullOrWhiteSpace(idToken)
                ? Task.FromResult(new VerifiedRequestIdentity(idToken, $"{idToken}@example.test"))
                : Task.FromException<VerifiedRequestIdentity>(new UnauthorizedAccessException());
    }
}
