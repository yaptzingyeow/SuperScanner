using SuperScanner.Domain.Ocr;
using SuperScanner.Domain.TextEditing;

namespace SuperScanner.Application.TextEditing;

public sealed record OwnedTextSelection(
    Guid? ActiveRevisionId,
    string SourceObjectKey,
    Guid OcrResultId,
    OcrResultState OcrState,
    string OcrSourceObjectKey,
    IReadOnlyList<OcrElement> Elements);

public interface ITextSelectionRepository
{
    Task<OwnedTextSelection?> FindOwnedAsync(string ownerUid, Guid documentId,
        Guid pageId, Guid ocrResultId, CancellationToken ct);
}

public sealed record FontCandidate(string CatalogueId, string Version, double Score);

public sealed record TextStyleEstimate(
    IReadOnlyList<FontCandidate> Candidates,
    double Confidence,
    string ColorHex,
    double FontSizePoints,
    int FontWeight,
    double LetterSpacing,
    double BaselineAngleDegrees,
    string Alignment);

public interface ITextStyleEstimator
{
    Task<TextStyleEstimate> EstimateAsync(string sourceObjectKey,
        IReadOnlyList<OcrElement> words, CancellationToken ct);
}

public sealed record TextStyleProposalDto(
    Guid? ActiveRevisionId,
    Guid OcrResultId,
    IReadOnlyList<Guid> WordIds,
    string OriginalText,
    NormalizedBox Box,
    TextStyleEstimate Style);

public sealed class TextSelectionNotFoundException : Exception;
public sealed class StaleTextSelectionException : Exception;
public sealed class InvalidTextSelectionException : Exception;
public sealed class UnsupportedTextSelectionException : Exception;
