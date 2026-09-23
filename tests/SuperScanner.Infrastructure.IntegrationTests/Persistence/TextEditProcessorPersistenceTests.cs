using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Ocr;
using SuperScanner.Domain.TextEditing;
using SuperScanner.Infrastructure.Persistence;
using SuperScanner.Infrastructure.TextEditing;
using Testcontainers.PostgreSql;
using TextAlignment = SuperScanner.Domain.TextEditing.TextAlignment;

namespace SuperScanner.Infrastructure.IntegrationTests.Persistence;

public sealed class TextEditProcessorPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    public Task InitializeAsync() => postgres.StartAsync();
    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Success_activates_one_immutable_revision_and_retry_is_idempotent()
    {
        var fixture = await SeedAsync();
        await using var db = new AppDbContext(fixture.Options);
        var processor = new TextEditProcessor(db, fixture.Store,
            new Renderer(), new Clock());
        await processor.RunAsync(fixture.EditId, default);
        await processor.RunAsync(fixture.EditId, default);

        await using var verify = new AppDbContext(fixture.Options);
        var page = await verify.Pages.Include(candidate => candidate.ActiveRevision)
            .SingleAsync(candidate => candidate.Id == fixture.PageId);
        var edit = await verify.TextEditOperations.SingleAsync();
        Assert.Equal(TextEditState.Succeeded, edit.State);
        Assert.Equal(edit.ResultRevisionId, page.ActiveRevisionId);
        Assert.Equal(fixture.SourceRevisionId, page.ActiveRevision!.ParentRevisionId);
        Assert.Single(await verify.PageRevisions.Where(revision =>
            revision.ProducingTextEditId == fixture.EditId).ToListAsync());
        Assert.Single(fixture.Store.CreatedKeys);
    }

    [Fact]
    public async Task Stale_source_revision_never_activates_rendered_object()
    {
        var fixture = await SeedAsync();
        await using (var change = new AppDbContext(fixture.Options))
        {
            var page = await change.Pages.Include(candidate => candidate.ActiveRevision)
                .SingleAsync(candidate => candidate.Id == fixture.PageId);
            var other = PageRevision.CreateBase(Guid.NewGuid(), page.Id,
                "other.jpg", new string('b', 64), Now);
            change.PageRevisions.Add(other);
            page.ActivateRevision(other);
            await change.SaveChangesAsync();
        }
        await using var db = new AppDbContext(fixture.Options);
        var processor = new TextEditProcessor(db, fixture.Store,
            new Renderer(), new Clock());
        var error = await Assert.ThrowsAsync<TextEditProcessingException>(() =>
            processor.RunAsync(fixture.EditId, default));
        Assert.Equal("text_edit_stale_revision", error.SafeCode);
        Assert.False(error.Retryable);
    }

    [Fact]
    public async Task Removed_page_is_not_reactivated_by_pending_text_edit()
    {
        var fixture = await SeedAsync();
        await using (var change = new AppDbContext(fixture.Options))
        {
            var page = await change.Pages.SingleAsync(candidate => candidate.Id == fixture.PageId);
            page.SoftRemove("owner", Now.AddMinutes(1));
            await change.SaveChangesAsync();
        }
        await using var db = new AppDbContext(fixture.Options);
        var processor = new TextEditProcessor(db, fixture.Store, new Renderer(), new Clock());
        var error = await Assert.ThrowsAsync<TextEditProcessingException>(() =>
            processor.RunAsync(fixture.EditId, default));
        Assert.Equal("text_edit_stale_revision", error.SafeCode);
        Assert.Empty(fixture.Store.CreatedKeys);
    }

    private async Task<Fixture> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        var source = "source"u8.ToArray();
        var store = new Store(source);
        var documentId = Guid.NewGuid();
        var pageId = Guid.NewGuid();
        var sourceRevisionId = Guid.NewGuid();
        var ocrId = Guid.NewGuid();
        var wordId = Guid.NewGuid();
        var editId = Guid.NewGuid();
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        var document = Document.Create(documentId, "owner", "Test", Now);
        var page = document.AddPage(pageId, 10, Now);
        page.MarkImportReady("original.jpg", "image/jpeg");
        page.SetPreview("source.jpg", "thumb.jpg");
        page.MarkReady();
        document.MarkReady(Now);
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        var sourceRevision = PageRevision.CreateBase(sourceRevisionId, pageId,
            "source.jpg", Hash(source), Now);
        page.ActivateRevision(sourceRevision);
        var ocr = PageOcrResult.Queue(ocrId, pageId, "source.jpg", Hash(source), "en", Now);
        ocr.BeginAttempt(1, Now);
        var word = OcrElement.Create(wordId, ocrId, null, OcrElementKind.Word,
            "Name", .9, OcrTextType.Printed, 0,
            [new(.1, .2), new(.4, .2), new(.4, .3), new(.1, .3)]);
        ocr.Complete("test", "v1", "Name", [word], Now);
        var edit = TextEditOperation.Queue(editId, documentId, pageId, "owner",
            sourceRevisionId, ocrId, [wordId], "Name", "Tan BB",
            new NormalizedBox(.1, .2, .8, .1),
            new TextEditStyle("noto-sans", "archive-main-regular", .04,
                400, "#000000", 0, .5, 0, TextAlignment.Left), 1, null,
            "apply-1", new string('a', 64), TextEditRenderer.RendererVersion,
            TextLayoutEngine.LayoutVersion, Now);
        db.AddRange(sourceRevision, ocr, edit);
        await db.SaveChangesAsync();
        return new Fixture(options, store, pageId, editId, sourceRevisionId);
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static readonly DateTimeOffset Now = new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Now; }
    private sealed record Fixture(DbContextOptions<AppDbContext> Options, Store Store,
        Guid PageId, Guid EditId, Guid SourceRevisionId);

    private sealed class Renderer : ITextEditRenderer
    {
        public Task<TextEditRenderResult> RenderAsync(TextEditRenderRequest request, CancellationToken ct)
        {
            var bytes = "rendered"u8.ToArray();
            return Task.FromResult(new TextEditRenderResult(bytes, Hash(bytes),
                [true], null));
        }
    }

    private sealed class Store(byte[] source) : IObjectStore
    {
        private readonly Dictionary<string, byte[]> objects = new() { ["source.jpg"] = source };
        public List<string> CreatedKeys { get; } = [];
        public Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<StoredObjectInfo?> HeadAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) =>
            Task.FromResult<Stream>(new MemoryStream(objects[key]));
        public Task PromoteAsync(string from, string to, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public async Task<ObjectCreationResult> WriteIfAbsentAsync(string key, string type,
            Stream content, CancellationToken ct)
        {
            if (objects.ContainsKey(key)) return ObjectCreationResult.AlreadyExists;
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            objects.Add(key, buffer.ToArray());
            CreatedKeys.Add(key);
            return ObjectCreationResult.Created;
        }
    }
}
