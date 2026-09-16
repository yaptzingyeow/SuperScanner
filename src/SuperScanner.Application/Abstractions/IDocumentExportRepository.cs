using SuperScanner.Domain.Documents;

namespace SuperScanner.Application.Abstractions;

public sealed record OwnedDocumentExport(DocumentExport Export, long CurrentDocumentRevision);

public interface IDocumentExportRepository
{
    // Creation joins the caller's document transaction; only SaveChangesAsync persists tracked rows.
    Task AddAsync(DocumentExport export, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task<OwnedDocumentExport?> FindOwnedAsync(string ownerUid, Guid documentId, Guid exportId,
        CancellationToken cancellationToken);
}
