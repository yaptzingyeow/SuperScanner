using Microsoft.EntityFrameworkCore;
using SuperScanner.Application.TextEditing;
using SuperScanner.Domain.Documents;
using SuperScanner.Infrastructure.Persistence;

namespace SuperScanner.Infrastructure.TextEditing;

public sealed class EfTextEditRepository(AppDbContext db) : ITextSelectionRepository
{
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
}
