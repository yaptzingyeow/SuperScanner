using Microsoft.EntityFrameworkCore;
using SuperScanner.Application.Abstractions;

namespace SuperScanner.Infrastructure.Persistence;

public sealed class EfUploadValidationRepository(AppDbContext db) : IUploadValidationRepository
{
    public async Task<UploadValidationTarget?> FindAsync(
        Guid uploadId,
        CancellationToken cancellationToken)
    {
        var upload = await db.UploadIntents.SingleOrDefaultAsync(
            candidate => candidate.Id == uploadId,
            cancellationToken);
        if (upload is null)
        {
            return null;
        }

        var document = await db.Documents.SingleOrDefaultAsync(
            candidate => candidate.Id == upload.DocumentId, cancellationToken);
        return document is null ? null : new UploadValidationTarget(upload, document);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);
}
