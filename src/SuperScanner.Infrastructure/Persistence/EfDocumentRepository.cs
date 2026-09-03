using Microsoft.EntityFrameworkCore;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;

namespace SuperScanner.Infrastructure.Persistence;

public sealed class EfDocumentRepository(AppDbContext db) : IDocumentRepository
{
    public async Task AddAsync(Document document, CancellationToken cancellationToken)
    {
        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Document>> ListByOwnerAsync(
        string ownerFirebaseUid,
        CancellationToken cancellationToken) =>
        await db.Documents
            .AsNoTracking()
            .Include(document => document.Pages)
            .Where(document => document.OwnerFirebaseUid == ownerFirebaseUid)
            .OrderByDescending(document => document.UpdatedAt)
            .ToListAsync(cancellationToken);
}
