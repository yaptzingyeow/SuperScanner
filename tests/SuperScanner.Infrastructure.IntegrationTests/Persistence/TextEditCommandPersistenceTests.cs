using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SuperScanner.Application.Abstractions;
using SuperScanner.Application.TextEditing;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Ocr;
using SuperScanner.Domain.TextEditing;
using SuperScanner.Infrastructure.Auditing;
using SuperScanner.Infrastructure.Persistence;
using SuperScanner.Infrastructure.Processing;
using SuperScanner.Infrastructure.TextEditing;
using Testcontainers.PostgreSql;
using TextAlignment = SuperScanner.Domain.TextEditing.TextAlignment;

namespace SuperScanner.Infrastructure.IntegrationTests.Persistence;

public sealed class TextEditCommandPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    public Task InitializeAsync() => postgres.StartAsync();
    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task First_edit_creates_base_revision_operation_job_and_audit_once()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        var documentId = Guid.NewGuid();
        var pageId = Guid.NewGuid();
        var ocrId = Guid.NewGuid();
        var wordId = Guid.NewGuid();
        await using (var seed = new AppDbContext(options))
        {
            await seed.Database.MigrateAsync();
            var document = Document.Create(documentId, "owner", "Test", Now);
            var page = document.AddPage(pageId, 10, Now);
            page.MarkImportReady("original.jpg", "image/jpeg");
            page.SetPreview("preview.jpg", "thumb.jpg");
            page.MarkReady();
            document.MarkReady(Now);
            var ocr = PageOcrResult.Queue(ocrId, pageId, "preview.jpg", new string('a', 64), "en", Now);
            ocr.BeginAttempt(1, Now);
            var lineId = Guid.NewGuid();
            var line = OcrElement.Create(lineId, ocrId, null, OcrElementKind.Line,
                "Name", .9, OcrTextType.Printed, 0,
                [new(.1, .2), new(.4, .2), new(.4, .3), new(.1, .3)]);
            var word = OcrElement.Create(wordId, ocrId, lineId, OcrElementKind.Word,
                "Name", .9, OcrTextType.Printed, 0,
                [new(.1, .2), new(.4, .2), new(.4, .3), new(.1, .3)]);
            ocr.Complete("test", "v1", "Name", [line, word], Now);
            seed.Documents.Add(document);
            seed.PageOcrResults.Add(ocr);
            await seed.SaveChangesAsync();
        }

        using var image = new MagickImage(MagickColors.White, 600, 800);
        var store = new Store(image.ToByteArray(MagickFormat.Png));
        var root = FindRoot();
        var fonts = BundledFontCatalogue.Load(Path.Combine(root, "assets/fonts/manifest.json"));
        var clock = new Clock();
        await using var db = new AppDbContext(options);
        var service = new CreateTextEdit(new EfTextEditRepository(db),
            new TextEditPreparation(store, fonts, root),
            new PostgresJobQueue(db, clock),
            new HmacAuditWriter(db, Options.Create(new AuditOptions
            {
                SigningKeyBase64 = Convert.ToBase64String(new byte[32]),
                SigningKeyId = "test"
            })), clock, new TextEditLimits(true, 50, 4000, .5, 2));
        var request = new CreateTextEditRequest("owner", documentId, pageId, ocrId,
            null, [wordId], "Tan BB", new NormalizedBox(.1, .2, .8, .1),
            new TextEditStyle("noto-sans", "archive-main-regular", .04,
                400, "#000000", 0, .25, 0, TextAlignment.Left), "apply-1");
        var first = await service.HandleAsync(request, default);
        var replay = await service.HandleAsync(request, default);
        Assert.Equal(first.EditId, replay.EditId);
        Assert.True(replay.Replayed);

        await using var verify = new AppDbContext(options);
        Assert.Single(await verify.PageRevisions.ToListAsync());
        Assert.Single(await verify.TextEditOperations.ToListAsync());
        Assert.Single(await verify.ProcessingJobs.Where(job => job.Type == "RenderTextEdit").ToListAsync());
        var audit = Assert.Single(await verify.AuditEvents.Where(evt => evt.Action == "text_edit.queued").ToListAsync());
        Assert.DoesNotContain("Tan BB", audit.RegionJson);
        var status = await new GetTextEdit(new EfTextEditRepository(verify))
            .HandleAsync("owner", documentId, pageId, first.EditId, default);
        Assert.Equal("Tan BB", status.ReplacementText);
        Assert.Equal("Queued", status.State);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SuperScanner.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Now; }
    private sealed class Store(byte[] bytes) : IObjectStore
    {
        public Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<StoredObjectInfo?> HeadAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(bytes));
        public Task PromoteAsync(string from, string to, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task<ObjectCreationResult> WriteIfAbsentAsync(string key, string type, Stream content, CancellationToken ct) => throw new NotSupportedException();
    }
}
