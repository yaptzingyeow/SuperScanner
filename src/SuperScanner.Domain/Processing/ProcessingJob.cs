namespace SuperScanner.Domain.Processing;

public enum ProcessingJobStatus
{
    Queued
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
            CreatedAt = createdAt
        };
}
