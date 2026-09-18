using ImageMagick;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;
using SuperScanner.Infrastructure.Persistence;
using SuperScanner.Infrastructure.Processing;
using SuperScanner.Worker;

namespace SuperScanner.Application.Tests.Processing;

public sealed class DocumentPdfBuilderTests
{
    [Fact]
    public async Task Build_UsesOnlySnapshotImagesInOrderAt96Dpi_AndCompletedRetryIsImmutable()
    {
        await using var f = await Fixture.CreateAsync();
        var snapshot = f.Export.SnapshotJson;
        // Later edits/removal must not affect this export's assets or membership.
        foreach (var page in f.Document.ActivePages.ToArray())
        {
            page.SetPreview("new-revision", "new-thumb");
            f.Document.RemovePage(page.Id, "owner", f.Clock.UtcNow);
        }
        await f.Db.SaveChangesAsync();
        f.Store.OnWrite = () =>
        {
            var inFlight = Assert.Single(f.Db.DocumentExports.Local);
            Assert.Equal(DocumentExportState.Processing, inFlight.State);
            Assert.Null(inFlight.OutputObjectKey);
        };
        await f.BuildAsync();
        var export = await f.ReloadAsync();
        Assert.Equal(DocumentExportState.Ready, export.State);
        Assert.Equal(snapshot, export.SnapshotJson);
        Assert.Equal($"exports/{f.Document.Id}/{export.Id}/document.pdf", export.OutputObjectKey);
        var bytes = f.Store.Objects[export.OutputObjectKey!];
        using var pdf = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.Equal(3, pdf.PageCount);
        Assert.Equal(new[] { 72d, 36d, 54d }, pdf.Pages.Cast<PdfPage>().Select(p => p.Width.Point));
        Assert.Equal(new[] { 36d, 72d, 54d }, pdf.Pages.Cast<PdfPage>().Select(p => p.Height.Point));
        Assert.Equal(new[] { "revision-1", "revision-2", "revision-3" }, f.Store.ReadKeys);
        for (var i = 0; i < 3; i++)
        {
            var resources = pdf.Pages[i].Resources!;
            var images = resources.Elements.GetDictionary("/XObject")!;
            var image = Assert.IsAssignableFrom<PdfDictionary>(Assert.IsType<PdfReference>(Assert.Single(images.Elements.Values)).Value);
            Assert.Equal(f.Store.Objects[$"revision-{i + 1}"], image.Stream.Value);
            Assert.DoesNotContain("/Font", resources.Elements.Keys);
        }
        var completedAt = export.CompletedAt;
        await f.BuildAsync();
        Assert.Equal(1, f.Store.Writes);
        Assert.Equal(bytes, f.Store.Objects[export.OutputObjectKey!]);
        Assert.Equal(completedAt, (await f.ReloadAsync()).CompletedAt);
    }

    [Theory]
    [InlineData("missing", "export_asset_missing")]
    [InlineData("decode", "export_decode_failed")]
    [InlineData("write", "export_build_failed")]
    [InlineData("source-size", "export_size_limit")]
    [InlineData("pdf-size", "export_size_limit")]
    [InlineData("pixels", "export_size_limit")]
    public async Task Failure_IsSafeTerminalAndNeverPublishesPartialOutput(string failure, string expected)
    {
        await using var f = await Fixture.CreateAsync();
        if (failure == "missing") f.Store.Objects.Remove("revision-2");
        if (failure == "decode") f.Store.Objects["revision-2"] = "not an image"u8.ToArray();
        if (failure == "write") f.Store.FailWrite = true;
        var limits = new DocumentPdfLimits
        {
            MaxSourceBytes = failure == "source-size" ? 10 : 25 * 1024 * 1024,
            MaxPdfBytes = failure == "pdf-size" ? 100 : 250 * 1024 * 1024,
            MaxPagePixels = failure == "pixels" ? 10 : 40_000_000
        };
        await f.BuildAsync(limits);
        var export = await f.ReloadAsync();
        Assert.Equal(DocumentExportState.Failed, export.State);
        Assert.Equal(expected, export.FailureCode);
        Assert.Null(export.OutputObjectKey);
        Assert.NotNull(export.CompletedAt);
        var writes = f.Store.Writes;
        await f.BuildAsync();
        Assert.Equal(writes, f.Store.Writes);
        Assert.Equal(failure == "write" ? 1 : 0, writes);
    }

