using SuperScanner.Application.Ocr;

namespace SuperScanner.Infrastructure.Ocr;

public sealed class DisabledOcrProvider : IOcrProvider
{
    public Task<NormalizedOcrDocument> RecognizeAsync(OcrInput input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromException<NormalizedOcrDocument>(
            new OcrProviderException("ocr_disabled", false));
    }
}
