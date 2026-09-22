using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using SuperScanner.Domain.Documents;
using SuperScanner.Infrastructure.Persistence;

namespace SuperScanner.Infrastructure.Processing;

public static class CropDocumentStatus
{
    public sealed record LockedDocumentPage(Document Document, Page Page);

    public static readonly Expression<Func<Document, DocumentStatus>> Summary = d =>
        d.Pages.Any(p => p.RemovedAt == null && (p.State == PageState.Importing || p.State == PageState.Processing)) ? DocumentStatus.Processing :
        d.Pages.Any(p => p.RemovedAt == null && p.State == PageState.NeedsCrop) ? DocumentStatus.NeedsCrop :
        d.Pages.Any(p => p.RemovedAt == null && p.State == PageState.Ready) ? DocumentStatus.Ready :
        d.Pages.Any(p => p.RemovedAt == null) ? DocumentStatus.Failed : DocumentStatus.Draft;

    public static DocumentStatus Compute(Document document) => ComputeSummary(document);
    private static readonly Func<Document, DocumentStatus> ComputeSummary = Summary.Compile();

    public static async Task<Page?> LockSubmissionPageAsync(
        AppDbContext db,
        Guid documentId,
        Guid pageId,
        CancellationToken ct)
    {
        var locked = await LockDocumentAndPageAsync(db, documentId, pageId, ct);
        return locked?.Page;
    }

    public static async Task<LockedDocumentPage> LockWorkerPageAsync(
        AppDbContext db,
        Guid pageId,
        CancellationToken ct)
    {
        var documentId = await db.Pages.AsNoTracking()
            .Where(page => page.Id == pageId)
            .Select(page => page.DocumentId)
            .SingleAsync(ct);
        return await LockDocumentAndPageAsync(db, documentId, pageId, ct)
            ?? throw new InvalidOperationException("The page was removed while acquiring crop locks.");
    }

    private static async Task<LockedDocumentPage?> LockDocumentAndPageAsync(
        AppDbContext db,
        Guid documentId,
        Guid pageId,
        CancellationToken ct)
    {
        var document = await db.Documents.FromSqlInterpolated(
                $"SELECT * FROM documents WHERE \"Id\" = {documentId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (document is null) return null;

        var page = await db.Pages.FromSqlInterpolated(
                $"SELECT * FROM pages WHERE \"Id\" = {pageId} AND \"DocumentId\" = {documentId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        return page is null ? null : new LockedDocumentPage(document, page);
    }

    public static Task RefreshAsync(AppDbContext db, Guid documentId, CancellationToken ct) =>
        db.Documents.Where(d => d.Id == documentId).ExecuteUpdateAsync(set => set
            .SetProperty(d => d.Status, Summary)
            .SetProperty(d => d.UpdatedAt, DateTimeOffset.UtcNow), ct);
}
