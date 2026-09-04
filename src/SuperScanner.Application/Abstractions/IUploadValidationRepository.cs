using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Uploads;

namespace SuperScanner.Application.Abstractions;

public sealed record UploadValidationTarget(UploadIntent Upload, Page Page);

public interface IUploadValidationRepository
{
    Task<UploadValidationTarget?> FindAsync(Guid uploadId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
