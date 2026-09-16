using Microsoft.EntityFrameworkCore;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;

namespace SuperScanner.Infrastructure.Persistence;

public sealed class EfDocumentExportRepository(AppDbContext db) : IDocumentExportRepository
{
    public Task AddAsync(DocumentExport export, CancellationToken cancellationToken)
    {
        RequireTransaction();
        cancellationToken.ThrowIfCancellationRequested();
        db.DocumentExports.Add(export);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        RequireTransaction();
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<OwnedDocumentExport?> FindOwnedAsync(string ownerUid, Guid documentId, Guid exportId,
        CancellationToken cancellationToken) =>
        (from export in db.DocumentExports.AsNoTracking()
         join document in db.Documents.AsNoTracking() on export.DocumentId equals document.Id
         where export.Id == exportId && export.DocumentId == documentId &&
               export.OwnerFirebaseUid == ownerUid && document.OwnerFirebaseUid == ownerUid
         select new OwnedDocumentExport(export, document.Revision)).SingleOrDefaultAsync(cancellationToken);

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Export creation requires a document transaction.");
    }
}
