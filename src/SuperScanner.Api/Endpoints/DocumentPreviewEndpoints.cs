using Microsoft.EntityFrameworkCore;
using SuperScanner.Api.Auth;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Processing;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Uploads;
using SuperScanner.Infrastructure.Persistence;
using SuperScanner.Infrastructure.Processing;

namespace SuperScanner.Api.Endpoints;

public static class DocumentPreviewEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/documents").RequireAuthorization();
        group.MapGet("/{id:guid}", async (Guid id, ICurrentUser user, AppDbContext db, CancellationToken ct) =>
        {
            var document = await db.Documents.AsNoTracking().Include(x => x.Pages)
                .SingleOrDefaultAsync(x => x.Id == id && x.OwnerFirebaseUid == user.FirebaseUid, ct);
            if (document is null) return Results.NotFound();
            var imports = await db.UploadIntents.AsNoTracking().Where(x => x.DocumentId == id)
                .OrderBy(x => x.AcceptedAt).ThenBy(x => x.Id).ToListAsync(ct);
            var latestExport = await db.DocumentExports.AsNoTracking()
                .Where(x => x.DocumentId == id && x.OwnerFirebaseUid == user.FirebaseUid)
                .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            return Results.Ok(CreateDetail(document, imports, latestExport));
        });
        group.MapGet("/{id:guid}/pages/{pageId:guid}/{asset}", async (
            Guid id, Guid pageId, string asset, ICurrentUser user, AppDbContext db,
            IObjectStore store, HttpContext context, CancellationToken ct) =>
        {
            var document = await db.Documents.AsNoTracking().Include(x => x.Pages)
                .SingleOrDefaultAsync(x => x.Id == id && x.OwnerFirebaseUid == user.FirebaseUid, ct);
            var page = document?.ActivePages.SingleOrDefault(x => x.Id == pageId);
            var key = asset switch {
                "preview" => page?.PreviewObjectKey, "thumbnail" => page?.ThumbnailObjectKey,
                "original" => page?.OriginalObjectKey, "crop-source" => page?.CropSourceObjectKey, _ => null
            };
            if (key is null) return Results.NotFound();
            var mediaType = asset == "original" ? page!.OriginalMediaType : "image/jpeg";
            var extension = mediaType switch { "application/pdf" => ".pdf", "image/png" => ".png", "image/jpeg" => ".jpg", "image/heic" => ".heic", _ => ".bin" };
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            var stream = await store.OpenReadAsync(key, ct);
            return Results.Stream(stream, mediaType ?? "application/octet-stream",
                fileDownloadName: asset == "original" ? "original" + extension : null);
        });
        group.MapPost("/{id:guid}/retry-preview", async (Guid id, ICurrentUser user, AppDbContext db, CancellationToken ct) =>
        {
            if (!await db.Documents.AnyAsync(x => x.Id == id && x.OwnerFirebaseUid == user.FirebaseUid, ct))
                return Results.NotFound();
            var ids = await db.UploadIntents.Where(x => x.DocumentId == id).Select(x => x.Id.ToString()).ToListAsync(ct);
            var jobs = await db.ProcessingJobs.Where(x => (x.Type == "ExpandDocumentImport" || x.Type == "ProcessDocument") && ids.Contains(x.Payload)
                && x.Status == ProcessingJobStatus.Failed).ToListAsync(ct);
            foreach (var job in jobs) job.Retry(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
            return Results.Accepted();
        });
    }

    public static object CreateDetail(Document document, IReadOnlyList<UploadIntent> imports, DocumentExport? latestExport) => new
    {
        document.Id, document.Title,
        status = CropDocumentStatus.Compute(document).ToString(),
        document.Revision, document.PageOrderRevision,
        pages = document.ActivePages.Select(page => new
        {
            page.Id, page.Position, pageNumber = page.Position,
            page.SourceUploadId, page.SourcePageIndex,
            state = page.State.ToString(), failureCode = SafeCode(page.FailureCode, "page_failed"),
            hasPreview = page.PreviewObjectKey != null, hasOriginal = page.OriginalObjectKey != null,
            canCrop = page.CropSourceObjectKey != null, cropStatus = page.CropStatus,
            page.CropRevision, page.AppliedCropRevision, previewRevision = page.AppliedCropRevision,
            page.Filter, page.AppliedFilter
        }).ToArray(),
        imports = imports.Select(upload => new
        {
            uploadId = upload.Id, fileName = upload.OriginalFileName, mediaType = upload.DeclaredMediaType,
            state = upload.ExpansionErrorCode != null ? "Failed" : upload.State == UploadIntentState.Accepted &&
                upload.DiscoveredPageCount > 0 && upload.CreatedPageCount + upload.FailedPageCount == upload.DiscoveredPageCount
                    ? "Completed" : upload.State.ToString(),
            upload.DiscoveredPageCount, upload.CreatedPageCount, upload.FailedPageCount,
            errorCode = SafeCode(upload.ExpansionErrorCode ?? upload.ValidationErrorCode, "import_failed")
        }).ToArray(),
        latestExport = latestExport is null ? null : new
        {
            latestExport.Id, state = latestExport.State.ToString(), latestExport.DocumentRevision,
            latestExport.ReadyPageCount, latestExport.ExcludedPageCount,
            failureCode = SafeCode(latestExport.FailureCode, "export_build_failed"),
            latestExport.CreatedAt, latestExport.CompletedAt, latestExport.ExpiresAt,
            isOutdated = latestExport.DocumentRevision != document.Revision
        }
    };

    private static string? SafeCode(string? code, string fallback) => code switch
    {
        null => null,
        "pdf_encrypted" or "pdf_invalid" or "pdf_timeout" or "pdf_tool_unavailable" or "pdf_render_failed" or
        "image_invalid" or "import_page_limit" or "document_page_limit" or "import_pixel_limit" or
        "import_size_limit" or "render_size_limit" or "import_failed" or "crop_failed" or "page_failed" or
        "export_asset_missing" or "export_decode_failed" or "export_size_limit" or "export_build_failed" or
        "upload_expired" or "size_mismatch" or "size_limit" or "signature_mismatch" or "hash_mismatch" or
        "malware_detected" or "scanner_unavailable" => code,
        _ => fallback
    };
}
