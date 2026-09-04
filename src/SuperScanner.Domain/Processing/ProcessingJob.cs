namespace SuperScanner.Domain.Processing;

public enum ProcessingJobStatus
{
    Queued,
    Leased,
    Completed,
    Failed
}

public sealed class ProcessingJob
{
    private ProcessingJob()
    {
    }

    public Guid Id { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public ProcessingJobStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset AvailableAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public int AttemptCount { get; private set; }
    public string? WorkerId { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public string? ErrorCode { get; private set; }

    public static ProcessingJob Create(
        Guid id,
        string type,
        string payload,
        string idempotencyKey,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = id,
            Type = type,
            Payload = payload,
            IdempotencyKey = idempotencyKey,
            Status = ProcessingJobStatus.Queued,
            CreatedAt = createdAt,
            AvailableAt = createdAt,
            UpdatedAt = createdAt
        };

    public void Lease(string workerId, DateTimeOffset now, TimeSpan leaseDuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        if (Status == ProcessingJobStatus.Queued && AvailableAt > now ||
            Status == ProcessingJobStatus.Leased && LeaseExpiresAt > now ||
            Status is ProcessingJobStatus.Completed or ProcessingJobStatus.Failed)
        {
            throw new InvalidOperationException("The processing job is not available for leasing.");
        }

        Status = ProcessingJobStatus.Leased;
        WorkerId = workerId;
        LeaseExpiresAt = now.Add(leaseDuration);
        AttemptCount++;
        UpdatedAt = now;
        ErrorCode = null;
    }

    public void Complete(string workerId, DateTimeOffset now)
    {
        EnsureOwnedLease(workerId);
        Status = ProcessingJobStatus.Completed;
        WorkerId = null;
        LeaseExpiresAt = null;
        UpdatedAt = now;
        ErrorCode = null;
    }

    public void Reschedule(
        string workerId,
        DateTimeOffset now,
        string errorCode,
        IReadOnlyList<TimeSpan> retryDelays)
    {
        EnsureOwnedLease(workerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        WorkerId = null;
        LeaseExpiresAt = null;
        UpdatedAt = now;
        ErrorCode = errorCode;
        if (AttemptCount > retryDelays.Count)
        {
            Status = ProcessingJobStatus.Failed;
            return;
        }

        Status = ProcessingJobStatus.Queued;
        AvailableAt = now.Add(retryDelays[AttemptCount - 1]);
    }

    private void EnsureOwnedLease(string workerId)
    {
        if (Status != ProcessingJobStatus.Leased ||
            !string.Equals(WorkerId, workerId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The processing job lease is not owned by this worker.");
        }
    }
}
