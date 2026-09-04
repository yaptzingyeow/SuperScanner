using Microsoft.Extensions.DependencyInjection;
using SuperScanner.Application.Abstractions;
using SuperScanner.Application.Uploads;

namespace SuperScanner.Worker;

public sealed class UploadValidationJobRunner(
    IServiceScopeFactory scopeFactory,
    ILogger<UploadValidationJobRunner> logger)
{
    public async Task<bool> RunOnceAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IProcessingJobQueue>();
        var lease = await queue.TryLeaseAsync(workerId, leaseDuration, cancellationToken);
        if (lease is null)
        {
            return false;
        }

        using var workCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = RunHeartbeatAsync(
            lease.Id,
            workerId,
            leaseDuration,
            workCancellation,
            cancellationToken);
        try
        {
            if (!string.Equals(lease.Type, "ValidateUpload", StringComparison.Ordinal) ||
                !Guid.TryParse(lease.Payload, out var uploadId))
            {
                await queue.RescheduleAsync(lease.Id, workerId, "invalid_job", cancellationToken);
                return true;
            }

            var validator = scope.ServiceProvider.GetRequiredService<ValidateUpload>();
            var scanner = scope.ServiceProvider.GetRequiredService<IMalwareScanner>();
            try
            {
                await validator.ValidateAsync(uploadId, scanner, workCancellation.Token);
                await queue.CompleteAsync(lease.Id, workerId, cancellationToken);
                logger.LogInformation(
                    "Upload validation job completed. JobId={JobId} UploadId={UploadId}",
                    lease.Id,
                    uploadId);
            }
            catch (MalwareScannerUnavailableException)
            {
                await queue.RescheduleAsync(
                    lease.Id,
                    workerId,
                    "scanner_unavailable",
                    cancellationToken);
                logger.LogWarning(
                    "Upload validation job rescheduled. JobId={JobId} UploadId={UploadId} ErrorCode={ErrorCode}",
                    lease.Id,
                    uploadId,
                    "scanner_unavailable");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    "Upload validation lease lost. JobId={JobId} UploadId={UploadId} ErrorCode={ErrorCode}",
                    lease.Id,
                    uploadId,
                    "lease_lost");
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                await queue.RescheduleAsync(
                    lease.Id,
                    workerId,
                    "validation_failed",
                    cancellationToken);
                logger.LogWarning(
                    "Upload validation job rescheduled. JobId={JobId} UploadId={UploadId} ErrorCode={ErrorCode}",
                    lease.Id,
                    uploadId,
                    "validation_failed");
            }

            return true;
        }
        finally
        {
            workCancellation.Cancel();
            await heartbeat;
        }
    }

    private async Task RunHeartbeatAsync(
        Guid jobId,
        string workerId,
        TimeSpan leaseDuration,
        CancellationTokenSource workCancellation,
        CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromTicks(Math.Max(1, leaseDuration.Ticks / 3));
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(workCancellation.Token))
            {
                await using var heartbeatScope = scopeFactory.CreateAsyncScope();
                var heartbeatQueue = heartbeatScope.ServiceProvider.GetRequiredService<IProcessingJobQueue>();
                var extended = await heartbeatQueue.HeartbeatAsync(
                    jobId,
                    workerId,
                    leaseDuration,
                    workCancellation.Token);
                if (!extended)
                {
                    workCancellation.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (workCancellation.IsCancellationRequested || stoppingToken.IsCancellationRequested)
        {
        }
    }
}
