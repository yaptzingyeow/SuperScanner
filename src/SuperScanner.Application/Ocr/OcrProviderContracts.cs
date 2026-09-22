using SuperScanner.Domain.Ocr;

namespace SuperScanner.Application.Ocr;

public sealed record OcrInput(Stream Content, string MediaType, string Language);

public sealed record OcrLimits(int MaxElements, int MaxRecognizedCharacters);

public sealed record NormalizedOcrDocument(
    string FullText,
    string ProviderName,
    string ModelVersion,
    IReadOnlyList<NormalizedOcrElement> Elements);

public sealed record NormalizedOcrElement(
    string ClientId,
    string? ParentClientId,
    OcrElementKind Kind,
    string Text,
    double Confidence,
    OcrTextType TextType,
    int ReadingOrder,
    IReadOnlyList<OcrPoint> Polygon);

public interface IOcrProvider
{
    Task<NormalizedOcrDocument> RecognizeAsync(OcrInput input, CancellationToken ct);
}

public sealed class OcrProviderException(string safeCode, bool retryable) : Exception("OCR provider operation failed.")
{
    public string SafeCode { get; } = safeCode;
    public bool Retryable { get; } = retryable;
}
