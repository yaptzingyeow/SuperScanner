using Microsoft.Extensions.Options;
using SuperScanner.Application.Abstractions;
using SuperScanner.Application.Ocr;
using SuperScanner.Domain.Ocr;

namespace SuperScanner.Infrastructure.Ocr;

public sealed class OcrJobScheduler(
    IOcrRepository repository,
    IProcessingJobQueue queue,
    IClock clock,
    IOptions<OcrOptions> options)
{
    public async Task EnsureQueuedAsync(
        Guid pageId,
        string sourceObjectKey,
        string mediaType,
        CancellationToken ct)
    {
        if (!options.Value.Enabled) return;
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceObjectKey);
        if (mediaType is not ("image/jpeg" or "image/png"))
            throw new ArgumentException("OCR source must be a supported image.", nameof(mediaType));

        var fingerprint = OcrSourceFingerprint.Create(sourceObjectKey);
        var result = await repository.FindBySourceAsync(pageId, fingerprint, true, ct);
        if (result is null)
        {
            result = PageOcrResult.Queue(Guid.NewGuid(), pageId, sourceObjectKey,
                fingerprint, options.Value.Language, clock.UtcNow);
            await repository.AddAsync(result, ct);
        }

        await queue.EnqueueAsync("RecognizePageText", result.Id.ToString(),
            $"page:{pageId}:ocr:{fingerprint}", ct);
        await repository.SaveChangesAsync(ct);
    }
}
