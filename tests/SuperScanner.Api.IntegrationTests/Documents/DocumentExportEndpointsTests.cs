using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;
using SuperScanner.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SuperScanner.Api.IntegrationTests.Documents;

public sealed class DocumentExportEndpointsTests : IDisposable
{
    private readonly PageManagementRepository documents = new();
    private readonly ExportRepository exports;
    private readonly WebApplicationFactory<Program> factory;

    public DocumentExportEndpointsTests()
    {
        var page = documents.Document.ActivePages.First();
        page.SetPreview("private/preview", "private/thumbnail");
        page.MarkReady();
        exports = new ExportRepository(documents.Document);
        factory = PageManagementHttp.CreateFactory(services =>
        {
            services.RemoveAll<IDocumentRepository>();
            services.AddSingleton<IDocumentRepository>(documents);
            services.RemoveAll<IDocumentExportRepository>();
            services.AddSingleton<IDocumentExportRepository>(exports);
            services.RemoveAll<IAuditWriter>();
            services.AddSingleton<IAuditWriter, NullAudit>();
            services.RemoveAll<IProcessingJobQueue>();
            services.AddSingleton<IProcessingJobQueue, Queue>();
        });
    }

    [Fact]
    public async Task CreateAndStatus_ReturnAcceptedLocationCountsAndOutdatedState()
    {
        using var client = PageManagementHttp.Client(factory);
        var response = await client.PostAsync($"/api/documents/{documents.Document.Id}/exports", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var statusUrl = body.GetProperty("statusUrl").GetString()!;
        Assert.Equal(statusUrl, response.Headers.Location!.OriginalString);
        Assert.Equal("Queued", body.GetProperty("state").GetString());
        Assert.Equal(1, body.GetProperty("readyPageCount").GetInt32());
        Assert.Equal(2, body.GetProperty("excludedPageCount").GetInt32());
        Assert.False(body.GetProperty("isOutdated").GetBoolean());
        documents.Document.MarkContentChanged(DateTimeOffset.UtcNow);
        var status = await client.GetAsync(statusUrl);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.True((await status.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isOutdated").GetBoolean());
        Assert.Contains("private", status.Headers.CacheControl!.ToString());
        Assert.True(status.Headers.CacheControl.NoStore);
        var json = await status.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private/", json);
        Assert.DoesNotContain("snapshot", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("objectKey", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NoReadyPages_Returns422_WithoutCreatingExport()
    {
        documents.Document.ActivePages.First().MarkProcessing();
        using var client = PageManagementHttp.Client(factory);
        var response = await client.PostAsync($"/api/documents/{documents.Document.Id}/exports", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(exports.Items);
    }

    [Fact]
    public async Task CreateAndStatus_HideUnownedMissingAndMismatchedResources()
    {
        using var owner = PageManagementHttp.Client(factory);
        using var other = PageManagementHttp.Client(factory, "user-b");
        var created = await owner.PostAsync($"/api/documents/{documents.Document.Id}/exports", null);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        foreach (var documentId in new[] { documents.Document.Id, Guid.NewGuid() })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/documents/{documentId}/exports", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/documents/{documentId}/exports/{id}")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/documents/{Guid.NewGuid()}/exports/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/documents/{documents.Document.Id}/exports/{Guid.NewGuid()}")).StatusCode);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("user-a", false)]
    [InlineData("invalid-user", true)]
    public async Task Routes_RequireVerifiedIdentityAndAppCheck(string? user, bool appCheck)
    {
        using var client = PageManagementHttp.Client(factory, user, appCheck);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync($"/api/documents/{documents.Document.Id}/exports", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/documents/{documents.Document.Id}/exports/{Guid.NewGuid()}")).StatusCode);
        Assert.Empty(exports.Items);
    }

    public void Dispose() => factory.Dispose();

    private sealed class ExportRepository(Document document) : IDocumentExportRepository
    {
        public List<DocumentExport> Items { get; } = [];
        public Task AddAsync(DocumentExport export, CancellationToken ct) { Items.Add(export); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<OwnedDocumentExport?> FindOwnedAsync(string owner, Guid documentId, Guid exportId, CancellationToken ct)
        {
            var export = Items.SingleOrDefault(e => e.Id == exportId && e.DocumentId == documentId && e.OwnerFirebaseUid == owner);
            return Task.FromResult(export is null || document.OwnerFirebaseUid != owner ? null : new OwnedDocumentExport(export, document.Revision));
        }
    }

    private sealed class NullAudit : IAuditWriter
    {
        public Task<Guid> AppendAsync(AuditWriteRequest request, CancellationToken ct) => Task.FromResult(Guid.NewGuid());
    }

    private sealed class Queue : IProcessingJobQueue
    {
        public Task EnqueueAsync(string type, string payload, string key, CancellationToken ct) => Task.CompletedTask;
        public Task<ProcessingJobLease?> TryLeaseAsync(string worker, TimeSpan lease, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> HeartbeatAsync(Guid id, string worker, TimeSpan lease, CancellationToken ct) => throw new NotSupportedException();
        public Task CompleteAsync(Guid id, string worker, CancellationToken ct) => throw new NotSupportedException();
        public Task RescheduleAsync(Guid id, string worker, string code, CancellationToken ct) => throw new NotSupportedException();
    }
}

// Native PostgreSQL transaction/locking verification; run when Docker is available.
public sealed class PostgreSqlDocumentExportEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> factory = null!;

    [Fact]
    public async Task Create_CommitsSnapshotAuditAndJobTogether()
    {
        var document = PageManagementHttp.CreateDocument();
        var page = document.ActivePages.First();
        page.SetPreview("private/preview", "private/thumb");
        page.MarkReady();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Add(document);
            await db.SaveChangesAsync();
        }
        using var client = PageManagementHttp.Client(factory);
        var response = await client.PostAsync($"/api/documents/{document.Id}/exports", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await using var verification = factory.Services.CreateAsyncScope();
        var saved = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        var export = await saved.DocumentExports.SingleAsync();
        Assert.Equal(page.Id, Assert.Single(JsonSerializer.Deserialize<DocumentExportSnapshotEntry[]>(export.SnapshotJson)!).PageId);
        Assert.Equal("document.export_created", (await saved.AuditEvents.SingleAsync()).Action);
        Assert.Equal($"export:{export.Id}:build:v1", (await saved.ProcessingJobs.SingleAsync()).IdempotencyKey);
    }

    [Fact]
    public async Task QueueFailure_RollsBackSnapshotAuditAndJobTogether()
    {
        var document = PageManagementHttp.CreateDocument();
        var page = document.ActivePages.First();
        page.SetPreview("private/preview", "private/thumb");
        page.MarkReady();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Add(document);
        await db.SaveChangesAsync();
        var queue = scope.ServiceProvider.GetRequiredService<IProcessingJobQueue>();
        var handler = new SuperScanner.Application.Documents.CreateDocumentExport(
            scope.ServiceProvider.GetRequiredService<IDocumentRepository>(),
            scope.ServiceProvider.GetRequiredService<IDocumentExportRepository>(),
            scope.ServiceProvider.GetRequiredService<IClock>(),
            scope.ServiceProvider.GetRequiredService<IAuditWriter>(), new FailingQueue(queue),
            new SuperScanner.Application.Documents.DocumentExportPolicy(7));
        await Assert.ThrowsAsync<IOException>(() => handler.HandleAsync("user-a", document.Id, default));
        await using var verification = factory.Services.CreateAsyncScope();
        var saved = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await saved.DocumentExports.ToListAsync());
        Assert.Empty(await saved.AuditEvents.ToListAsync());
        Assert.Empty(await saved.ProcessingJobs.ToListAsync());
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

    private sealed class FailingQueue(IProcessingJobQueue inner) : IProcessingJobQueue
    {
        public async Task EnqueueAsync(string type, string payload, string key, CancellationToken ct)
        {
            await inner.EnqueueAsync(type, payload, key, ct);
            throw new IOException("Injected failure after SQL queue insert");
        }
        public Task<ProcessingJobLease?> TryLeaseAsync(string worker, TimeSpan lease, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> HeartbeatAsync(Guid id, string worker, TimeSpan lease, CancellationToken ct) => throw new NotSupportedException();
        public Task CompleteAsync(Guid id, string worker, CancellationToken ct) => throw new NotSupportedException();
        public Task RescheduleAsync(Guid id, string worker, string code, CancellationToken ct) => throw new NotSupportedException();
    }
}
