using Microsoft.EntityFrameworkCore;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;
using SuperScanner.Infrastructure.Persistence;
using SuperScanner.Infrastructure.Signatures;
using Testcontainers.PostgreSql;

namespace SuperScanner.Infrastructure.IntegrationTests.Signatures;

public sealed class SignatureAssetCleanupTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    public Task InitializeAsync() => postgres.StartAsync();
    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Cleanup_RetriesDurableOrphanButLeavesRecentUploadAlone()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options);
        await db.Database.MigrateAsync();
        var clock = new Clock();
        db.SignatureAssetWriteIntents.AddRange(
            new SignatureAssetWriteIntent { Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), AssetKey = "old.png", CreatedAt = clock.UtcNow.AddDays(-2) },
            new SignatureAssetWriteIntent { Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), AssetKey = "recent.png", CreatedAt = clock.UtcNow });
        await db.SaveChangesAsync();
        var store = new Store();
        await new SignatureAssetCleanup(db, store, clock).RunAsync(default);
        await new SignatureAssetCleanup(db, store, clock).RunAsync(default);
        Assert.Equal("old.png", Assert.Single(store.Deleted));
        Assert.Equal("recent.png", (await db.SignatureAssetWriteIntents.SingleAsync()).AssetKey);
    }

    [Fact]
    public async Task Cleanup_LaterBatchIsNotStarvedByProtectedAssets()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options);
        await db.Database.MigrateAsync();
        var clock = new Clock();
        var document = Document.Create(Guid.NewGuid(), "owner", "Test", clock.UtcNow);
        var page = document.AddPage(Guid.NewGuid(), 10, clock.UtcNow);
        page.SetPreview("page.jpg", "thumb.jpg"); page.MarkReady();
        var snapshots = new List<SignatureOverlaySnapshot>();
        for (var i = 1; i <= 101; i++)
        {
            var signature = PageSignature.Create(Guid.Parse($"00000000-0000-0000-0000-{i:D12}"), document.Id,
                page.Id, Guid.NewGuid(), $"private/{i}.png", 2, new SignatureBox(.1, .1, .2, .1), clock.UtcNow);
            signature.Delete(0, clock.UtcNow); db.Add(signature);
            if (i < 101) snapshots.Add(new(signature.Id, signature.AssetKey, signature.Box, 2));
        }
        db.Add(document);
        db.Add(DocumentExport.Create(Guid.NewGuid(), document, "owner", clock.UtcNow, TimeSpan.FromDays(7),
            signaturesByPage: new Dictionary<Guid, IReadOnlyList<SignatureOverlaySnapshot>> { [page.Id] = snapshots }));
        await db.SaveChangesAsync();
        var store = new Store();
        await new SignatureAssetCleanup(db, store, clock).RunAsync(default);
        clock.UtcNow = clock.UtcNow.AddHours(1);
        await new SignatureAssetCleanup(db, store, clock).RunAsync(default);
        Assert.Equal("private/101.png", Assert.Single(store.Deleted));
    }

    [Theory]
    [InlineData(false, "none", false)]
    [InlineData(true, "none", true)]
    [InlineData(true, "queued", false)]
    [InlineData(true, "ready-live", false)]
    [InlineData(true, "ready-expired", true)]
    [InlineData(false, "removed", true)]
    public async Task Cleanup_DeletesOnlyRetiredUnreferencedAssets(bool deleted, string reference, bool removed)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options);
        await db.Database.MigrateAsync();
        var clock = new Clock();
        var document = Document.Create(Guid.NewGuid(), "owner", "Test", clock.UtcNow);
        var page = document.AddPage(Guid.NewGuid(), 10, clock.UtcNow);
        page.SetPreview("page.jpg", "thumb.jpg"); page.MarkReady();
        var signature = PageSignature.Create(Guid.NewGuid(), document.Id, page.Id, Guid.NewGuid(),
            "private/ink.png", 2, new SignatureBox(.1, .1, .2, .1), clock.UtcNow);
        if (deleted) signature.Delete(0, clock.UtcNow);
        db.AddRange(document, signature);
        if (reference == "removed") document.RemovePage(page.Id, "owner", clock.UtcNow);
        if (reference is not ("none" or "removed"))
        {
            var export = DocumentExport.Create(Guid.NewGuid(), document, "owner", clock.UtcNow.AddDays(-2),
                TimeSpan.FromDays(reference == "ready-live" ? 7 : 1), signaturesByPage:
                new Dictionary<Guid, IReadOnlyList<SignatureOverlaySnapshot>>
                { [page.Id] = [new(signature.Id, signature.AssetKey, signature.Box, 2)] });
            if (reference.StartsWith("ready")) { export.Start(clock.UtcNow); export.Complete("export.pdf", clock.UtcNow); }
            db.Add(export);
        }
        await db.SaveChangesAsync();
        var store = new Store();
        await new SignatureAssetCleanup(db, store, clock).RunAsync(default);
        Assert.Equal(removed, store.Deleted.Contains("private/ink.png"));
        await new SignatureAssetCleanup(db, store, clock).RunAsync(default);
        Assert.Equal(removed ? 1 : 0, store.Deleted.Count);
        Assert.Single(await db.PageSignatures.ToListAsync()); // Keep idempotency tombstone.
    }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-09-27T00:00:00Z"); }
    private sealed class Store : IObjectStore
    {
        public List<string> Deleted { get; } = [];
        public Task DeleteAsync(string key, CancellationToken ct) { Deleted.Add(key); return Task.CompletedTask; }
        public Task<Uri> CreatePutUrlAsync(PutObjectRequest r, CancellationToken ct) => throw new NotSupportedException();
        public Task<StoredObjectInfo?> HeadAsync(string k, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string k, CancellationToken ct) => throw new NotSupportedException();
        public Task PromoteAsync(string a, string b, CancellationToken ct) => throw new NotSupportedException();
        public Task<ObjectCreationResult> WriteIfAbsentAsync(string k, string t, Stream s, CancellationToken ct) => throw new NotSupportedException();
    }
}
