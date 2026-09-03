using SuperScanner.Domain.Documents;

namespace SuperScanner.Application.Abstractions;

public interface IDocumentRepository
{
    Task AddAsync(Document document, CancellationToken cancellationToken);

    Task<IReadOnlyList<Document>> ListByOwnerAsync(
        string ownerFirebaseUid,
        CancellationToken cancellationToken);
}
