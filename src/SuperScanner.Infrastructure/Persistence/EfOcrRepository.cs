using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Ocr;

namespace SuperScanner.Infrastructure.Persistence;

public sealed class EfOcrRepository(AppDbContext db) : IOcrRepository
{
    public async Task<IOcrTransaction> BeginTransactionAsync(CancellationToken ct) =>
        new OcrTransaction(await db.Database.BeginTransactionAsync(ct));

    public async Task<OcrPageSource?> FindOwnedSourceAsync(
        string ownerUid,
        Guid documentId,
        Guid pageId,
        bool forUpdate,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUid);
        if (forUpdate && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("An OCR request requires a transaction.");

        Page? page;
        if (forUpdate)
        {
            page = await db.Pages
                .FromSqlInterpolated($"""
                    SELECT p.*
                    FROM pages AS p
                    INNER JOIN documents AS d ON d."Id" = p."DocumentId"
                    WHERE p."Id" = {pageId}
                      AND p."DocumentId" = {documentId}
                      AND d."OwnerFirebaseUid" = {ownerUid}
                      AND p."RemovedAt" IS NULL
                    FOR UPDATE OF p
                    """)
                .SingleOrDefaultAsync(ct);
        }
        else
        {
            page = await db.Pages.AsNoTracking()
                .Where(candidate => candidate.Id == pageId &&
                    candidate.DocumentId == documentId &&
                    candidate.RemovedAt == null)
                .Where(_ => db.Documents.Any(document =>
                    document.Id == documentId && document.OwnerFirebaseUid == ownerUid))
                .SingleOrDefaultAsync(ct);
        }

        return page is null
            ? null
            : new OcrPageSource(page.Id, page.State, page.PreviewObjectKey, "image/jpeg");
    }

    public async Task<PageOcrResult?> FindBySourceAsync(
        Guid pageId,
        string sourceFingerprint,
        bool forUpdate,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFingerprint);
        if (!forUpdate)
        {
            return await db.PageOcrResults
                .Include(result => result.Elements)
                .SingleOrDefaultAsync(result =>
                    result.PageId == pageId && result.SourceFingerprint == sourceFingerprint, ct);
        }

        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("An OCR mutation requires a transaction.");

        return await db.PageOcrResults
            .FromSqlInterpolated($"""
                SELECT *
                FROM page_ocr_results
                WHERE "PageId" = {pageId} AND "SourceFingerprint" = {sourceFingerprint}
                FOR UPDATE
                """)
            .Include(result => result.Elements)
            .SingleOrDefaultAsync(ct);
    }

    public Task AddAsync(PageOcrResult result, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(result);
        return db.PageOcrResults.AddAsync(result, ct).AsTask();
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    private sealed class OcrTransaction(IDbContextTransaction transaction) : IOcrTransaction
    {
        public Task CommitAsync(CancellationToken ct) => transaction.CommitAsync(ct);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
