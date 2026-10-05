using ArksScanner.Domain.Documents;

namespace ArksScanner.Application.Abstractions;

public interface IDocumentRepository
{
    Task<IDocumentTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    Task<Document?> FindOwnedForUpdateAsync(string ownerUid, Guid documentId, CancellationToken cancellationToken);

    /// <summary>Locks an owner-deleted document (removed at or after <paramref name="deletedSince"/>) with the pages deleted with it.</summary>
    Task<Document?> FindOwnedDeletedForUpdateAsync(string ownerUid, Guid documentId, DateTimeOffset deletedSince,
        CancellationToken cancellationToken);

    /// <summary>Owner-deleted documents removed at or after <paramref name="deletedSince"/>, newest first.</summary>
    Task<IReadOnlyList<Document>> ListDeletedByOwnerAsync(string ownerUid, DateTimeOffset deletedSince,
        CancellationToken cancellationToken);

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
