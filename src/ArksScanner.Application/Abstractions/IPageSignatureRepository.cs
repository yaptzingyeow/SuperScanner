using ArksScanner.Domain.Documents;

namespace ArksScanner.Application.Abstractions;

public interface IPageSignatureRepository
{
    Task<IReadOnlyList<PageSignature>> GetActiveForDocumentAsync(Guid documentId, CancellationToken ct);
}
