using SuperScanner.Domain.Documents;

namespace SuperScanner.Application.Abstractions;

public interface IDocumentRepository
{
    Task<IDocumentTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    Task<Document?> FindOwnedForUpdateAsync(string ownerUid, Guid documentId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    Task AddAsync(Document document, CancellationToken cancellationToken);

    Task<IReadOnlyList<Document>> ListByOwnerAsync(
        string ownerFirebaseUid,
        CancellationToken cancellationToken);
}

public interface IDocumentTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}
