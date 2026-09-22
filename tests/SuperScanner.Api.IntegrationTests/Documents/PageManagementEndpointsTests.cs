using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;
using SuperScanner.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SuperScanner.Api.IntegrationTests.Documents;

// Runs the actual HTTP pipeline and commands with an in-memory repository; no database lock claims.
public sealed class PageManagementEndpointsTests : IDisposable
{
    private readonly PageManagementRepository repository = new();
    private readonly WebApplicationFactory<Program> factory;

    public PageManagementEndpointsTests()
    {
        factory = PageManagementHttp.CreateFactory(services =>
        {
            services.RemoveAll<IDocumentRepository>();
            services.AddSingleton<IDocumentRepository>(repository);
            services.RemoveAll<IAuditWriter>();
            services.AddSingleton<IAuditWriter, NullAudit>();
        });
    }

    [Fact]
    public async Task Reorder_ReturnsNewOrderAndRevisions_ThenRejectsStaleRequest()
    {
        using var client = PageManagementHttp.Client(factory);
        var ids = repository.Document.ActivePages.Select(p => p.Id).Reverse().ToArray();
        var url = $"/api/documents/{repository.Document.Id}/page-order";
        var response = await client.PutAsJsonAsync(url, new { expectedPageOrderRevision = 1, pageIds = ids });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ids, body.GetProperty("pages").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()));
        Assert.Equal(2, body.GetProperty("revision").GetInt64());
        Assert.Equal(2, body.GetProperty("pageOrderRevision").GetInt64());
        var stale = await client.PutAsJsonAsync(url, new { expectedPageOrderRevision = 1, pageIds = ids });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(2, (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("pageOrderRevision").GetInt64());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"expectedPageOrderRevision\":1,\"pageIds\":[]}")]
    [InlineData("{\"expectedPageOrderRevision\":1,\"pageIds\":null}")]
    [InlineData("{\"expectedPageOrderRevision\":-1,\"pageIds\":[]}")]
    public async Task MalformedOrder_ReturnsValidationDetails(string json)
    {
        using var client = PageManagementHttp.Client(factory);
        var response = await client.PutAsync($"/api/documents/{repository.Document.Id}/page-order",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task Delete_ReturnsNoContent_ThenHidesRemovedPage()
    {
        using var client = PageManagementHttp.Client(factory);
        var url = $"/api/documents/{repository.Document.Id}/pages/{repository.Document.ActivePages.First().Id}";
        var response = await client.DeleteAsync(url);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnownedOrMissingDocuments_AreHidden(bool missing)
    {
        using var client = PageManagementHttp.Client(factory, "user-b");
        var id = missing ? Guid.NewGuid() : repository.Document.Id;
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/documents/{id}/page-order",
            new { expectedPageOrderRevision = 1, pageIds = Array.Empty<Guid>() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(
            $"/api/documents/{id}/pages/{repository.Document.ActivePages.First().Id}")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Routes_RequireAuthenticationAndAppCheck(bool missingIdentity)
    {
        using var client = PageManagementHttp.Client(factory, missingIdentity ? null : "user-a", appCheck: false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync(
            $"/api/documents/{repository.Document.Id}/page-order", new { expectedPageOrderRevision = 1, pageIds = Array.Empty<Guid>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync(
            $"/api/documents/{repository.Document.Id}/pages/{repository.Document.ActivePages.First().Id}")).StatusCode);
    }

    public void Dispose() => factory.Dispose();

    private sealed class NullAudit : IAuditWriter
    {
        public Task<Guid> AppendAsync(AuditWriteRequest request, CancellationToken ct) => Task.FromResult(Guid.NewGuid());
    }
}

public sealed class PostgreSqlPageManagementEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> factory = null!;

    [Fact]
    public async Task ConcurrentReorders_OneWinsOneConflicts_AndRemoveCompactsPersistedOrder()
    {
        var document = PageManagementHttp.CreateDocument();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Add(document);
            await db.SaveChangesAsync();
        }
        using var client = PageManagementHttp.Client(factory);
        var ids = document.ActivePages.Select(p => p.Id).ToArray();
        var results = await Task.WhenAll(
            client.PutAsJsonAsync($"/api/documents/{document.Id}/page-order", new { expectedPageOrderRevision = 1, pageIds = new[] { ids[2], ids[0], ids[1] } }),
            client.PutAsJsonAsync($"/api/documents/{document.Id}/page-order", new { expectedPageOrderRevision = 1, pageIds = new[] { ids[1], ids[2], ids[0] } }));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        var conflict = Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(2, (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("pageOrderRevision").GetInt64());
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/documents/{document.Id}/pages/{ids[0]}")).StatusCode);
        await using var verification = factory.Services.CreateAsyncScope();
        var persisted = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await persisted.Documents.Include(d => d.Pages).SingleAsync(d => d.Id == document.Id);
        Assert.Equal(new[] { 1, 2 }, saved.ActivePages.Select(p => p.Position));
        Assert.Equal(3, saved.PageOrderRevision);
        Assert.Equal(3, saved.Revision);
        Assert.Equal(2, await persisted.AuditEvents.CountAsync(e => e.TargetId == document.Id));
    }

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        var connection = postgres.GetConnectionString();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options))
            await db.Database.MigrateAsync();
        factory = PageManagementHttp.CreateFactory(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connection));
        });
    }

    public async Task DisposeAsync()
    {
        if (factory is not null) await factory.DisposeAsync();
        await postgres.DisposeAsync();
    }
}

