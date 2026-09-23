using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SuperScanner.Application.TextEditing;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Ocr;
using SuperScanner.Domain.TextEditing;
using SuperScanner.Infrastructure.Persistence;

namespace SuperScanner.Infrastructure.TextEditing;

public sealed class EfTextEditRepository(AppDbContext db) : ITextSelectionRepository,
    ITextEditCommandRepository, ITextEditReadRepository
{
    public Task<bool> IsOwnedPageAsync(string ownerUid, Guid documentId,
        Guid pageId, CancellationToken ct) =>
        db.Pages.AsNoTracking().AnyAsync(page => page.Id == pageId &&
            page.DocumentId == documentId && page.RemovedAt == null &&
            db.Documents.Any(document => document.Id == documentId &&
                document.OwnerFirebaseUid == ownerUid), ct);

    public Task<TextEditOperation?> FindAsync(Guid pageId, Guid editId, CancellationToken ct) =>
        db.TextEditOperations.AsNoTracking()
            .SingleOrDefaultAsync(edit => edit.PageId == pageId && edit.Id == editId, ct);

    public async Task<IReadOnlyList<TextEditOperation>> ListAsync(Guid pageId, CancellationToken ct) =>
        await db.TextEditOperations.AsNoTracking()
            .Where(edit => edit.PageId == pageId)
            .OrderBy(edit => edit.Sequence)
            .ToArrayAsync(ct);

    public async Task<ITextEditTransaction> BeginAsync(CancellationToken ct) =>
        new Transaction(await db.Database.BeginTransactionAsync(ct));

    public async Task<LockedTextEditPage?> FindOwnedForUpdateAsync(string ownerUid,
        Guid documentId, Guid pageId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUid);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A text edit requires a transaction.");
        var page = await db.Pages.FromSqlInterpolated($"""
                SELECT p.* FROM pages AS p
                INNER JOIN documents AS d ON d."Id" = p."DocumentId"
                WHERE p."Id" = {pageId} AND p."DocumentId" = {documentId}
                  AND d."OwnerFirebaseUid" = {ownerUid}
                  AND p."RemovedAt" IS NULL AND p."State" = {PageState.Ready.ToString()}
                FOR UPDATE OF p
                """)
            .Include(candidate => candidate.ActiveRevision)
            .SingleOrDefaultAsync(ct);
        if (page is null) return null;
        var ocr = await db.PageOcrResults.Include(result => result.Elements)
            .Where(result => result.PageId == pageId &&
                result.SourceObjectKey == page.GetProcessedObjectKey())
            .OrderByDescending(result => result.CompletedAt)
            .FirstOrDefaultAsync(ct);
        return new LockedTextEditPage(page, ocr);
    }

    public Task<TextEditOperation?> FindByIdempotencyAsync(Guid pageId,
        string key, CancellationToken ct) =>
        db.TextEditOperations.SingleOrDefaultAsync(edit => edit.PageId == pageId &&
            edit.IdempotencyKey == key, ct);

    public Task<int> CountPendingAsync(Guid pageId, CancellationToken ct) =>
        db.TextEditOperations.CountAsync(edit => edit.PageId == pageId &&
            (edit.State == TextEditState.Queued || edit.State == TextEditState.Processing), ct);

    public async Task<long> NextSequenceAsync(Guid pageId, CancellationToken ct) =>
        (await db.TextEditOperations.Where(edit => edit.PageId == pageId)
            .MaxAsync(edit => (long?)edit.Sequence, ct) ?? 0) + 1;

    public Task AddBaseRevisionAsync(PageRevision revision, CancellationToken ct) =>
        db.PageRevisions.AddAsync(revision, ct).AsTask();

    public Task AddEditAsync(TextEditOperation edit, CancellationToken ct) =>
        db.TextEditOperations.AddAsync(edit, ct).AsTask();

    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task<OwnedTextSelection?> FindOwnedAsync(
        string ownerUid, Guid documentId, Guid pageId, Guid ocrResultId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUid);
        var page = await db.Pages.AsNoTracking()
            .Include(candidate => candidate.ActiveRevision)
            .Where(candidate => candidate.Id == pageId &&
                candidate.DocumentId == documentId &&
                candidate.RemovedAt == null &&
                candidate.State == PageState.Ready)
            .Where(_ => db.Documents.Any(document => document.Id == documentId &&
                document.OwnerFirebaseUid == ownerUid))
            .SingleOrDefaultAsync(ct);
        if (page is null) return null;

        var ocr = await db.PageOcrResults.AsNoTracking()
            .Include(result => result.Elements)
            .SingleOrDefaultAsync(result => result.Id == ocrResultId && result.PageId == pageId, ct);
        if (ocr is null) return null;

        return new OwnedTextSelection(page.ActiveRevisionId, page.GetProcessedObjectKey(),
            ocr.Id, ocr.State, ocr.SourceObjectKey, ocr.Elements.ToArray());
    }

    private sealed class Transaction(IDbContextTransaction transaction) : ITextEditTransaction
    {
        public Task CommitAsync(CancellationToken ct) => transaction.CommitAsync(ct);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
