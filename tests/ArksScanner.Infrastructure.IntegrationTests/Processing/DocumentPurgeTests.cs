using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ArksScanner.Application.Abstractions;
using ArksScanner.Domain.Documents;
using ArksScanner.Domain.TextEditing;
using ArksScanner.Infrastructure.Persistence;
using ArksScanner.Infrastructure.Processing;
using Testcontainers.PostgreSql;

namespace ArksScanner.Infrastructure.IntegrationTests.Processing;

public sealed class DocumentPurgeTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 3, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RecordingStore store = new();

    [Fact]
    public async Task Erases_documents_deleted_longer_ago_than_the_window_with_their_history_and_files()
    {
        var expired = await SeedEditedDocumentAsync("expired", deletedAt: Now.AddDays(-31));
        var inBin = await SeedEditedDocumentAsync("in-bin", deletedAt: Now.AddDays(-10));
        var live = await SeedEditedDocumentAsync("live", deletedAt: null);

        await using (var db = Db())
            Assert.Equal(1, await Purge(db).RunAsync(default));

        await using var reader = Db();
        var remaining = await reader.Documents.IgnoreQueryFilters().Select(d => d.Id).ToListAsync();
        Assert.Equal(new[] { inBin, live }.Order(), remaining.Order());
        Assert.Empty(await reader.Pages.IgnoreQueryFilters().Where(p => p.DocumentId == expired).ToListAsync());
        Assert.Empty(await reader.TextEditOperations.Where(e => e.DocumentId == expired).ToListAsync());
        Assert.Equal(4, await reader.PageRevisions.CountAsync());

        Assert.Contains("expired/preview.jpg", store.Deleted);
        Assert.Contains("expired/source.jpg", store.Deleted);
        Assert.Contains("expired/derived.png", store.Deleted);
        Assert.DoesNotContain(store.Deleted, key => !key.StartsWith("expired/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Nothing_to_purge_is_a_no_op()
    {
        await SeedEditedDocumentAsync("live", deletedAt: null);
        await using var db = Db();
        Assert.Equal(0, await Purge(db).RunAsync(default));
        Assert.Empty(store.Deleted);
    }

    private DocumentPurge Purge(AppDbContext db) =>
        new(db, store, new FixedClock(), NullLogger<DocumentPurge>.Instance, purgeAfterDays: 30);

    private async Task<Guid> SeedEditedDocumentAsync(string prefix, DateTimeOffset? deletedAt)
    {
        await using var db = Db();
        var created = Now.AddDays(-60);
        var document = Document.Create(Guid.NewGuid(), "owner", prefix, created);
        var page = document.AddPage(Guid.NewGuid(), 10, created);
        page.MarkImportReady($"{prefix}/source.jpg", "image/jpeg");
        page.SetPreview($"{prefix}/preview.jpg", $"{prefix}/thumb.jpg");
        page.MarkReady();
        document.MarkReady(created);
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        var baseRevision = PageRevision.CreateBase(Guid.NewGuid(), page.Id, $"{prefix}/preview.jpg", Hash('a'), created);
        db.PageRevisions.Add(baseRevision);
        await db.SaveChangesAsync();
        var edit = TextEditOperation.Queue(Guid.NewGuid(), document.Id, page.Id, "owner",
            baseRevision.Id, Guid.NewGuid(), [Guid.NewGuid()], "Before", "After",
            new NormalizedBox(0.1, 0.1, 0.2, 0.1),
            new TextEditStyle("noto-sans", "v1", 0.04, 400, "#000000", 0, 0.2, 0, TextAlignment.Left),
            1, null, $"purge-{prefix}", Hash('c'), "renderer-v1", "layout-v1", created);
        db.TextEditOperations.Add(edit);
        await db.SaveChangesAsync();
        var derived = PageRevision.CreateDerived(Guid.NewGuid(), page.Id, baseRevision.Id, edit.Id,
            $"{prefix}/derived.png", Hash('b'), created.AddSeconds(1));
        db.PageRevisions.Add(derived);
        page.ActivateRevision(derived);
        await db.SaveChangesAsync();

        if (deletedAt is { } at)
        {
            var tracked = await db.Documents.Include(d => d.Pages).SingleAsync(d => d.Id == document.Id);
            tracked.Remove(Document.UserDeletedReason, at);
            await db.SaveChangesAsync();
        }
        return document.Id;
    }

    private AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(postgres.GetConnectionString()).Options);

    private static string Hash(char value) => new(value, 64);

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await using var db = Db();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => Now; }

    private sealed class RecordingStore : IObjectStore
    {
        public List<string> Deleted { get; } = [];
        public Task DeleteAsync(string key, CancellationToken ct) { Deleted.Add(key); return Task.CompletedTask; }
        public Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<StoredObjectInfo?> HeadAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task PromoteAsync(string source, string destination, CancellationToken ct) => throw new NotSupportedException();
        public Task<ObjectCreationResult> WriteIfAbsentAsync(string key, string mediaType, Stream content,
            CancellationToken ct) => throw new NotSupportedException();
    }
}
