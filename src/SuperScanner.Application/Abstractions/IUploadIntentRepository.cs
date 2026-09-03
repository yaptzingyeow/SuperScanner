using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Uploads;

namespace SuperScanner.Application.Abstractions;

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

    Task AddAsync(
        UploadIntent uploadIntent,
        Page page,
        CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
