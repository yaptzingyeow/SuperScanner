using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ArksScanner.Application.Abstractions;
using ArksScanner.Infrastructure.Persistence;

namespace ArksScanner.Infrastructure.Processing;

/// <summary>
/// Permanently erases documents that were removed (deleted by the owner, by retention or with the
/// account) more than <c>purgeAfterDays</c> ago: their stored files first, then every database row.
/// Audit events are kept. Safe to re-run: a failed document is simply retried on the next run.
/// </summary>
public sealed class DocumentPurge(AppDbContext db, IObjectStore store, IClock clock,
    ILogger<DocumentPurge> logger, int purgeAfterDays = DocumentPurge.DefaultPurgeAfterDays)
{
    public const int DefaultPurgeAfterDays = 30;
    private const int BatchSize = 50;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var cutoff = clock.UtcNow.AddDays(-purgeAfterDays);
        var due = await db.Documents.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.RemovedAt != null && d.RemovedAt <= cutoff)
            .OrderBy(d => d.RemovedAt).Select(d => d.Id).Take(BatchSize).ToListAsync(ct);
        var purged = 0;
        foreach (var id in due)
        {
            try
            {
                await PurgeAsync(id, ct);
                purged++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Document purge failed. DocumentId={DocumentId}", id);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
        return purged;
    }

    private async Task PurgeAsync(Guid id, CancellationToken ct)
    {
        foreach (var key in await ObjectKeysAsync(id, ct))
            await store.DeleteAsync(key, ct);

        // Revisions and text edits point at each other (and OCR elements at themselves) with
        // RESTRICT keys, so break those links before the cascading delete of the document.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE pages SET "ActiveRevisionId" = NULL WHERE "DocumentId" = {id};
            UPDATE page_revisions SET "ParentRevisionId" = NULL, "ProducingTextEditId" = NULL
              WHERE "PageId" IN (SELECT "Id" FROM pages WHERE "DocumentId" = {id});
            UPDATE text_edit_operations SET "BranchParentEditId" = NULL, "ResultRevisionId" = NULL
              WHERE "DocumentId" = {id};
            DELETE FROM text_edit_operations WHERE "DocumentId" = {id};
            DELETE FROM page_revisions WHERE "PageId" IN (SELECT "Id" FROM pages WHERE "DocumentId" = {id});
            UPDATE ocr_elements SET "ParentElementId" = NULL WHERE "PageOcrResultId" IN
              (SELECT r."Id" FROM page_ocr_results r JOIN pages p ON p."Id" = r."PageId" WHERE p."DocumentId" = {id});
            DELETE FROM upload_intents WHERE "DocumentId" = {id};
            DELETE FROM documents WHERE "Id" = {id};
            """, ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<IReadOnlyCollection<string>> ObjectKeysAsync(Guid id, CancellationToken ct)
    {
        var pages = db.Pages.IgnoreQueryFilters().Where(p => p.DocumentId == id);
        var pageIds = pages.Select(p => p.Id);
        var keys = new List<string?>();
        foreach (var page in await pages.Select(p => new
                     { p.OriginalObjectKey, p.PreviewObjectKey, p.ThumbnailObjectKey, p.CropSourceObjectKey })
                     .ToListAsync(ct))
            keys.AddRange([page.OriginalObjectKey, page.PreviewObjectKey, page.ThumbnailObjectKey, page.CropSourceObjectKey]);
        keys.AddRange(await db.PageRevisions.Where(r => pageIds.Contains(r.PageId)).Select(r => r.ObjectKey).ToListAsync(ct));
        keys.AddRange(await db.PageRepairOperations.Where(r => pageIds.Contains(r.PageId))
            .Select(r => r.PreviewObjectKey).ToListAsync(ct));
        keys.AddRange(await db.DocumentExports.Where(e => e.DocumentId == id).Select(e => e.OutputObjectKey).ToListAsync(ct));
        foreach (var upload in await db.UploadIntents.Where(u => u.DocumentId == id)
                     .Select(u => new { u.QuarantineObjectKey, u.AcceptedObjectKey }).ToListAsync(ct))
            keys.AddRange([upload.QuarantineObjectKey, upload.AcceptedObjectKey]);
        return keys.Where(key => !string.IsNullOrWhiteSpace(key)).Select(key => key!).Distinct(StringComparer.Ordinal).ToList();
    }
}
