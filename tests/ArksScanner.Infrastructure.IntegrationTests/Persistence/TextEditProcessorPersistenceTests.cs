using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ArksScanner.Application.Abstractions;
using ArksScanner.Domain.Documents;
using ArksScanner.Domain.Ocr;
using ArksScanner.Domain.TextEditing;
using ArksScanner.Infrastructure.Persistence;
using ArksScanner.Infrastructure.TextEditing;
using Testcontainers.PostgreSql;
using TextAlignment = ArksScanner.Domain.TextEditing.TextAlignment;

namespace ArksScanner.Infrastructure.IntegrationTests.Persistence;

public sealed class TextEditProcessorPersistenceTests : IAsyncLifetime
{
    [Fact]
    public async Task Add_text_without_ocr_activates_a_new_revision()
    {
        var fixture = await SeedAsync(add: true);
        await using var db = new AppDbContext(fixture.Options);
        await new TextEditProcessor(db, fixture.Store, new Renderer(), new Clock())
            .RunAsync(fixture.EditId, default);
        await using var verify = new AppDbContext(fixture.Options);
        var edit = await verify.TextEditOperations.SingleAsync();
        var page = await verify.Pages.SingleAsync();
        Assert.Equal(TextEditState.Succeeded, edit.State);
        Assert.Equal(Guid.Empty, edit.SourceOcrResultId);
        Assert.Equal(edit.ResultRevisionId, page.ActiveRevisionId);
    }

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    public Task InitializeAsync() => postgres.StartAsync();
    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Success_activates_one_immutable_revision_and_retry_is_idempotent()
    {
        var fixture = await SeedAsync();
        await using var db = new AppDbContext(fixture.Options);
        var originalDocumentRevision = await db.Documents.Where(candidate =>
            candidate.Id == fixture.DocumentId).Select(candidate => candidate.Revision).SingleAsync();
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
        Assert.Equal(originalDocumentRevision + 1, (await verify.Documents.SingleAsync(candidate =>
            candidate.Id == fixture.DocumentId)).Revision);
    }