internal static class PageManagementHttp
{
    public static Document CreateDocument()
    {
        var document = Document.Create(Guid.NewGuid(), "user-a", "Pages", DateTimeOffset.UtcNow);
        document.AppendImportedPages(Guid.NewGuid(), [1, 2, 3], 50, DateTimeOffset.UtcNow);
        return document;
    }

    public static WebApplicationFactory<Program> CreateFactory(Action<IServiceCollection> configure) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("Audit:SigningKeyBase64", Convert.ToBase64String(new byte[32]));
            builder.UseSetting("Audit:SigningKeyId", "page-tests");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRequestIdentityVerifier>();
                services.AddSingleton<IRequestIdentityVerifier, IdentityVerifier>();
                configure(services);
            });
        });

    public static HttpClient Client(WebApplicationFactory<Program> factory, string? user = "user-a", bool appCheck = true)
    {
        var client = factory.CreateClient();
        if (user is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user);
        if (appCheck) client.DefaultRequestHeaders.Add("X-Firebase-AppCheck", "valid-app");
        return client;
    }

    private sealed class IdentityVerifier : IRequestIdentityVerifier
    {
        public Task<VerifiedRequestIdentity> VerifyAsync(string idToken, string appCheckToken, CancellationToken ct) =>
            appCheckToken == "valid-app" && idToken is "user-a" or "user-b"
                ? Task.FromResult(new VerifiedRequestIdentity(idToken, "test@example.test"))
                : Task.FromException<VerifiedRequestIdentity>(new UnauthorizedAccessException());
    }
}

internal sealed class PageManagementRepository : IDocumentRepository
{
    public Document Document { get; } = PageManagementHttp.CreateDocument();
    public Task AddAsync(Document document, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<Document>> ListByOwnerAsync(string owner, CancellationToken ct) => throw new NotSupportedException();
    public Task<IDocumentTransaction> BeginTransactionAsync(CancellationToken ct) => Task.FromResult<IDocumentTransaction>(new Transaction());
    public Task<Document?> FindOwnedForUpdateAsync(string owner, Guid id, CancellationToken ct) =>
        Task.FromResult(Document.OwnerFirebaseUid == owner && Document.Id == id ? Document : null);
    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    private sealed class Transaction : IDocumentTransaction
    {
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
