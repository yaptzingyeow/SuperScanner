using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SuperScanner.Application.Abstractions;
using SuperScanner.Application.Uploads;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Uploads;
using SuperScanner.Worker;
using SuperScanner.Application.Tests.TestDoubles;

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
            page.Id,
            $"quarantine/{document.Id:N}/{Guid.NewGuid():N}",
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
            new Repository(upload, page),
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
        public Guid JobId { get; } = Guid.NewGuid();
        public Guid? CompletedJobId { get; private set; }
        public string? RescheduledErrorCode { get; private set; }
        public int HeartbeatCalls { get; private set; }

        public Task<ProcessingJobLease?> TryLeaseAsync(
            string workerId,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken) =>
            Task.FromResult<ProcessingJobLease?>(new ProcessingJobLease(
                JobId,
                "ValidateUpload",
                uploadId.ToString(),
                1,
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
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Repository(UploadIntent upload, Page page) : IUploadValidationRepository
    {
        public Task<UploadValidationTarget?> FindAsync(Guid uploadId, CancellationToken cancellationToken) =>
            Task.FromResult<UploadValidationTarget?>(new UploadValidationTarget(upload, page));

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingStore(byte[] bytes) : IObjectStore
    {
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