    [Fact]
    public async Task Success_carries_the_page_text_forward_so_the_next_edit_needs_no_new_recognition()
    {
        var fixture = await SeedAsync();
        await using var db = new AppDbContext(fixture.Options);
        await new TextEditProcessor(db, fixture.Store, new Renderer(), new Clock())
            .RunAsync(fixture.EditId, default);

        await using var verify = new AppDbContext(fixture.Options);
        var page = await verify.Pages.Include(candidate => candidate.ActiveRevision)
            .SingleAsync(candidate => candidate.Id == fixture.PageId);
        var carried = await verify.PageOcrResults.Include(result => result.Elements)
            .SingleAsync(result => result.SourceObjectKey == page.ActiveRevision!.ObjectKey);
        Assert.Equal(OcrResultState.Ready, carried.State);
        Assert.Equal("test", carried.ProviderName);
        Assert.Equal("carried-forward", carried.ProviderModelVersion);
        Assert.Equal("Tan BB", carried.FullText);
        Assert.Equal(["Tan", "BB"], carried.Elements.Where(element => element.Kind == OcrElementKind.Word)
            .OrderBy(element => element.ReadingOrder).Select(element => element.Text));
        Assert.Equal(2, await verify.PageOcrResults.CountAsync());
        Assert.Empty(await verify.ProcessingJobs.Where(job => job.Type == "RecognizePageText").ToListAsync());
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

    [Fact]
    public async Task Page_that_reentered_processing_does_not_activate_pending_edit()
    {
        var fixture = await SeedAsync();
        await using (var change = new AppDbContext(fixture.Options))
        {
            var page = await change.Pages.SingleAsync(candidate => candidate.Id == fixture.PageId);
            page.MarkProcessing();
            await change.SaveChangesAsync();
        }
        await using var db = new AppDbContext(fixture.Options);
        var processor = new TextEditProcessor(db, fixture.Store, new Renderer(), new Clock());
        var error = await Assert.ThrowsAsync<TextEditProcessingException>(() =>
            processor.RunAsync(fixture.EditId, default));
        Assert.Equal("text_edit_stale_revision", error.SafeCode);
        Assert.Empty(fixture.Store.CreatedKeys);
    }

    [Fact]
    public async Task Existing_identical_output_after_lost_ack_is_reused()
    {
        var fixture = await SeedAsync();
        fixture.Store.Seed(OutputKey(fixture), "rendered"u8.ToArray());
        await using var db = new AppDbContext(fixture.Options);
        await new TextEditProcessor(db, fixture.Store, new Renderer(), new Clock())
            .RunAsync(fixture.EditId, default);
        Assert.Empty(fixture.Store.CreatedKeys);
        await using var verify = new AppDbContext(fixture.Options);
        Assert.Equal(TextEditState.Succeeded,
            (await verify.TextEditOperations.SingleAsync()).State);
    }

    [Fact]
    public async Task Existing_conflicting_output_is_never_activated()
    {
        var fixture = await SeedAsync();
        fixture.Store.Seed(OutputKey(fixture), "different"u8.ToArray());
        await using var db = new AppDbContext(fixture.Options);
        var error = await Assert.ThrowsAsync<TextEditProcessingException>(() =>
            new TextEditProcessor(db, fixture.Store, new Renderer(), new Clock())
                .RunAsync(fixture.EditId, default));
        Assert.Equal("text_edit_object_conflict", error.SafeCode);
        await using var verify = new AppDbContext(fixture.Options);
        Assert.Equal(fixture.SourceRevisionId,
            (await verify.Pages.SingleAsync()).ActiveRevisionId);
    }

    [Fact]
    public async Task Revision_changed_during_render_cannot_be_activated()
    {
        var fixture = await SeedAsync();
        var renderer = new CallbackRenderer(async () =>
        {
            await using var change = new AppDbContext(fixture.Options);
            var page = await change.Pages.Include(candidate => candidate.ActiveRevision)
                .SingleAsync(candidate => candidate.Id == fixture.PageId);
            var other = PageRevision.CreateBase(Guid.NewGuid(), page.Id,
                "new-source.jpg", new string('b', 64), Now);
            change.PageRevisions.Add(other);
            page.ActivateRevision(other);
            await change.SaveChangesAsync();
        });
        await using var db = new AppDbContext(fixture.Options);
        var error = await Assert.ThrowsAsync<TextEditProcessingException>(() =>
            new TextEditProcessor(db, fixture.Store, renderer, new Clock())
                .RunAsync(fixture.EditId, default));
        Assert.Equal("text_edit_stale_revision", error.SafeCode);
        Assert.Single(fixture.Store.CreatedKeys); // orphan is unreachable, not activated
        await using var verify = new AppDbContext(fixture.Options);
        Assert.NotEqual(fixture.SourceRevisionId,
            (await verify.Pages.SingleAsync()).ActiveRevisionId);
    }

    [Fact]
    public async Task Unsafe_background_does_not_create_an_output_object()
    {
        var fixture = await SeedAsync();
        await using var db = new AppDbContext(fixture.Options);
        var error = await Assert.ThrowsAsync<TextEditProcessingException>(() =>
            new TextEditProcessor(db, fixture.Store,
                new CallbackRenderer(() => Task.CompletedTask, "text_edit_unsafe_background"),
                new Clock()).RunAsync(fixture.EditId, default));
        Assert.Equal("text_edit_unsafe_background", error.SafeCode);
        Assert.False(error.Retryable);
        Assert.Empty(fixture.Store.CreatedKeys);
    }

    [Fact]
    public async Task Transient_object_store_failure_keeps_edit_uncommitted_for_retry()
    {
        var fixture = await SeedAsync();
        fixture.Store.FailWrites = true;
        await using var db = new AppDbContext(fixture.Options);
        await Assert.ThrowsAsync<IOException>(() =>
            new TextEditProcessor(db, fixture.Store, new Renderer(), new Clock())
                .RunAsync(fixture.EditId, default));
        await using var verify = new AppDbContext(fixture.Options);
        Assert.Equal(TextEditState.Processing,
            (await verify.TextEditOperations.SingleAsync()).State);
        Assert.Equal(fixture.SourceRevisionId,
            (await verify.Pages.SingleAsync()).ActiveRevisionId);
    }

    [Fact]
    public async Task Cancellation_does_not_write_or_activate_a_revision()
    {
        var fixture = await SeedAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var db = new AppDbContext(fixture.Options);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new TextEditProcessor(db, fixture.Store, new Renderer(), new Clock())
                .RunAsync(fixture.EditId, cancellation.Token));
        Assert.Empty(fixture.Store.CreatedKeys);
        await using var verify = new AppDbContext(fixture.Options);
        Assert.Equal(fixture.SourceRevisionId,
            (await verify.Pages.SingleAsync()).ActiveRevisionId);
    }

