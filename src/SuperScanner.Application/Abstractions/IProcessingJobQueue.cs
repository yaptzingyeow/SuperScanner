namespace SuperScanner.Application.Abstractions;

public interface IProcessingJobQueue
{
    Task EnqueueAsync(
        string type,
        string payload,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
