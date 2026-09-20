using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SuperScanner.Application.Abstractions;
using SuperScanner.Application.Uploads;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Uploads;
using SuperScanner.Worker;
using SuperScanner.Application.Tests.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SuperScanner.Infrastructure.Persistence;
using SuperScanner.Infrastructure.Processing;

namespace SuperScanner.Application.Tests.Processing;

public sealed class UploadValidationJobRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 3, 0, 0, TimeSpan.Zero);
    private static readonly byte[] PdfBytes = "%PDF-1.7\nworker"u8.ToArray();

    [Fact]
    public async Task RunOnceAsync_CleanUploadCompletesLeasedJob()
    {
        using var fixture = CreateFixture(new CleanScanner());

        var foundWork = await fixture.Runner.RunOnceAsync(
            "worker-a",
            TimeSpan.FromMinutes(2),
            CancellationToken.None);

        Assert.True(foundWork);
        Assert.Equal(fixture.Queue.JobId, fixture.Queue.CompletedJobId);
        Assert.Null(fixture.Queue.RescheduledErrorCode);
        Assert.Equal("ExpandDocumentImport", fixture.Queue.EnqueuedType);
        Assert.Equal(fixture.Upload.Id.ToString(), fixture.Queue.EnqueuedPayload);
        Assert.Equal($"upload:{fixture.Upload.Id}:expand:v1", fixture.Queue.EnqueuedKey);
        Assert.Equal(UploadIntentState.Accepted, fixture.Upload.State);
    }

    [Fact]
    public async Task RunOnceAsync_ScannerOutageReschedulesWithSafeErrorCode()
    {
        using var fixture = CreateFixture(new UnavailableScanner());

        var foundWork = await fixture.Runner.RunOnceAsync(
            "worker-a",
            TimeSpan.FromMinutes(2),
            CancellationToken.None);

        Assert.True(foundWork);
        Assert.Null(fixture.Queue.CompletedJobId);
        Assert.Equal("scanner_unavailable", fixture.Queue.RescheduledErrorCode);
        Assert.Equal(UploadIntentState.PendingValidation, fixture.Upload.State);
        Assert.Empty(fixture.Store.DeletedKeys);
    }

    [Fact]
    public async Task RunOnceAsync_HeartbeatsWhileValidationIsRunning()
    {
        using var fixture = CreateFixture(new SlowCleanScanner(TimeSpan.FromMilliseconds(160)));

        await fixture.Runner.RunOnceAsync(
            "worker-a",
            TimeSpan.FromMilliseconds(90),
            CancellationToken.None);

        Assert.True(fixture.Queue.HeartbeatCalls >= 1);
    }

    private static RunnerFixture CreateFixture(IMalwareScanner scanner)
    {
        var document = Document.Create(Guid.NewGuid(), "user-a", "Private scan", Now);
        var page = document.AddPage(Guid.NewGuid(), 50, Now);
        var upload = UploadIntent.Create(
            Guid.NewGuid(),
            "user-a",
            document.Id,
            $"quarantine/{document.Id:N}/{Guid.NewGuid():N}",
            "scan.pdf",
            "application/pdf",
            PdfBytes.LongLength,
            Convert.ToHexString(SHA256.HashData(PdfBytes)).ToLowerInvariant(),
            Now.AddMinutes(5));
        upload.TryMarkPendingValidation(Now);

        var queue = new RecordingQueue(upload.Id);
        var store = new RecordingStore(PdfBytes);
        var services = new ServiceCollection();
        services.AddSingleton<IProcessingJobQueue>(queue);
        services.AddSingleton<IMalwareScanner>(scanner);
        services.AddSingleton(new ValidateUpload(
            new Repository(upload, document),
            store,
            new FixedClock(Now),
            new UploadValidationPolicy(1024),
            new RecordingAuditWriter()));
        var provider = services.BuildServiceProvider();
        var runner = new UploadValidationJobRunner(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<UploadValidationJobRunner>.Instance);
        return new RunnerFixture(upload, queue, store, runner, provider);
    }

    [Theory]
    [InlineData(false, 1, null, true)]
    [InlineData(true, 1, "import_failed", true)]
    [InlineData(true, 3, "import_failed", true)]
    [InlineData(false, 1, "import_failed", false)]
    public async Task ImportJob_DispatchesAndBoundsFailure(bool storageOutage, int attempt, string? error, bool encrypted)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new WorkerLockOrderInterceptor(new WorkerLockOrderObserver()))
            .Options);
        await db.Database.EnsureCreatedAsync();
        var document = Document.Create(Guid.NewGuid(), "owner", "Import", Now);
        var upload = UploadIntent.Create(Guid.NewGuid(), "owner", document.Id, "quarantine", "scan.pdf",
            "application/pdf", 100, new string('a', 64), Now.AddHours(1));
        upload.TryMarkPendingValidation(Now);
        upload.Accept("imports/source", Now);
        upload.BeginExpansion(3);
        var pages = document.AppendImportedPages(upload.Id, [1, 2, 3], 50, Now);
        pages[0].MarkImportReady("original", "image/png");
        pages[0].SetPreview("preview", "thumb");
        pages[0].InitializeCrop();
        pages[0].MarkReady();
        pages[1].MarkFailed("pdf_render_failed");
        var removed = document.AppendImportedPages(Guid.NewGuid(), [1], 50, Now)[0];
        document.RemovePage(removed.Id, "owner", Now);
        db.AddRange(document, upload);
        await db.SaveChangesAsync();
        var store = new ImportStore(storageOutage);
        var queue = new RecordingQueue(upload.Id) { Type = "ExpandDocumentImport", AttemptCount = attempt };
        var options = Options.Create(new DocumentImportOptions { MaxAttempts = 3 });
        var crop = new CropProcessor(db, store, new ConfigurationBuilder().Build(),
            Options.Create(new DocumentBoundaryOptions()), new DocumentBoundaryHealth(), NullLogger<CropProcessor>.Instance);
        var services = new ServiceCollection();
        services.AddSingleton<IProcessingJobQueue>(queue);
        services.AddSingleton(db);
        services.AddSingleton<IOptions<DocumentImportOptions>>(options);
        // No validator/scanner registration: expansion must depend only on import services.
        services.AddSingleton(new DocumentImportProcessor(db, store, new FailingPdfTool(encrypted),
            new DocumentPreviewProcessor(db, store), crop, options));
        await using var provider = services.BuildServiceProvider();
        var runner = new UploadValidationJobRunner(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<UploadValidationJobRunner>.Instance);

        await runner.RunOnceAsync("worker-a", TimeSpan.FromMinutes(2), CancellationToken.None);

        Assert.Equal(error, queue.RescheduledErrorCode);
        Assert.Equal(error != null ? null : queue.JobId, queue.CompletedJobId);
        db.ChangeTracker.Clear();
        var stored = await db.UploadIntents.SingleAsync();
        var storedPages = await db.Pages.ToDictionaryAsync(p => p.Id);
        if (!encrypted)
        {
            Assert.Equal("pdf_render_failed", stored.ExpansionErrorCode);
            Assert.Equal(PageState.Ready, storedPages[pages[0].Id].State);
            Assert.Equal(2, stored.FailedPageCount);
        }
        else if (!storageOutage) Assert.Equal("pdf_encrypted", stored.ExpansionErrorCode);
        else if (attempt == 3)
        {
            Assert.Equal("import_failed", stored.ExpansionErrorCode);
            Assert.Equal(1, stored.CreatedPageCount);
            Assert.Equal(2, stored.FailedPageCount);
            Assert.Equal(PageState.Ready, storedPages[pages[0].Id].State);
            Assert.Equal("pdf_render_failed", storedPages[pages[1].Id].FailureCode);
            Assert.Equal("import_failed", storedPages[pages[2].Id].FailureCode);
            Assert.Equal(PageState.Importing, storedPages[removed.Id].State);
        }
        else
        {
            Assert.Null(stored.ExpansionErrorCode);
            Assert.Equal(PageState.Importing, storedPages[pages[2].Id].State);
        }
    }

    [Fact]
    public async Task ImportJob_RejectsExtraPayloadSegments()
    {
        using var fixture = CreateFixture(new CleanScanner());
        fixture.Queue.Type = "ExpandDocumentImport";
        fixture.Queue.Payload = $"{fixture.Upload.Id}:unexpected";
        await fixture.Runner.RunOnceAsync("worker-a", TimeSpan.FromMinutes(2), CancellationToken.None);
        Assert.Equal("invalid_job", fixture.Queue.RescheduledErrorCode);
        Assert.Null(fixture.Queue.CompletedJobId);
    }

    private sealed class FailingPdfTool(bool encrypted) : IPdfImportTool
    {
        public Task<PdfInspection> InspectAsync(string path, CancellationToken ct) => Task.FromResult(new PdfInspection(3, encrypted, 100));
        public Task RenderPageAsync(string path, int page, string output, CancellationToken ct) => throw new PdfImportException("pdf_render_failed");
    }

    private sealed class ImportStore(bool outage) : IObjectStore
    {
        public Task<ObjectCreationResult> WriteIfAbsentAsync(string key, string mediaType, Stream content, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<StoredObjectInfo?> HeadAsync(string key, CancellationToken ct) => outage
            ? throw new IOException("Sensitive storage error")
            : Task.FromResult<StoredObjectInfo?>(key == "imports/source" ? new StoredObjectInfo(PdfBytes.Length, "application/pdf", "etag") : null);
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(PdfBytes));
        public Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task PromoteAsync(string from, string to, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed record RunnerFixture(
        UploadIntent Upload,
        RecordingQueue Queue,
        RecordingStore Store,
        UploadValidationJobRunner Runner,
        ServiceProvider Provider) : IDisposable
    {
        public void Dispose() => Provider.Dispose();
    }

    private sealed class RecordingQueue(Guid uploadId) : IProcessingJobQueue
    {
        public string Type { get; set; } = "ValidateUpload";
        public string Payload { get; set; } = uploadId.ToString();
        public int AttemptCount { get; set; } = 1;
        public Guid JobId { get; } = Guid.NewGuid();
        public Guid? CompletedJobId { get; private set; }
        public string? RescheduledErrorCode { get; private set; }
        public int HeartbeatCalls { get; private set; }
        public string? EnqueuedType { get; private set; }
        public string? EnqueuedPayload { get; private set; }
        public string? EnqueuedKey { get; private set; }

        public Task<ProcessingJobLease?> TryLeaseAsync(
            string workerId,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken) =>
            Task.FromResult<ProcessingJobLease?>(new ProcessingJobLease(
                JobId,
                Type,
                Payload,
                AttemptCount,
                Now.Add(leaseDuration)));

        public Task CompleteAsync(Guid jobId, string workerId, CancellationToken cancellationToken)
        {
            CompletedJobId = jobId;
            return Task.CompletedTask;
        }

        public Task RescheduleAsync(
            Guid jobId,
            string workerId,
            string errorCode,
            CancellationToken cancellationToken)
        {
            RescheduledErrorCode = errorCode;
            return Task.CompletedTask;
        }

        public Task<bool> HeartbeatAsync(
            Guid jobId,
            string workerId,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            HeartbeatCalls++;
            return Task.FromResult(true);
        }

        public Task EnqueueAsync(
            string type,
            string payload,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            EnqueuedType = type;
            EnqueuedPayload = payload;
            EnqueuedKey = idempotencyKey;
            return Task.CompletedTask;
        }
    }

    private sealed class Repository(UploadIntent upload, Document document) : IUploadValidationRepository
    {
        public Task<UploadValidationTarget?> FindAsync(Guid uploadId, CancellationToken cancellationToken) =>
            Task.FromResult<UploadValidationTarget?>(new UploadValidationTarget(upload, document));

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingStore(byte[] bytes) : IObjectStore
    {
        public Task<ObjectCreationResult> WriteIfAbsentAsync(string key, string mediaType, Stream content, CancellationToken ct) =>
            throw new NotSupportedException();
        public List<string> DeletedKeys { get; } = [];

        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));

        public Task PromoteAsync(string quarantineKey, string acceptedKey, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
        {
            DeletedKeys.Add(objectKey);
            return Task.CompletedTask;
        }

        public Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StoredObjectInfo?> HeadAsync(string objectKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CleanScanner : IMalwareScanner
    {
        public Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken) =>
            Task.FromResult(MalwareScanResult.Clean());
    }

    private sealed class UnavailableScanner : IMalwareScanner
    {
        public Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken) =>
            Task.FromException<MalwareScanResult>(new MalwareScannerUnavailableException());
    }

    private sealed class SlowCleanScanner(TimeSpan delay) : IMalwareScanner
    {
        public async Task<MalwareScanResult> ScanAsync(
            Stream content,
            CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return MalwareScanResult.Clean();
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
