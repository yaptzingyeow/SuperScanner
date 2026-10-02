using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using ArksScanner.Application.Abstractions;
using ArksScanner.Domain.Documents;
using ArksScanner.Infrastructure.Persistence;
using ArksScanner.Infrastructure.Processing;
using Testcontainers.PostgreSql;

namespace ArksScanner.Infrastructure.IntegrationTests.Processing;

public sealed class PageRepairProcessorTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    public Task InitializeAsync() => postgres.StartAsync();
    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Cleanup_removes_only_expired_unapplied_private_preview()
    {
        var store = new MemoryStore();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        Guid unusedId, appliedId;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            var document = Document.Create(Guid.NewGuid(), "owner", "Cleanup", DateTimeOffset.UtcNow);
            var page = document.AddPage(Guid.NewGuid(), 20, DateTimeOffset.UtcNow);
            db.Documents.Add(document);
            var old = DateTimeOffset.UtcNow.AddDays(-2);
            var unused = PageRepairOperation.Create(Guid.NewGuid(), page.Id, null,
                "previews/source.jpg", "[[0.01,0.2,0.04,0.25]]", old);
            var applied = PageRepairOperation.Create(Guid.NewGuid(), page.Id, null,
                "previews/source.jpg", "[[0.01,0.2,0.04,0.25]]", old);
            unusedId = unused.Id; appliedId = applied.Id;
            unused.CompletePreview("repairs/unused.png");
            applied.CompletePreview("repairs/applied.png");
            applied.Apply(Guid.NewGuid());
            db.PageRepairOperations.AddRange(unused, applied);
            await db.SaveChangesAsync();
            store.Add("repairs/unused.png", [1]);
            store.Add("repairs/applied.png", [2]);
        }

        await using (var db = new AppDbContext(options))
            await new PageRepairAssetCleanup(db, store, new Clock()).RunAsync(default);

        await using var reader = new AppDbContext(options);
        Assert.Equal("Expired", (await reader.PageRepairOperations.SingleAsync(x => x.Id == unusedId)).State);
        Assert.Equal("Applied", (await reader.PageRepairOperations.SingleAsync(x => x.Id == appliedId)).State);
        Assert.False(store.Has("repairs/unused.png"));
        Assert.True(store.Has("repairs/applied.png"));
    }

    [Fact]
    public async Task Worker_generates_private_png_preview_and_does_not_change_source()
    {
        var root = FindRepositoryRoot();
        var python = Path.Combine(root, ".task-tools", "crop-runtime", "Scripts", "python.exe");
        var script = Path.Combine(root, "src", "ArksScanner.Worker", "processing", "repair_image.py");
        var fixture = Path.Combine(root, "apps", "web", "e2e", "fixtures", "append-photo.jpg");
        if (!File.Exists(python)) return; // Local OpenCV runtime is optional in CI.
        var source = await File.ReadAllBytesAsync(fixture);
        var store = new MemoryStore();
        store.Add("previews/fixture.jpg", source);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        var configuration = new MinimalConfiguration(new Dictionary<string, string?> {
            ["Crop:PythonPath"] = python, ["Repair:ScriptPath"] = script
        });
        Guid previewId, suggestionId, pageId, documentId;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            var document = Document.Create(Guid.NewGuid(), "owner", "Repair", DateTimeOffset.UtcNow);
            var page = document.AddPage(Guid.NewGuid(), 20, DateTimeOffset.UtcNow);
            documentId = document.Id; pageId = page.Id;
            page.MarkImportReady("originals/fixture.jpg", "image/jpeg");
            page.SetPreview("previews/fixture.jpg", "thumbnails/fixture.jpg");
            page.MarkReady(); document.MarkReady(DateTimeOffset.UtcNow);
            db.Documents.Add(document);
            var preview = PageRepairOperation.Create(Guid.NewGuid(), page.Id, null,
                "previews/fixture.jpg", "[[0.145,0.365,0.18,0.4]]", DateTimeOffset.UtcNow);
            var suggestion = PageRepairOperation.CreateSuggestion(Guid.NewGuid(), page.Id,
                null, "previews/fixture.jpg", DateTimeOffset.UtcNow);
            previewId = preview.Id; suggestionId = suggestion.Id;
            db.PageRepairOperations.AddRange(preview, suggestion);
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            var processor = new PageRepairProcessor(db, store, configuration);
            await processor.RunAsync(suggestionId, default);
            await processor.RunAsync(previewId, default);
        }

        Guid protectedPreviewId;
        await using (var db = new AppDbContext(options))
        {
            db.PageSignatures.Add(PageSignature.Create(Guid.NewGuid(), documentId, pageId,
                Guid.NewGuid(), "signatures/ink.png", 2, new SignatureBox(.14, .36, .05, .06),
                DateTimeOffset.UtcNow));
            var protectedPreview = PageRepairOperation.Create(Guid.NewGuid(), pageId, null,
                "previews/fixture.jpg", "[[0.145,0.365,0.18,0.4]]", DateTimeOffset.UtcNow);
            protectedPreviewId = protectedPreview.Id;
            db.PageRepairOperations.Add(protectedPreview);
            await db.SaveChangesAsync();
        }
        await using (var db = new AppDbContext(options))
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new PageRepairProcessor(db, store, configuration).RunAsync(protectedPreviewId, default));

        await using var reader = new AppDbContext(options);
        var suggested = await reader.PageRepairOperations.SingleAsync(x => x.Id == suggestionId);
        var repaired = await reader.PageRepairOperations.SingleAsync(x => x.Id == previewId);
        Assert.Equal("Ready", suggested.State);
        Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(suggested.CandidatesJson!).RootElement.ValueKind);
        Assert.Equal("Ready", repaired.State);
        Assert.NotNull(repaired.PreviewObjectKey);
        var output = store.Read(repaired.PreviewObjectKey!);
        Assert.True(output.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71 }));
        Assert.Equal(source, store.Read("previews/fixture.jpg"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "ArksScanner.Worker",
                "processing", "repair_image.py"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class MemoryStore : IObjectStore
    {
        private readonly Dictionary<string, byte[]> data = new(StringComparer.Ordinal);
        public void Add(string key, byte[] bytes) => data[key] = bytes;
        public byte[] Read(string key) => data[key];
        public bool Has(string key) => data.ContainsKey(key);
        public Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<StoredObjectInfo?> HeadAsync(string key, CancellationToken ct) =>
            Task.FromResult<StoredObjectInfo?>(data.TryGetValue(key, out var bytes) ?
                new StoredObjectInfo(bytes.Length, "image/png", "test") : null);
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) =>
            Task.FromResult<Stream>(new MemoryStream(data[key], writable: false));
        public Task PromoteAsync(string source, string destination, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) { data.Remove(key); return Task.CompletedTask; }
        public async Task<ObjectCreationResult> WriteIfAbsentAsync(string key, string mediaType,
            Stream content, CancellationToken ct)
        {
            if (data.ContainsKey(key)) return ObjectCreationResult.AlreadyExists;
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            data.Add(key, buffer.ToArray());
            return ObjectCreationResult.Created;
        }
    }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

    private sealed class MinimalConfiguration(Dictionary<string, string?> values) : IConfiguration
    {
        public string? this[string key] { get => values.GetValueOrDefault(key); set => values[key] = value; }
        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => throw new NotSupportedException();
        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
