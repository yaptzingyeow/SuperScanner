using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Ocr;

namespace SuperScanner.Application.Ocr;

public sealed class RequestPageOcr(
    IOcrRepository repository,
    IProcessingJobQueue queue,
    IClock clock)
{
    public async Task<PageOcrDto> HandleAsync(
        string ownerUid,
        Guid documentId,
        Guid pageId,
        bool retryFailed,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUid);
        await using var transaction = await repository.BeginTransactionAsync(ct);
        var source = await repository.FindOwnedSourceAsync(
            ownerUid, documentId, pageId, true, ct)
            ?? throw new OcrResourceNotFoundException();
        if (source.State != PageState.Ready || string.IsNullOrWhiteSpace(source.SourceObjectKey))
            throw new OcrPageNotReadyException();

        var fingerprint = OcrSourceFingerprint.Create(source.SourceObjectKey);
        var result = await repository.FindBySourceAsync(pageId, fingerprint, true, ct);
        var reactivatedFailedJob = false;
        if (result is null)
        {
            result = PageOcrResult.Queue(Guid.NewGuid(), pageId, source.SourceObjectKey,
                fingerprint, "en", clock.UtcNow);
            await repository.AddAsync(result, ct);
        }
        else if (result.State == OcrResultState.Failed)
        {
            if (!retryFailed || !result.FailureRetryable)
                throw new OcrRetryNotAllowedException();
            result.Retry(clock.UtcNow);
            await queue.RetryFailedAsync(JobKey(pageId, fingerprint), ct);
            reactivatedFailedJob = true;
        }

        if (!reactivatedFailedJob)
        {
            await queue.EnqueueAsync("RecognizePageText", result.Id.ToString(),
                JobKey(pageId, fingerprint), ct);
        }

        await repository.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return OcrDtoMapper.Map(result);
    }

    private static string JobKey(Guid pageId, string fingerprint) =>
        $"page:{pageId}:ocr:{fingerprint}";
}
