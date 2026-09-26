using Microsoft.EntityFrameworkCore;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;

namespace SuperScanner.Infrastructure.Persistence;

public sealed class EfPageSignatureRepository(AppDbContext db) : IPageSignatureRepository
{
    public async Task<IReadOnlyList<PageSignature>> GetActiveForDocumentAsync(Guid documentId, CancellationToken ct) =>
        (await db.PageSignatures.AsNoTracking().Where(x => x.DocumentId == documentId && x.DeletedAt == null)
            .ToListAsync(ct)).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToArray();
}
