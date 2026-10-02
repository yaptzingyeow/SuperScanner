using Microsoft.EntityFrameworkCore;
using ArksScanner.Application.Abstractions;
using ArksScanner.Domain.Documents;
using ArksScanner.Domain.Uploads;

namespace ArksScanner.Infrastructure.Persistence;

public sealed class EfUploadIntentRepository(AppDbContext db) : IUploadIntentRepository
{
    public Task<Document?> FindOwnedDocumentAsync(
        string ownerFirebaseUid,
        Guid documentId,
        CancellationToken cancellationToken) =>
        db.Documents
            .Include(document => document.Pages)
            .SingleOrDefaultAsync(
                document => document.Id == documentId && document.OwnerFirebaseUid == ownerFirebaseUid,
                cancellationToken);

    public async Task AddAsync(UploadIntent uploadIntent, CancellationToken cancellationToken)
    {
        await db.UploadIntents.AddAsync(uploadIntent, cancellationToken);
    }

    public Task<UploadIntent?> FindOwnedUploadAsync(
        string ownerFirebaseUid,
        Guid documentId,
        Guid uploadId,
        CancellationToken cancellationToken) =>
        db.UploadIntents.SingleOrDefaultAsync(
            upload => upload.Id == uploadId &&
                      upload.DocumentId == documentId &&
                      upload.OwnerFirebaseUid == ownerFirebaseUid,
            cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);
}