    [Fact]
    public async Task Cancellation_DoesNotPublishOrTurnIntoTerminalFailure()
    {
        await using var f = await Fixture.CreateAsync();
        using var cts = new CancellationTokenSource();
        f.Store.OnRead = cts.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DocumentPdfBuilder(f.Db, f.Store, f.Clock).BuildAsync(f.Export.Id, cts.Token));
        Assert.Equal(DocumentExportState.Queued, (await f.ReloadAsync()).State);
        Assert.Equal(0, f.Store.Writes);
        f.Store.OnRead = null;
        await f.BuildAsync();
        Assert.Equal(DocumentExportState.Ready, (await f.ReloadAsync()).State);
    }

    [Fact]
    public async Task Retry_AfterSuccessfulPutButCanceledCommit_ReusesImmutableOutput()
    {
        await using var f = await Fixture.CreateAsync();
        using var cts = new CancellationTokenSource();
        f.Store.OnWrite = cts.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DocumentPdfBuilder(f.Db, f.Store, f.Clock).BuildAsync(f.Export.Id, cts.Token));
        Assert.Equal(DocumentExportState.Queued, (await f.ReloadAsync()).State);
        var outputKey = $"exports/{f.Document.Id}/{f.Export.Id}/document.pdf";
        var original = f.Store.Objects[outputKey];
        f.Store.OnWrite = null;
        await f.BuildAsync();
        Assert.Equal(DocumentExportState.Ready, (await f.ReloadAsync()).State);
        Assert.Equal(1, f.Store.Writes);
        Assert.Equal(original, f.Store.Objects[outputKey]);
    }

    [Fact]
    public async Task Retry_DoesNotOverwriteOrPublishCorruptExistingOutput()
    {
        await using var f = await Fixture.CreateAsync();
        var outputKey = $"exports/{f.Document.Id}/{f.Export.Id}/document.pdf";
        f.Store.Objects[outputKey] = "incomplete PDF"u8.ToArray();
        await f.BuildAsync();
        Assert.Equal(DocumentExportState.Failed, (await f.ReloadAsync()).State);
        Assert.Equal("export_build_failed", (await f.ReloadAsync()).FailureCode);
        Assert.Equal(0, f.Store.Writes);
        Assert.Equal("incomplete PDF"u8.ToArray(), f.Store.Objects[outputKey]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Worker_DispatchesBuildDocumentPdfAndCompletesTerminalJob(bool missingAsset)
    {
        await using var f = await Fixture.CreateAsync();
        if (missingAsset) f.Store.Objects.Remove("revision-2");
        var queue = new Queue(f.Export.Id);
        var services = new ServiceCollection();
        services.AddSingleton<IProcessingJobQueue>(queue);
        services.AddSingleton(new DocumentPdfBuilder(f.Db, f.Store, f.Clock));
        await using var provider = services.BuildServiceProvider();
        var runner = new UploadValidationJobRunner(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<UploadValidationJobRunner>.Instance);
        Assert.True(await runner.RunOnceAsync("worker", TimeSpan.FromMinutes(2), default));
        Assert.True(queue.Completed);
        Assert.Null(queue.Failure);
        Assert.Equal(missingAsset ? DocumentExportState.Failed : DocumentExportState.Ready, (await f.ReloadAsync()).State);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required SqliteConnection Connection { get; init; }
        public required AppDbContext Db { get; init; }
        public required Document Document { get; init; }
        public required DocumentExport Export { get; init; }
        public Store Store { get; } = new();
        public Clock Clock { get; } = new();
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var now = new Clock().UtcNow;
            var document = Document.Create(Guid.NewGuid(), "owner", "Sensitive title", now);
            foreach (var index in Enumerable.Range(1, 4))
            {
                var page = document.AddPage(Guid.NewGuid(), 50, now);
                page.SetPreview($"revision-{index}", $"thumb-{index}");
                if (index < 4) page.MarkReady();
            }
            var export = DocumentExport.Create(Guid.NewGuid(), document, "owner", now, TimeSpan.FromDays(7));
            db.AddRange(document, export);
            await db.SaveChangesAsync();
            var f = new Fixture { Connection = connection, Db = db, Document = document, Export = export };
            (uint W, uint H, MagickColor Color)[] images = [(96, 48, MagickColors.Red), (48, 96, MagickColors.Lime), (72, 72, MagickColors.Blue)];
            for (var i = 0; i < images.Length; i++)
            {
                using var image = new MagickImage(images[i].Color, images[i].W, images[i].H);
                f.Store.Objects[$"revision-{i + 1}"] = image.ToByteArray(MagickFormat.Jpeg);
            }
            return f;
        }
        public Task BuildAsync(DocumentPdfLimits? limits = null) => new DocumentPdfBuilder(Db, Store, Clock, limits).BuildAsync(Export.Id, default);
        public async Task<DocumentExport> ReloadAsync() { Db.ChangeTracker.Clear(); return await Db.DocumentExports.SingleAsync(); }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow => new(2026, 9, 18, 0, 0, 0, TimeSpan.Zero); }
    private sealed class Store : IObjectStore
    {
        public Dictionary<string, byte[]> Objects { get; } = [];
        public List<string> ReadKeys { get; } = [];
        public int Writes { get; private set; }
        public bool FailWrite { get; set; }
        public Action? OnRead { get; set; }
        public Action? OnWrite { get; set; }
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
        {
            ReadKeys.Add(key);
            OnRead?.Invoke();
            return Task.FromResult<Stream>(new MemoryStream(Objects.TryGetValue(key, out var bytes) ? bytes : throw new FileNotFoundException("private key")));
        }
        public async Task WriteAsync(string key, string mediaType, Stream content, CancellationToken ct)
        {
            Writes++;
            Assert.Equal("application/pdf", mediaType);
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            Objects[key] = copy.ToArray();
            OnWrite?.Invoke();
            if (FailWrite) throw new IOException("secret storage detail");
        }
        // Deliberately under-report to prove streamed-byte enforcement independently of metadata.
        public Task<StoredObjectInfo?> HeadAsync(string key, CancellationToken ct) => Task.FromResult<StoredObjectInfo?>(Objects.ContainsKey(key) ? new(1, "image/jpeg", "etag") : null);
        public Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task PromoteAsync(string from, string to, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Queue(Guid exportId) : IProcessingJobQueue
    {
        public bool Completed { get; private set; }
        public string? Failure { get; private set; }
        public Task<ProcessingJobLease?> TryLeaseAsync(string worker, TimeSpan duration, CancellationToken ct) =>
            Task.FromResult<ProcessingJobLease?>(new(Guid.NewGuid(), "BuildDocumentPdf", exportId.ToString(), 1, DateTimeOffset.UtcNow.AddMinutes(2)));
        public Task CompleteAsync(Guid id, string worker, CancellationToken ct) { Completed = true; return Task.CompletedTask; }
        public Task RescheduleAsync(Guid id, string worker, string code, CancellationToken ct) { Failure = code; return Task.CompletedTask; }
        public Task<bool> HeartbeatAsync(Guid id, string worker, TimeSpan duration, CancellationToken ct) => Task.FromResult(true);
        public Task EnqueueAsync(string type, string payload, string key, CancellationToken ct) => throw new NotSupportedException();
    }
}
