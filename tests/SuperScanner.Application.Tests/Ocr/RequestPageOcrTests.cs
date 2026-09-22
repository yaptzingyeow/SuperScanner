using SuperScanner.Application.Abstractions;
using SuperScanner.Application.Ocr;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Ocr;

namespace SuperScanner.Application.Tests.Ocr;

public sealed class RequestPageOcrTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 1, 0, 0, TimeSpan.Zero);
    private readonly Guid documentId = Guid.NewGuid();
    private readonly Guid pageId = Guid.NewGuid();

    [Fact]
    public async Task TwoRequestsForSameSource_ReturnSameResultAndOneJob()
    {
        var repository = ReadyRepository();
        var queue = new RecordingQueue();
        var command = new RequestPageOcr(repository, queue, new FixedClock(Now));

        var first = await command.HandleAsync("owner", documentId, pageId, false, default);
        var second = await command.HandleAsync("owner", documentId, pageId, false, default);

        Assert.Equal(first.ResultId, second.ResultId);
        var job = Assert.Single(queue.Enqueued.Values);
        Assert.Equal(first.ResultId?.ToString(), job.Payload);
        Assert.Equal($"page:{pageId}:ocr:{first.SourceFingerprint}", job.IdempotencyKey);
    }

    [Fact]
    public async Task MissingOrUnownedPage_IsHiddenAsNotFound()
    {
        var command = new RequestPageOcr(new MemoryOcrRepository(null),
            new RecordingQueue(), new FixedClock(Now));

        await Assert.ThrowsAsync<OcrResourceNotFoundException>(() =>
            command.HandleAsync("owner", documentId, pageId, false, default));
    }

    [Fact]
    public async Task OwnedPageWithoutReadyPreview_ReturnsConflictSignal()
    {
        var source = new OcrPageSource(pageId, PageState.Processing, null, "image/jpeg");
        var command = new RequestPageOcr(new MemoryOcrRepository(source),
            new RecordingQueue(), new FixedClock(Now));

        await Assert.ThrowsAsync<OcrPageNotReadyException>(() =>
            command.HandleAsync("owner", documentId, pageId, false, default));
    }

    [Fact]
    public async Task Retry_ReactivatesRetryableResultAndExistingFailedJob()
    {
        var repository = ReadyRepository();
        var fingerprint = OcrSourceFingerprint.Create(repository.Source!.SourceObjectKey!);
        var failed = PageOcrResult.Queue(Guid.NewGuid(), pageId,
            repository.Source.SourceObjectKey!, fingerprint, "en", Now);
        failed.Fail("ocr_provider_unavailable", true, Now.AddSeconds(1));
        repository.Results.Add(failed);
        var queue = new RecordingQueue();
        queue.SeedFailed($"page:{pageId}:ocr:{fingerprint}", failed.Id.ToString());
        var command = new RequestPageOcr(repository, queue, new FixedClock(Now.AddMinutes(1)));

        var result = await command.HandleAsync("owner", documentId, pageId, true, default);

        Assert.Equal("Queued", result.State);
        Assert.Equal(1, queue.RetryCalls);
        Assert.Equal(0, failed.AttemptCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retry_RejectsMissingPermissionOrPermanentFailure(bool requestRetry)
    {
        var repository = ReadyRepository();
        var fingerprint = OcrSourceFingerprint.Create(repository.Source!.SourceObjectKey!);
        var failed = PageOcrResult.Queue(Guid.NewGuid(), pageId,
            repository.Source.SourceObjectKey!, fingerprint, "en", Now);
        failed.Fail("ocr_invalid_response", requestRetry, Now.AddSeconds(1));
        repository.Results.Add(failed);
        var command = new RequestPageOcr(repository, new RecordingQueue(), new FixedClock(Now));

        await Assert.ThrowsAsync<OcrRetryNotAllowedException>(() =>
            command.HandleAsync("owner", documentId, pageId, !requestRetry, default));
    }

    private MemoryOcrRepository ReadyRepository() => new(new OcrPageSource(
        pageId, PageState.Ready, "previews/document/page/crop-1.jpg", "image/jpeg"));
}

internal sealed class MemoryOcrRepository(OcrPageSource? source) : IOcrRepository
{
    public OcrPageSource? Source { get; } = source;
    public List<PageOcrResult> Results { get; } = [];

    public Task<IOcrTransaction> BeginTransactionAsync(CancellationToken ct) =>
        Task.FromResult<IOcrTransaction>(new MemoryTransaction());

    public Task<OcrPageSource?> FindOwnedSourceAsync(
        string ownerUid, Guid documentId, Guid pageId, bool forUpdate, CancellationToken ct) =>
        Task.FromResult(Source is not null && Source.PageId == pageId ? Source : null);

    public Task<PageOcrResult?> FindBySourceAsync(
        Guid pageId, string sourceFingerprint, bool forUpdate, CancellationToken ct) =>
        Task.FromResult(Results.SingleOrDefault(result =>
            result.PageId == pageId && result.SourceFingerprint == sourceFingerprint));

    public Task AddAsync(PageOcrResult result, CancellationToken ct)
    {
        Results.Add(result);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;

    private sealed class MemoryTransaction : IOcrTransaction
    {
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class RecordingQueue : IProcessingJobQueue
{
    public Dictionary<string, RecordedJob> Enqueued { get; } = new(StringComparer.Ordinal);
    public int RetryCalls { get; private set; }

    public void SeedFailed(string key, string payload) => Enqueued[key] = new(key, payload, true);

    public Task EnqueueAsync(string type, string payload, string idempotencyKey, CancellationToken ct)
    {
        Enqueued.TryAdd(idempotencyKey, new(idempotencyKey, payload, false));
        return Task.CompletedTask;
    }

    public Task RetryFailedAsync(string idempotencyKey, CancellationToken ct)
    {
        if (!Enqueued.TryGetValue(idempotencyKey, out var job) || !job.Failed)
            throw new InvalidOperationException();
        Enqueued[idempotencyKey] = job with { Failed = false };
        RetryCalls++;
        return Task.CompletedTask;
    }

    public Task<ProcessingJobLease?> TryLeaseAsync(string workerId, TimeSpan leaseDuration, CancellationToken ct) =>
        throw new NotSupportedException();
    public Task<bool> HeartbeatAsync(Guid jobId, string workerId, TimeSpan leaseDuration, CancellationToken ct) =>
        throw new NotSupportedException();
    public Task CompleteAsync(Guid jobId, string workerId, CancellationToken ct) =>
        throw new NotSupportedException();
    public Task RescheduleAsync(Guid jobId, string workerId, string errorCode, CancellationToken ct) =>
        throw new NotSupportedException();

    public sealed record RecordedJob(string IdempotencyKey, string Payload, bool Failed);
}

internal sealed class FixedClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; } = utcNow;
}
