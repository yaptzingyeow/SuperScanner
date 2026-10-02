using ArksScanner.Domain.Documents;
using ArksScanner.Domain.Uploads;

namespace ArksScanner.Application.Abstractions;

public sealed record UploadValidationTarget(UploadIntent Upload, Document Document);

public interface IUploadValidationRepository
{
    Task<UploadValidationTarget?> FindAsync(Guid uploadId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
