using Microsoft.EntityFrameworkCore;
using ArksScanner.Application.Abstractions;
using ArksScanner.Domain.Documents;

namespace ArksScanner.Infrastructure.Persistence;

public sealed class EfPageSignatureRepository(AppDbContext db) : IPageSignatureRepository
{
    public async Task<IReadOnlyList<PageSignature>> GetActiveForDocumentAsync(Guid documentId, CancellationToken ct) =>
        (await db.PageSignatures.AsNoTracking().Where(x => x.DocumentId == documentId && x.DeletedAt == null)
            .ToListAsync(ct)).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToArray();
}
