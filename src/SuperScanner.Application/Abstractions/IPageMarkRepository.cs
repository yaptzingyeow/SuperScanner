using SuperScanner.Domain.Documents;

namespace SuperScanner.Application.Abstractions;

public interface IPageMarkRepository
{
    Task<IReadOnlyList<PageMark>> GetActiveForDocumentAsync(Guid documentId, CancellationToken ct);
}
