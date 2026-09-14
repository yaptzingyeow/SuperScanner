using Microsoft.EntityFrameworkCore;
using SuperScanner.Api.Auth;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Processing;
using SuperScanner.Infrastructure.Persistence;

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
            var uploadIds = await db.UploadIntents.Where(x => x.DocumentId == id)
                .Select(x => x.Id.ToString()).ToListAsync(ct);
            var jobs = await db.ProcessingJobs.AsNoTracking()
                .Where(x => x.Type == "ProcessDocument" && uploadIds.Contains(x.Payload)).ToListAsync(ct);
            var failed = jobs.Any(x => x.Status == ProcessingJobStatus.Failed);
            return Results.Ok(new {
                document.Id, document.Title,
                status = failed ? "Failed" : document.Status.ToString(),
                message = failed ? "We could not create a preview. Please retry." :
                    jobs.Any(x => x.ErrorCode != null) ? "Preview generation is taking longer than expected. Retrying automatically." : null,
                pages = document.Pages.OrderBy(x => x.PageNumber).Select(x => new {
                    x.Id, x.PageNumber, hasPreview = x.PreviewObjectKey != null,
                    hasOriginal = x.OriginalObjectKey != null,
                    canCrop = x.CropSourceObjectKey != null, cropStatus = x.CropStatus,
                    previewRevision = x.AppliedCropRevision, appliedFilter = x.AppliedFilter
                })
            });
        });
        group.MapGet("/{id:guid}/pages/{pageId:guid}/{asset}", async (
            Guid id, Guid pageId, string asset, ICurrentUser user, AppDbContext db,
            IObjectStore store, HttpContext context, CancellationToken ct) =>
        {
            var document = await db.Documents.AsNoTracking().Include(x => x.Pages)
                .SingleOrDefaultAsync(x => x.Id == id && x.OwnerFirebaseUid == user.FirebaseUid, ct);
            var page = document?.Pages.SingleOrDefault(x => x.Id == pageId);
            var key = asset switch {
                "preview" => page?.PreviewObjectKey, "thumbnail" => page?.ThumbnailObjectKey,
                "original" => page?.OriginalObjectKey, "crop-source" => page?.CropSourceObjectKey, _ => null
            };
            if (key is null) return Results.NotFound();
            var mediaType = asset == "original" ? await db.UploadIntents
                .Where(x => x.PageId == pageId && x.State == SuperScanner.Domain.Uploads.UploadIntentState.Accepted)
                .Select(x => x.DeclaredMediaType).FirstOrDefaultAsync(ct) : "image/jpeg";
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
            var jobs = await db.ProcessingJobs.Where(x => x.Type == "ProcessDocument" && ids.Contains(x.Payload)
                && x.Status == ProcessingJobStatus.Failed).ToListAsync(ct);
            foreach (var job in jobs) job.Retry(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
            return Results.Accepted();
        });
    }
}
