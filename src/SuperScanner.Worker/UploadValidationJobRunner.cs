using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SuperScanner.Application.Abstractions;
using SuperScanner.Application.Uploads;
using SuperScanner.Infrastructure.Processing;
using SuperScanner.Infrastructure.Persistence;

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
            var cropJob = lease.Type is "DetectDocumentEdges" or "ApplyPerspectiveCrop";
            var importJob = lease.Type is "ExpandDocumentImport" or "ProcessDocument";
            var exportJob = lease.Type == "BuildDocumentPdf";
            var parts = lease.Payload.Split(':');
            var revision = 0;
            if ((!cropJob && lease.Type != "ValidateUpload" && !importJob && !exportJob) ||
                !Guid.TryParse(parts[0], out var uploadId) ||
                (!cropJob && parts.Length != 1) ||
                (cropJob && (parts.Length != 2 || !int.TryParse(parts[1], out revision) || revision < 1)))
            {
                await queue.RescheduleAsync(lease.Id, workerId, "invalid_job", cancellationToken);
                return true;
            }

            try
            {
                if (exportJob)
                {
                    await scope.ServiceProvider.GetRequiredService<DocumentPdfBuilder>()
                        .BuildAsync(uploadId, workCancellation.Token);
                }
                else if (cropJob)
                {
                    await scope.ServiceProvider.GetRequiredService<CropProcessor>()
                        .RunAsync(uploadId, revision, lease.Type == "DetectDocumentEdges", workCancellation.Token);
                }
                else if (importJob)
                {
                    await scope.ServiceProvider.GetRequiredService<DocumentImportProcessor>()
                        .ProcessAsync(uploadId, workCancellation.Token);
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var upload = await db.UploadIntents.AsNoTracking().SingleAsync(x => x.Id == uploadId, workCancellation.Token);
                    await CropDocumentStatus.RefreshAsync(db, upload.DocumentId, workCancellation.Token);
                    if (upload.ExpansionErrorCode is "pdf_render_failed" or "pdf_timeout" or "pdf_tool_unavailable")
                        throw new InvalidOperationException("Import processing requires a retry.");
                }
                else
                {
                    var validator = scope.ServiceProvider.GetRequiredService<ValidateUpload>();
                    var scanner = scope.ServiceProvider.GetRequiredService<IMalwareScanner>();
                    var result = await validator.ValidateAsync(uploadId, scanner, workCancellation.Token);
                    if (result.Outcome == UploadValidationOutcome.Accepted)
                        await queue.EnqueueAsync("ExpandDocumentImport", uploadId.ToString(), $"upload:{uploadId}:expand:v1", workCancellation.Token);
                }
                await queue.CompleteAsync(lease.Id, workerId, cancellationToken);
                logger.LogInformation(
                    "Document job completed. JobType={JobType} JobId={JobId} UploadId={UploadId}",
                    lease.Type,
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
                if (cropJob && lease.AttemptCount >= 6)
                    await scope.ServiceProvider.GetRequiredService<CropProcessor>().FailAsync(uploadId, revision, cancellationToken);
                if (importJob && lease.AttemptCount >= scope.ServiceProvider.GetRequiredService<IOptions<DocumentImportOptions>>().Value.MaxAttempts)
                    await scope.ServiceProvider.GetRequiredService<DocumentImportProcessor>().FailAsync(uploadId, cancellationToken);
                var errorCode = exportJob ? "export_build_failed" : cropJob ? "crop_failed" : importJob ? "import_failed" : "validation_failed";
                await queue.RescheduleAsync(
                    lease.Id,
                    workerId,
                    errorCode,
                    cancellationToken);
                logger.LogWarning(
                    "Upload validation job rescheduled. JobId={JobId} UploadId={UploadId} ErrorCode={ErrorCode}",
                    lease.Id,
                    uploadId,
                    errorCode);
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
