using SuperScanner.Application.Abstractions;
using SuperScanner.Application.Uploads;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Uploads;

namespace SuperScanner.Application.Tests.Uploads;

public sealed class CompleteUploadTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Complete_IsIdempotentAndQueuesValidationOnlyOnce()
    {
        var document = Document.Create(Guid.NewGuid(), "user-a", "Form", Now);
        var page = document.AddPage(Guid.NewGuid(), 50, Now);
        var upload = UploadIntent.Create(
            Guid.NewGuid(),
            "user-a",
            document.Id,
            page.Id,
            $"quarantine/{document.Id:N}/{Guid.NewGuid():N}",
            "application/pdf",
            1200,
            new string('a', 64),
            Now.AddMinutes(5));
        var repository = new InMemoryUploadIntentRepository(upload);
        var objectStore = new HeadObjectStore(new StoredObjectInfo(1200, "application/pdf", "etag"));
        var jobs = new RecordingProcessingJobQueue();
        var handler = new CompleteUpload(repository, objectStore, jobs, new FixedClock(Now));

        var first = await handler.HandleAsync("user-a", document.Id, upload.Id, CancellationToken.None);
        var repeated = await handler.HandleAsync("user-a", document.Id, upload.Id, CancellationToken.None);

        Assert.Equal(UploadIntentState.PendingValidation, first.State);
        Assert.Equal(first, repeated);
        var job = Assert.Single(jobs.Jobs);
        Assert.Equal("ValidateUpload", job.Type);
        Assert.Equal(upload.Id.ToString(), job.Payload);
        Assert.Equal($"upload:{upload.Id}:validate", job.IdempotencyKey);
        Assert.Equal(1, objectStore.HeadCalls);
        Assert.Null(page.OriginalObjectKey);
    }

    [Fact]
    public async Task Complete_RejectsExpiredIntentBeforeAccessingObjectStorage()
    {
        var document = Document.Create(Guid.NewGuid(), "user-a", "Form", Now);
        var page = document.AddPage(Guid.NewGuid(), 50, Now);
        var upload = UploadIntent.Create(
            Guid.NewGuid(),
            "user-a",
            document.Id,
            page.Id,
            "quarantine/expired",
            "application/pdf",
            1200,
            new string('a', 64),
            Now);
        var repository = new InMemoryUploadIntentRepository(upload);
        var objectStore = new HeadObjectStore(new StoredObjectInfo(1200, "application/pdf", "etag"));
        var jobs = new RecordingProcessingJobQueue();
        var handler = new CompleteUpload(repository, objectStore, jobs, new FixedClock(Now));

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            "user-a",
            document.Id,
            upload.Id,
            CancellationToken.None));

        Assert.Equal(0, objectStore.HeadCalls);
        Assert.Empty(jobs.Jobs);
        Assert.Equal(UploadIntentState.AwaitingUpload, upload.State);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(1199L, "application/pdf")]
    [InlineData(1200L, "image/png")]
    public async Task Complete_RejectsMissingOrMismatchedQuarantineObject(
        long? storedSizeBytes,
        string? storedMediaType)
    {
        var document = Document.Create(Guid.NewGuid(), "user-a", "Form", Now);
        var page = document.AddPage(Guid.NewGuid(), 50, Now);
        var upload = UploadIntent.Create(
            Guid.NewGuid(),
            "user-a",
            document.Id,
            page.Id,
            "quarantine/pending",
            "application/pdf",
            1200,
            new string('a', 64),
            Now.AddMinutes(5));
        var repository = new InMemoryUploadIntentRepository(upload);
        StoredObjectInfo? storedObject = storedSizeBytes.HasValue
            ? new StoredObjectInfo(storedSizeBytes.Value, storedMediaType!, "etag")
            : null;
        var jobs = new RecordingProcessingJobQueue();
        var handler = new CompleteUpload(
            repository,
            new HeadObjectStore(storedObject),
            jobs,
            new FixedClock(Now));

        await Assert.ThrowsAsync<InvalidDataException>(() => handler.HandleAsync(
            "user-a",
            document.Id,
            upload.Id,
            CancellationToken.None));

        Assert.Empty(jobs.Jobs);
        Assert.Equal(UploadIntentState.AwaitingUpload, upload.State);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class InMemoryUploadIntentRepository(UploadIntent upload) : IUploadIntentRepository
    {
        public Task<Document?> FindOwnedDocumentAsync(
            string ownerFirebaseUid,
            Guid documentId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UploadIntent?> FindOwnedUploadAsync(
            string ownerFirebaseUid,
            Guid documentId,
            Guid uploadId,
            CancellationToken cancellationToken) =>
            Task.FromResult<UploadIntent?>(
                upload.OwnerFirebaseUid == ownerFirebaseUid &&
                upload.DocumentId == documentId &&
                upload.Id == uploadId
                    ? upload
                    : null);

        public Task AddAsync(
            UploadIntent uploadIntent,
            Page page,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class HeadObjectStore(StoredObjectInfo? info) : IObjectStore
    {
        public int HeadCalls { get; private set; }

        public Task<StoredObjectInfo?> HeadAsync(string objectKey, CancellationToken cancellationToken)
        {
            HeadCalls++;
            return Task.FromResult<StoredObjectInfo?>(info);
        }

        public Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task PromoteAsync(
            string quarantineKey,
            string acceptedKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingProcessingJobQueue : IProcessingJobQueue
    {
        public List<JobRequest> Jobs { get; } = [];

        public Task EnqueueAsync(
            string type,
            string payload,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            if (Jobs.All(job => job.IdempotencyKey != idempotencyKey))
            {
                Jobs.Add(new JobRequest(type, payload, idempotencyKey));
            }

            return Task.CompletedTask;
        }

        public Task<ProcessingJobLease?> TryLeaseAsync(
            string workerId,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> HeartbeatAsync(
            Guid jobId,
            string workerId,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CompleteAsync(Guid jobId, string workerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RescheduleAsync(
            Guid jobId,
            string workerId,
            string errorCode,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed record JobRequest(string Type, string Payload, string IdempotencyKey);
}
