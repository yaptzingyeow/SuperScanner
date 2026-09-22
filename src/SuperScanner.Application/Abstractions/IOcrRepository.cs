using SuperScanner.Domain.Ocr;

namespace SuperScanner.Application.Abstractions;

using SuperScanner.Domain.Documents;

public sealed record OcrPageSource(
    Guid PageId,
    PageState State,
    string? SourceObjectKey,
    string MediaType);

public interface IOcrRepository
{
    Task<IOcrTransaction> BeginTransactionAsync(CancellationToken ct);

    Task<OcrPageSource?> FindOwnedSourceAsync(
        string ownerUid,
        Guid documentId,
        Guid pageId,
        bool forUpdate,
        CancellationToken ct);

    Task<PageOcrResult?> FindBySourceAsync(
        Guid pageId,
        string sourceFingerprint,
        bool forUpdate,
        CancellationToken ct);

    Task AddAsync(PageOcrResult result, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}

public interface IOcrTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}
