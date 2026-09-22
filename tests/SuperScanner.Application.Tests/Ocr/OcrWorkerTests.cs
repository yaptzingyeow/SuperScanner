using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SuperScanner.Application.Abstractions;
using SuperScanner.Application.Ocr;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Ocr;
using SuperScanner.Infrastructure.Ocr;
using SuperScanner.Infrastructure.Persistence;
using SuperScanner.Worker;

namespace SuperScanner.Application.Tests.Ocr;

public sealed class OcrWorkerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EmptyRecognition_CompletesReadyWithoutChangingReadyPage()
    {
        await using var fixture = await Fixture.CreateAsync(new StaticProvider(
            new NormalizedOcrDocument("", "Fake", "empty-v1", [])));

        await fixture.Processor.RunAsync(fixture.Result.Id, 1, default);

        fixture.Db.ChangeTracker.Clear();
        var result = await fixture.Db.PageOcrResults.Include(x => x.Elements).SingleAsync();
        var page = await fixture.Db.Pages.SingleAsync();
        Assert.Equal(OcrResultState.Ready, result.State);
        Assert.Empty(result.Elements);
        Assert.Equal(PageState.Ready, page.State);
        Assert.Equal(fixture.Result.SourceObjectKey, fixture.Store.OpenedKey);
    }

    [Fact]
    public async Task LateOldRevisionCompletion_IsStoredButDoesNotChangePage()
    {
        var output = new NormalizedOcrDocument("Name", "Fake", "fixture-v1",
        [
            new("block-1", null, OcrElementKind.Block, "Name", .96,
                OcrTextType.Printed, 0,
                [new(0.1, 0.1), new(0.2, 0.1), new(0.2, 0.2), new(0.1, 0.2)]),
            new("line-1", "block-1", OcrElementKind.Line, "Name", .97,
                OcrTextType.Printed, 0,
                [new(0.1, 0.1), new(0.2, 0.1), new(0.2, 0.2), new(0.1, 0.2)]),
            new("word-1", "line-1", OcrElementKind.Word, "Name", .98,
                OcrTextType.Printed, 0,
                [new(0.1, 0.1), new(0.2, 0.1), new(0.2, 0.2), new(0.1, 0.2)])
        ]);
        await using var fixture = await Fixture.CreateAsync(new StaticProvider(output));
        var page = await fixture.Db.Pages.SingleAsync();
        page.SetPreview("previews/new-revision.jpg", "thumbs/new-revision.jpg");
        await fixture.Db.SaveChangesAsync();

        await fixture.Processor.RunAsync(fixture.Result.Id, 1, default);

        fixture.Db.ChangeTracker.Clear();
        var stored = await fixture.Db.PageOcrResults.Include(x => x.Elements).SingleAsync();
        var unchangedPage = await fixture.Db.Pages.SingleAsync();
        Assert.Equal(OcrResultState.Ready, stored.State);
        Assert.Equal(3, stored.Elements.Count);
        Assert.Equal("previews/new-revision.jpg", unchangedPage.PreviewObjectKey);
        Assert.Equal(PageState.Ready, unchangedPage.State);
    }

    [Fact]
    public async Task InvalidProviderOutput_DoesNotPersistPartialElements()
    {
        var invalid = new NormalizedOcrDocument("secret", "Fake", "invalid-v1",
        [
            new("one", "missing", OcrElementKind.Word, "secret", .9,
                OcrTextType.Printed, 0,
                [new(0.1, 0.1), new(0.2, 0.1), new(0.2, 0.2), new(0.1, 0.2)])
        ]);
        await using var fixture = await Fixture.CreateAsync(new StaticProvider(invalid));

        var exception = await Assert.ThrowsAsync<OcrProviderException>(() =>
            fixture.Processor.RunAsync(fixture.Result.Id, 1, default));

        Assert.Equal("ocr_invalid_response", exception.SafeCode);
        fixture.Db.ChangeTracker.Clear();
        var stored = await fixture.Db.PageOcrResults.Include(x => x.Elements).SingleAsync();
        Assert.Equal(OcrResultState.Processing, stored.State);
        Assert.Empty(stored.Elements);
        Assert.Equal(string.Empty, stored.FullText);
    }

    [Fact]
    public async Task FailAsync_PersistsOnlySafeTerminalData()
    {
        await using var fixture = await Fixture.CreateAsync(new StaticProvider(
            new NormalizedOcrDocument("", "Fake", "fixture-v1", [])));

        await fixture.Processor.FailAsync(fixture.Result.Id, "ocr_timeout", true, default);

        fixture.Db.ChangeTracker.Clear();
        var stored = await fixture.Db.PageOcrResults.SingleAsync();
        Assert.Equal(OcrResultState.Failed, stored.State);
        Assert.Equal("ocr_timeout", stored.FailureCode);
        Assert.True(stored.FailureRetryable);
    }

    [Fact]
    public async Task RetryableProviderFailure_ReschedulesBeforeAttemptLimit()
    {
        await using var fixture = await Fixture.CreateAsync(
            new ThrowingProvider(new OcrProviderException("ocr_timeout", true)));
        var queue = new RunnerQueue(fixture.Result.Id, attemptCount: 1);
        using var services = BuildRunnerServices(fixture, queue, maxAttempts: 3);
        var runner = new UploadValidationJobRunner(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<UploadValidationJobRunner>.Instance);

        await runner.RunOnceAsync("worker-a", TimeSpan.FromMinutes(2), default);

        Assert.Equal("ocr_timeout", queue.RescheduledCode);
        Assert.Null(queue.FailedCode);
        Assert.Null(queue.CompletedJobId);
    }

    [Fact]
    public async Task RetryableProviderFailure_AtAttemptLimitFailsResultAndJob()
    {
        await using var fixture = await Fixture.CreateAsync(
            new ThrowingProvider(new OcrProviderException("ocr_timeout", true)));
        var queue = new RunnerQueue(fixture.Result.Id, attemptCount: 3);
        using var services = BuildRunnerServices(fixture, queue, maxAttempts: 3);
        var runner = new UploadValidationJobRunner(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<UploadValidationJobRunner>.Instance);

        await runner.RunOnceAsync("worker-a", TimeSpan.FromMinutes(2), default);

        Assert.Equal("ocr_timeout", queue.FailedCode);
        Assert.Null(queue.RescheduledCode);
        fixture.Db.ChangeTracker.Clear();
        var result = await fixture.Db.PageOcrResults.SingleAsync();
        Assert.Equal(OcrResultState.Failed, result.State);
        Assert.True(result.FailureRetryable);
    }

    private static ServiceProvider BuildRunnerServices(
        Fixture fixture, RunnerQueue queue, int maxAttempts)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProcessingJobQueue>(queue);
        services.AddSingleton(fixture.Processor);
        services.AddSingleton<IOptions<OcrOptions>>(Options.Create(new OcrOptions
        {
            Enabled = true,
            Provider = "Fake",
            MaxAttempts = maxAttempts
        }));
        return services.BuildServiceProvider();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private Fixture(SqliteConnection connection, AppDbContext db, PageOcrResult result,
            RecordingStore store, OcrProcessor processor)
        {
            this.connection = connection;
            Db = db;
            Result = result;
            Store = store;
            Processor = processor;
        }

        public AppDbContext Db { get; }
        public PageOcrResult Result { get; }
        public RecordingStore Store { get; }
        public OcrProcessor Processor { get; }

        public static async Task<Fixture> CreateAsync(IOcrProvider provider)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options);
            await db.Database.EnsureCreatedAsync();
            var document = Document.Create(Guid.NewGuid(), "owner", "OCR", Now);
            var page = document.AddPage(Guid.NewGuid(), 50, Now);
            page.AcceptOriginal("original/page.jpg");
            page.SetPreview("previews/page.jpg", "thumbs/page.jpg");
            page.InitializeCrop();
            page.MarkReady();
            var result = PageOcrResult.Queue(Guid.NewGuid(), page.Id, page.PreviewObjectKey!,
                OcrSourceFingerprint.Create(page.PreviewObjectKey!), "en", Now);
            db.AddRange(document, result);
            await db.SaveChangesAsync();
            var store = new RecordingStore();
            var processor = new OcrProcessor(db, store, provider, new FixedClock(),
                Options.Create(new OcrOptions { Enabled = true, Provider = "Fake" }), new OcrMetrics());
            return new Fixture(connection, db, result, store, processor);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class StaticProvider(NormalizedOcrDocument result) : IOcrProvider
    {
        public Task<NormalizedOcrDocument> RecognizeAsync(OcrInput input, CancellationToken ct) =>
            Task.FromResult(result);
    }

    private sealed class ThrowingProvider(OcrProviderException exception) : IOcrProvider
    {
        public Task<NormalizedOcrDocument> RecognizeAsync(OcrInput input, CancellationToken ct) =>
            Task.FromException<NormalizedOcrDocument>(exception);
    }

    private sealed class RunnerQueue(Guid resultId, int attemptCount) : IProcessingJobQueue
    {
        public Guid JobId { get; } = Guid.NewGuid();
        public Guid? CompletedJobId { get; private set; }
        public string? RescheduledCode { get; private set; }
        public string? FailedCode { get; private set; }
        public Task<ProcessingJobLease?> TryLeaseAsync(string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken) =>
            Task.FromResult<ProcessingJobLease?>(new(JobId, "RecognizePageText", resultId.ToString(),
                attemptCount, Now.Add(leaseDuration)));
        public Task<bool> HeartbeatAsync(Guid jobId, string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task CompleteAsync(Guid jobId, string workerId, CancellationToken cancellationToken)
        {
            CompletedJobId = jobId;
            return Task.CompletedTask;
        }
        public Task FailAsync(Guid jobId, string workerId, string errorCode, CancellationToken cancellationToken)
        {
            FailedCode = errorCode;
            return Task.CompletedTask;
        }
        public Task RescheduleAsync(Guid jobId, string workerId, string errorCode, CancellationToken cancellationToken)
        {
            RescheduledCode = errorCode;
            return Task.CompletedTask;
        }
        public Task EnqueueAsync(string type, string payload, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingStore : IObjectStore
    {
        public string? OpenedKey { get; private set; }
        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken)
        {
            OpenedKey = objectKey;
            return Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
        }
        public Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StoredObjectInfo?> HeadAsync(string objectKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task PromoteAsync(string quarantineKey, string acceptedKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ObjectCreationResult> WriteIfAbsentAsync(string objectKey, string mediaType, Stream content, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedClock : IClock
    {
        private DateTimeOffset current = Now;
        public DateTimeOffset UtcNow => current = current.AddMilliseconds(10);
    }
}
