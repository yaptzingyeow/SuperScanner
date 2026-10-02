using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ArksScanner.Application.Abstractions;
using ArksScanner.Application.Uploads;
using ArksScanner.Infrastructure.Processing;
using ArksScanner.Infrastructure.Persistence;
using ArksScanner.Infrastructure.Ocr;
using ArksScanner.Application.Ocr;
using ArksScanner.Infrastructure.TextEditing;

namespace ArksScanner.Worker;

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
            var automaticEnhancement = lease.Type is "AutoEnhanceDocument" or "ApplyAutomaticEnhancement";
            var cropJob = automaticEnhancement || lease.Type is "DetectDocumentEdges" or "ApplyPerspectiveCrop";
            var importJob = lease.Type is "ExpandDocumentImport" or "ProcessDocument";
            var exportJob = lease.Type == "BuildDocumentPdf";
            var ocrJob = lease.Type == "RecognizePageText";
            var textEditJob = lease.Type == "RenderTextEdit";
            var repairJob = lease.Type == "PreviewPageRepair";
            var parts = lease.Payload.Split(':');
            var revision = 0;
            var hasParsedId = Guid.TryParse(parts[0], out var uploadId);
            if ((!cropJob && lease.Type != "ValidateUpload" && !importJob && !exportJob && !ocrJob && !textEditJob && !repairJob) ||
                !hasParsedId ||
                (!cropJob && parts.Length != 1) ||
                (cropJob && (parts.Length != 2 || !int.TryParse(parts[1], out revision) || revision < 1)))
            {
                if (ocrJob)
                {
                    if (hasParsedId)
                        await FailOcrJobAsync(lease.Id, workerId, uploadId,
                            "invalid_job", false, cancellationToken);
                    else
                        await queue.FailAsync(lease.Id, workerId, "invalid_job", cancellationToken);
                }
                else if (textEditJob)
                    await queue.FailAsync(lease.Id, workerId, "text_edit_invalid_job", cancellationToken);
                else
                    await queue.RescheduleAsync(lease.Id, workerId, "invalid_job", cancellationToken);
                return true;
            }

            try
            {
                if (textEditJob)
                {
                    await scope.ServiceProvider.GetRequiredService<ITextEditProcessor>()
                        .RunAsync(uploadId, workCancellation.Token);
                }
                else if (repairJob)
                {
                    await scope.ServiceProvider.GetRequiredService<PageRepairProcessor>()
                        .RunAsync(uploadId, workCancellation.Token);
                }
                else if (ocrJob)
                {
                    await scope.ServiceProvider.GetRequiredService<OcrProcessor>()
                        .RunAsync(uploadId, lease.AttemptCount, workCancellation.Token);
                }
                else if (exportJob)
                {
                    await scope.ServiceProvider.GetRequiredService<DocumentPdfBuilder>()
                        .BuildAsync(uploadId, workCancellation.Token);
                }
                else if (cropJob)
                {
                    await scope.ServiceProvider.GetRequiredService<CropProcessor>()
                        .RunAsync(uploadId, revision, lease.Type is "DetectDocumentEdges" or "AutoEnhanceDocument",
                            workCancellation.Token, automaticEnhancement);
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
            catch (OcrProviderException failure) when (ocrJob)
            {
                var ocrOptions = scope.ServiceProvider.GetRequiredService<IOptions<OcrOptions>>().Value;
                if (failure.Retryable && lease.AttemptCount < ocrOptions.MaxAttempts)
                {
                    await RescheduleOcrJobAsync(
                        lease.Id, workerId, failure.SafeCode, cancellationToken);
                }
                else
                {
                    await FailOcrJobAsync(lease.Id, workerId, uploadId,
                        failure.SafeCode, failure.Retryable, cancellationToken);
                }
                logger.LogWarning(
                    "OCR job did not complete. JobId={JobId} ResultId={ResultId} ErrorCode={ErrorCode}",
                    lease.Id, uploadId, failure.SafeCode);
            }
            catch (TextEditProcessingException failure) when (textEditJob)
            {
                await ReconcileTextEditJobAsync(lease.Id, workerId, uploadId,
                    failure.SafeCode, failure.Retryable, lease.AttemptCount,
                    cancellationToken);
                logger.LogWarning("Text edit job failed safely. JobId={JobId} EditId={EditId} ErrorCode={ErrorCode}",
                    lease.Id, uploadId, failure.SafeCode);
            }
            catch (InvalidDataException) when (repairJob)
            {
                await ReconcileRepairJobAsync(lease.Id, workerId, uploadId,
                    terminal: true, cancellationToken);
                logger.LogWarning("Repair preview rejected. JobId={JobId} OperationId={OperationId}",
                    lease.Id, uploadId);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    "Upload validation lease lost. JobId={JobId} UploadId={UploadId} ErrorCode={ErrorCode}",
                    lease.Id,
                    uploadId,
                    "lease_lost");
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                if (automaticEnhancement)
                {
                    await using var recovery = scopeFactory.CreateAsyncScope();
                    await recovery.ServiceProvider.GetRequiredService<CropProcessor>()
                        .RestoreOriginalAfterAutomaticFailureAsync(uploadId, revision, cancellationToken);
                    await queue.FailAsync(lease.Id, workerId, "auto_enhancement_failed", cancellationToken);
                    logger.LogWarning(exception, "Automatic enhancement could not finish; original page remains Ready. PageId={PageId}", uploadId);
                    return true;
                }
                if (repairJob)
                {
                    await ReconcileRepairJobAsync(lease.Id, workerId, uploadId,
                        terminal: lease.AttemptCount >= PostgresJobQueue.DefaultMaxAttempts,
                        cancellationToken);
                    logger.LogWarning(exception,
                        "Repair preview job failed safely. JobId={JobId} OperationId={OperationId}",
                        lease.Id, uploadId);
                    return true;
                }
                if (textEditJob)
                {
                    await ReconcileTextEditJobAsync(lease.Id, workerId, uploadId,
                        "text_edit_failed", true, lease.AttemptCount, cancellationToken);
                    logger.LogWarning("Text edit job failed safely. JobId={JobId} EditId={EditId} ErrorCode={ErrorCode}",
                        lease.Id, uploadId, "text_edit_failed");
                    return true;
                }
                if (ocrJob)
                {
                    var ocrOptions = scope.ServiceProvider.GetRequiredService<IOptions<OcrOptions>>().Value;
                    const string safeCode = "ocr_failed";
                    if (lease.AttemptCount < ocrOptions.MaxAttempts)
                        await RescheduleOcrJobAsync(
                            lease.Id, workerId, safeCode, cancellationToken);
                    else
                    {
                        await FailOcrJobAsync(lease.Id, workerId, uploadId,
                            safeCode, true, cancellationToken);
                    }
                    logger.LogWarning(
                        "OCR job failed safely. JobId={JobId} ResultId={ResultId} ErrorCode={ErrorCode}",
                        lease.Id, uploadId, safeCode);
                    return true;
                }
                if (cropJob && lease.AttemptCount >= 6)
                    await scope.ServiceProvider.GetRequiredService<CropProcessor>().FailAsync(uploadId, revision, cancellationToken);
                if (importJob && lease.AttemptCount >= scope.ServiceProvider.GetRequiredService<IOptions<DocumentImportOptions>>().Value.MaxAttempts)
                    await scope.ServiceProvider.GetRequiredService<DocumentImportProcessor>().FailAsync(uploadId, cancellationToken);
                var errorCode = exportJob ? "export_build_failed" : cropJob ? "crop_failed" : importJob ? "import_failed" : "validation_failed";
                if (exportJob)
                {
                    // A failed transaction can leave stale Ready mutations in its tracker or a
                    // broken connection. Never reschedule through that build's database scope.
                    await using var recoveryScope = scopeFactory.CreateAsyncScope();
                    if (lease.AttemptCount >= PostgresJobQueue.DefaultMaxAttempts)
                        await recoveryScope.ServiceProvider.GetRequiredService<DocumentPdfBuilder>()
                            .FailAsync(uploadId, cancellationToken);
                    // If reconciliation is unavailable, leave the lease reclaimable instead of
                    // terminalizing its job while the export is still pending.
                    await recoveryScope.ServiceProvider.GetRequiredService<IProcessingJobQueue>()
                        .RescheduleAsync(lease.Id, workerId, errorCode, cancellationToken);
                }
                else
                {
                    await queue.RescheduleAsync(lease.Id, workerId, errorCode, cancellationToken);
                }
                logger.LogWarning(
                    exception,
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

    private async Task ReconcileTextEditJobAsync(Guid jobId, string workerId,
        Guid editId, string safeCode, bool retryable, int attemptCount,
        CancellationToken ct)
    {
        await using var recoveryScope = scopeFactory.CreateAsyncScope();
        var services = recoveryScope.ServiceProvider;
        var queue = services.GetRequiredService<IProcessingJobQueue>();
        var options = services.GetRequiredService<IOptions<TextEditingOptions>>().Value;
        if (retryable && attemptCount < options.MaxAttempts)
        {
            await queue.RescheduleAsync(jobId, workerId, safeCode, ct);
            return;
        }
        var db = services.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await services.GetRequiredService<ITextEditProcessor>()
            .FailAsync(editId, safeCode, ct);
        await queue.FailAsync(jobId, workerId, safeCode, ct);
        await transaction.CommitAsync(ct);
    }

    private async Task ReconcileRepairJobAsync(Guid jobId, string workerId,
        Guid operationId, bool terminal, CancellationToken ct)
    {
        await using var recoveryScope = scopeFactory.CreateAsyncScope();
        var services = recoveryScope.ServiceProvider;
        var queue = services.GetRequiredService<IProcessingJobQueue>();
        if (!terminal)
        {
            await queue.RescheduleAsync(jobId, workerId, "repair_failed", ct);
            return;
        }
        var db = services.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await services.GetRequiredService<PageRepairProcessor>().FailAsync(operationId, ct);
        await queue.FailAsync(jobId, workerId, "repair_failed", ct);
        await transaction.CommitAsync(ct);
    }

    private async Task FailOcrJobAsync(
        Guid jobId,
        string workerId,
        Guid resultId,
        string safeCode,
        bool retryable,
        CancellationToken cancellationToken)
    {
        // Processing may have left a failed or stale EF tracker. Reconcile the OCR result and
        // its queue job in a fresh scope and one transaction so they cannot diverge on a crash.
        await using var recoveryScope = scopeFactory.CreateAsyncScope();
        var db = recoveryScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await recoveryScope.ServiceProvider.GetRequiredService<OcrProcessor>()
            .FailAsync(resultId, safeCode, retryable, cancellationToken);
        await recoveryScope.ServiceProvider.GetRequiredService<IProcessingJobQueue>()
            .FailAsync(jobId, workerId, safeCode, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task RescheduleOcrJobAsync(
        Guid jobId,
        string workerId,
        string safeCode,
        CancellationToken cancellationToken)
    {
        await using var recoveryScope = scopeFactory.CreateAsyncScope();
        await recoveryScope.ServiceProvider.GetRequiredService<IProcessingJobQueue>()
            .RescheduleAsync(jobId, workerId, safeCode, cancellationToken);
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
