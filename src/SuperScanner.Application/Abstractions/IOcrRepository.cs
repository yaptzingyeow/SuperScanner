using SuperScanner.Domain.Ocr;

namespace SuperScanner.Application.Abstractions;

public sealed record OcrPageSource(Guid PageId, string SourceObjectKey, string MediaType);

public interface IOcrRepository
{
    Task<IOcrTransaction> BeginTransactionAsync(CancellationToken ct);

    Task<OcrPageSource?> FindOwnedReadySourceAsync(
        string ownerUid,
        Guid documentId,
        Guid pageId,
        CancellationToken ct);

    Task<PageOcrResult?> FindBySourceAsync(
        Guid pageId,
        string sourceFingerprint,
        bool forUpdate,
        CancellationToken ct);

    Task<PageOcrResult?> FindCurrentOwnedAsync(
        string ownerUid,
        Guid documentId,
        Guid pageId,
        CancellationToken ct);

    Task AddAsync(PageOcrResult result, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}

public interface IOcrTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}
