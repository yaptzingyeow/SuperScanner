using Microsoft.EntityFrameworkCore;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;

namespace SuperScanner.Infrastructure.Persistence;

public sealed class EfPageMarkRepository(AppDbContext db) : IPageMarkRepository
{
    public async Task<IReadOnlyList<PageMark>> GetActiveForDocumentAsync(Guid documentId, CancellationToken ct) =>
        (await db.PageMarks.AsNoTracking().Where(x => x.DocumentId == documentId && x.DeletedAt == null)
            .ToListAsync(ct)).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToArray();
}
