using ArksScanner.Domain.Documents;

namespace ArksScanner.Application.Abstractions;

public interface IPageMarkRepository
{
    Task<IReadOnlyList<PageMark>> GetActiveForDocumentAsync(Guid documentId, CancellationToken ct);
}
