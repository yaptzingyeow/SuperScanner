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

        var page = await db.Pages.SingleOrDefaultAsync(
            candidate => candidate.Id == upload.PageId,
            cancellationToken);
        return page is null ? null : new UploadValidationTarget(upload, page);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);
}