    private async Task<Fixture> SeedAsync(bool add = false)
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
            sourceRevisionId, add ? Guid.Empty : ocrId,
            add ? [] : [wordId], add ? "" : "Name", "Tan BB",
            new NormalizedBox(.1, .2, .8, .1),
            new TextEditStyle("noto-sans", "archive-main-regular", .04,
                400, "#000000", 0, .5, 0, TextAlignment.Left), 1, null,
            "apply-1", new string('a', 64), TextEditRenderer.RendererVersion,
            TextLayoutEngine.LayoutVersion, Now);
        if (add) db.AddRange(sourceRevision, edit);
        else db.AddRange(sourceRevision, ocr, edit);
        await db.SaveChangesAsync();
        return new Fixture(options, store, documentId, pageId, editId, sourceRevisionId);
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string OutputKey(Fixture fixture) =>
        $"page-revisions/{fixture.DocumentId:N}/{fixture.PageId:N}/{fixture.EditId:N}.png";

    private static readonly DateTimeOffset Now = new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Now; }
    private sealed record Fixture(DbContextOptions<AppDbContext> Options, Store Store,
        Guid DocumentId, Guid PageId, Guid EditId, Guid SourceRevisionId);

    private sealed class Renderer : ITextEditRenderer
    {
        public Task<TextEditRenderResult> RenderAsync(TextEditRenderRequest request, CancellationToken ct)
        {
            var bytes = "rendered"u8.ToArray();
            return Task.FromResult(new TextEditRenderResult(bytes, Hash(bytes),
                [true], null));
        }
    }

    private sealed class CallbackRenderer(Func<Task> callback, string? failureCode = null) : ITextEditRenderer
    {
        public async Task<TextEditRenderResult> RenderAsync(TextEditRenderRequest request,
            CancellationToken ct)
        {
            await callback();
            if (failureCode is not null) return new(null, null, null, failureCode);
            var bytes = "rendered"u8.ToArray();
            return new(bytes, Hash(bytes), [true], null);
        }
    }

    private sealed class Store(byte[] source) : IObjectStore
    {
        private readonly Dictionary<string, byte[]> objects = new() { ["source.jpg"] = source };
        public List<string> CreatedKeys { get; } = [];
        public bool FailWrites { get; set; }
        public void Seed(string key, byte[] bytes) => objects.Add(key, bytes);
        public Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<StoredObjectInfo?> HeadAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) =>
            Task.FromResult<Stream>(new MemoryStream(objects[key]));
        public Task PromoteAsync(string from, string to, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public async Task<ObjectCreationResult> WriteIfAbsentAsync(string key, string type,
            Stream content, CancellationToken ct)
        {
            if (FailWrites) throw new IOException("Storage unavailable.");
            if (objects.ContainsKey(key)) return ObjectCreationResult.AlreadyExists;
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            objects.Add(key, buffer.ToArray());
            CreatedKeys.Add(key);
            return ObjectCreationResult.Created;
        }
    }
}
