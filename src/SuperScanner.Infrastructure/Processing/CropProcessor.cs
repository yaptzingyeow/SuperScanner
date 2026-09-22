using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Processing;
using SuperScanner.Application.Abstractions;
using SuperScanner.Infrastructure.Persistence;
using SuperScanner.Infrastructure.Ocr;

namespace SuperScanner.Infrastructure.Processing;

public sealed class CropProcessor(
    AppDbContext db,
    IObjectStore store,
    IConfiguration configuration,
    IOptions<DocumentBoundaryOptions> boundaryOptions,
    DocumentBoundaryHealth boundaryHealth,
    ILogger<CropProcessor> logger,
    OcrJobScheduler? ocrScheduler = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task EnsureDetectionForPageAsync(Guid pageId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var page = (await CropDocumentStatus.LockWorkerPageAsync(db, pageId, ct)).Page;
        if (page.CropSourceObjectKey is null && page.PreviewObjectKey is not null)
        {
            page.InitializeCrop();
            Queue(db, page, true);
            await db.SaveChangesAsync(ct);
            await CropDocumentStatus.RefreshAsync(db, page.DocumentId, ct);
        }
        await transaction.CommitAsync(ct);
    }

    public static void Queue(AppDbContext db, Page page, bool detect) =>
        db.ProcessingJobs.Add(ProcessingJob.Create(Guid.NewGuid(),
            detect ? "DetectDocumentEdges" : "ApplyPerspectiveCrop", $"{page.Id}:{page.CropRevision}",
            $"page:{page.Id}:crop:{page.CropRevision}", DateTimeOffset.UtcNow));

    public async Task RunAsync(Guid pageId, int revision, bool detect, CancellationToken ct)
    {
        var elapsed = Stopwatch.StartNew();
        var page = await db.Pages.AsNoTracking().SingleAsync(x => x.Id == pageId, ct);
        var expectedStatus = detect ? "Detecting" : "Processing";
        if (page.CropRevision != revision || page.CropStatus != expectedStatus) return;
        if (page.CropSourceObjectKey is null) throw new InvalidOperationException("Crop source unavailable.");
        var directory = Directory.CreateTempSubdirectory("superscanner-crop-");
        try
        {
            var input = Path.Combine(directory.FullName, "source.jpg");
            await using (var source = await store.OpenReadAsync(page.CropSourceObjectKey, ct))
            await using (var target = File.Create(input))
            {
                var buffer = new byte[81920]; long total = 0; int count;
                while ((count = await source.ReadAsync(buffer, ct)) > 0)
                {
                    total += count;
                    if (total > 25 * 1024 * 1024) throw new InvalidDataException("Source too large.");
                    await target.WriteAsync(buffer.AsMemory(0, count), ct);
                }
            }
            var preview = Path.Combine(directory.FullName, "preview.jpg");
            var thumbnail = Path.Combine(directory.FullName, "thumbnail.jpg");
            var start = new ProcessStartInfo(configuration["Crop:PythonPath"] ?? "python3")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "processing", "crop_image.py"));
            start.ArgumentList.Add(detect ? "detect" : "apply");
            start.ArgumentList.Add(input);
            if (detect)
            {
                var options = boundaryOptions.Value;
                var mode = options.Mode;
                if (mode == "AiPreferred" && (!boundaryHealth.CanAttemptAi
                    || !DocumentBoundaryRollout.ShouldUseAi(pageId, options.RolloutPercentage)))
                {
                    mode = "OpenCvOnly";
                }
                var metadataPath = Path.IsPathRooted(options.ModelMetadataPath)
                    ? options.ModelMetadataPath
                    : Path.Combine(AppContext.BaseDirectory, options.ModelMetadataPath);
                start.Environment["SUPERSCANNER_BOUNDARY_MODE"] = mode;
                start.Environment["SUPERSCANNER_BOUNDARY_MODEL_METADATA"] = metadataPath;
                start.Environment["SUPERSCANNER_BOUNDARY_MASK_THRESHOLD"] = options.MaskThreshold.ToString(CultureInfo.InvariantCulture);
                start.Environment["SUPERSCANNER_BOUNDARY_HIGH_CONFIDENCE"] = options.HighConfidence.ToString(CultureInfo.InvariantCulture);
                start.Environment["SUPERSCANNER_BOUNDARY_MEDIUM_CONFIDENCE"] = options.MediumConfidence.ToString(CultureInfo.InvariantCulture);
            }
            if (!detect)
            {
                var points = JsonSerializer.Deserialize<CropPoint[]>(page.CropPointsJson ?? "null", Json);
                if (!CropGeometry.IsValid(points)) throw new InvalidDataException("Invalid crop geometry.");
                start.ArgumentList.Add(page.CropPointsJson!);
                start.ArgumentList.Add(preview);
                start.ArgumentList.Add(thumbnail);
                if (!ScanFilter.IsValid(page.Filter)) throw new InvalidDataException("Invalid scan filter.");
                start.ArgumentList.Add(page.Filter);
            }
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Crop runtime unavailable.");
            var output = process.StandardOutput.ReadToEndAsync(ct);
            var errors = process.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(
                detect ? boundaryOptions.Value.InferenceTimeoutSeconds : 25));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(true);
                await process.WaitForExitAsync(CancellationToken.None);
                if (ct.IsCancellationRequested) throw;
                throw new TimeoutException("Crop processing timed out.");
            }
            await errors;
            var result = await output;
            if (process.ExitCode != 0 || result.Length > 8192) throw new InvalidDataException("Crop processing failed.");
            if (detect)
            {
                var detection = JsonSerializer.Deserialize<CropDetectionResult>(result, Json)
                    ?? throw new InvalidDataException();
                if (!detection.IsValid()) throw new InvalidDataException("Invalid detection result.");
                if (detection.DiagnosticsCode.StartsWith("ai_checksum_invalid", StringComparison.Ordinal)
                    || detection.DiagnosticsCode.StartsWith("ai_model_unsupported", StringComparison.Ordinal)
                    || detection.DiagnosticsCode.StartsWith("ai_model_invalid", StringComparison.Ordinal))
                {
                    boundaryHealth.MarkUnhealthy(detection.DiagnosticsCode);
                }
                await CompleteDetectionAsync(pageId, revision, detection, ct);
                logger.LogInformation(
                    "Document boundary completed for page {PageId} revision {Revision}: source {Source}, confidence {Confidence}, model {ModelVersion}, diagnostics {DiagnosticsCode}, elapsed {ElapsedMilliseconds}ms",
                    pageId, revision, detection.Source, detection.Confidence, detection.ModelVersion,
                    detection.DiagnosticsCode, elapsed.ElapsedMilliseconds);
            }
            else
            {
                var previewKey = $"previews/{page.DocumentId:N}/{page.Id:N}/crop-{revision}.jpg";
                var thumbnailKey = $"thumbnails/{page.DocumentId:N}/{page.Id:N}/crop-{revision}.jpg";
                foreach (var (file, key) in new[] { (preview, previewKey), (thumbnail, thumbnailKey) })
                {
                    if (new FileInfo(file).Length > 12 * 1024 * 1024) throw new InvalidDataException("Output too large.");
                    await using var stream = File.OpenRead(file);
                    await store.WriteAsync(key, "image/jpeg", stream, ct);
                }
                await CompletePerspectiveCropAsync(pageId, revision, previewKey, thumbnailKey, ct);
            }
            await CropDocumentStatus.RefreshAsync(db, page.DocumentId, ct);
        }
        finally { directory.Delete(true); }
    }

    public async Task<bool> CompletePerspectiveCropAsync(
        Guid pageId,
        int revision,
        string previewKey,
        string thumbnailKey,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var locked = await CropDocumentStatus.LockWorkerPageAsync(db, pageId, ct);
        var updated = await db.Pages.Where(x => x.Id == pageId && x.CropRevision == revision && x.CropStatus == "Processing")
            .ExecuteUpdateAsync(set => set
                .SetProperty(x => x.PreviewObjectKey, previewKey)
                .SetProperty(x => x.ThumbnailObjectKey, thumbnailKey)
                .SetProperty(x => x.AppliedCropRevision, revision)
                .SetProperty(x => x.AppliedFilter, locked.Page.Filter)
                .SetProperty(x => x.CropStatus, "Ready")
                .SetProperty(x => x.State, PageState.Ready)
                .SetProperty(x => x.FailureCode, (string?)null), ct);
        if (updated == 1)
        {
            locked.Document.MarkContentChanged(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
            if (ocrScheduler is not null)
                await ocrScheduler.EnsureQueuedAsync(pageId, previewKey, "image/jpeg", ct);
        }
        await transaction.CommitAsync(ct);
        return updated == 1;
    }

    public async Task CompleteDetectionAsync(
        Guid pageId,
        int revision,
        CropDetectionResult detection,
        CancellationToken ct)
    {
        var pointsJson = JsonSerializer.Serialize(detection.Points, Json);
        var documentId = await db.Pages.Where(x => x.Id == pageId).Select(x => x.DocumentId).SingleAsync(ct);
        await db.Pages.Where(x => x.Id == pageId && x.CropRevision == revision && x.CropStatus == "Detecting")
            .ExecuteUpdateAsync(set => set
                .SetProperty(x => x.CropPointsJson, pointsJson)
                .SetProperty(x => x.CropConfidence, detection.Confidence)
                .SetProperty(x => x.CropSource, detection.Source)
                .SetProperty(x => x.CropModelVersion, detection.ModelVersion)
                .SetProperty(x => x.CropDiagnosticsCode, detection.DiagnosticsCode)
                .SetProperty(x => x.CropStatus, "NeedsCrop")
                .SetProperty(x => x.State, PageState.NeedsCrop)
                .SetProperty(x => x.FailureCode, (string?)null), ct);
        await CropDocumentStatus.RefreshAsync(db, documentId, ct);
    }

    public async Task FailAsync(Guid pageId, int revision, CancellationToken ct)
    {
        await db.Pages.Where(p => p.Id == pageId && p.CropRevision == revision &&
            (p.CropStatus == "Detecting" || p.CropStatus == "Processing"))
            .ExecuteUpdateAsync(set => set
                .SetProperty(p => p.CropStatus, "Failed")
                .SetProperty(p => p.State, PageState.Failed)
                .SetProperty(p => p.FailureCode, "crop_failed"), ct);
        var documentId = await db.Pages.Where(p => p.Id == pageId).Select(p => p.DocumentId).SingleAsync(ct);
        await CropDocumentStatus.RefreshAsync(db, documentId, ct);
    }
}
