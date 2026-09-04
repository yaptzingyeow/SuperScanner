namespace SuperScanner.Application.Abstractions;

public sealed record ProcessingJobLease(
    Guid Id,
    string Type,
    string Payload,
    int AttemptCount,
    DateTimeOffset LeaseExpiresAt);

public interface IProcessingJobQueue
{
    Task EnqueueAsync(
        string type,
        string payload,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<ProcessingJobLease?> TryLeaseAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> HeartbeatAsync(
        Guid jobId,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task CompleteAsync(Guid jobId, string workerId, CancellationToken cancellationToken);

    Task RescheduleAsync(
        Guid jobId,
        string workerId,
        string errorCode,
        CancellationToken cancellationToken);
}
