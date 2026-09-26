using SuperScanner.Domain.Documents;

namespace SuperScanner.Application.Abstractions;

public interface IPageSignatureRepository
{
    Task<IReadOnlyList<PageSignature>> GetActiveForDocumentAsync(Guid documentId, CancellationToken ct);
}
