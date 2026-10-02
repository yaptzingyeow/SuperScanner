using ArksScanner.Domain.Documents;
using ArksScanner.Domain.Uploads;

namespace ArksScanner.Application.Abstractions;

public interface IUploadIntentRepository
{
    Task<Document?> FindOwnedDocumentAsync(
        string ownerFirebaseUid,
        Guid documentId,
        CancellationToken cancellationToken);

    Task<UploadIntent?> FindOwnedUploadAsync(
        string ownerFirebaseUid,
        Guid documentId,
        Guid uploadId,
        CancellationToken cancellationToken);

    Task AddAsync(UploadIntent uploadIntent, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
