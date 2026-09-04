namespace SuperScanner.Worker;

public sealed class UploadValidationWorker(
    UploadValidationJobRunner runner,
    ILogger<UploadValidationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan EmptyQueueDelay = TimeSpan.FromSeconds(1);
    private readonly string _workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var foundWork = await runner.RunOnceAsync(_workerId, LeaseDuration, stoppingToken);
                if (!foundWork)
                {
                    await Task.Delay(EmptyQueueDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                logger.LogError("Upload validation worker iteration failed. ErrorCode={ErrorCode}", "worker_iteration_failed");
                await Task.Delay(EmptyQueueDelay, stoppingToken);
            }
        }
    }
}
