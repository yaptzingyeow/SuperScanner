using SuperScanner.Domain.Ocr;

namespace SuperScanner.Application.Ocr;

public sealed record PageOcrDto(
    Guid? ResultId,
    string State,
    string? SourceFingerprint,
    string? FullText,
    double? AggregateConfidence,
    int ElementCount,
    string? FailureCode,
    bool CanRetry,
    DateTimeOffset? QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<OcrElementDto> Elements);

public sealed record OcrElementDto(
    Guid Id,
    string Kind,
    string Text,
    double Confidence,
    string TextType,
    int ReadingOrder,
    IReadOnlyList<OcrPoint> Polygon,
    IReadOnlyList<OcrElementDto> Children);

public static class OcrDtoMapper
{
    public static PageOcrDto Map(PageOcrResult? result) => result is null
        ? new(null, "NotRequested", null, null, null, 0, null, false,
            null, null, null, [])
        : new(
            result.Id,
            result.State.ToString(),
            result.SourceFingerprint,
            result.State == OcrResultState.Ready ? result.FullText : null,
            result.State == OcrResultState.Ready ? result.AggregateConfidence : null,
            result.State == OcrResultState.Ready ? result.ElementCount : 0,
            result.State == OcrResultState.Failed ? result.FailureCode : null,
            result.State == OcrResultState.Failed && result.FailureRetryable,
            result.QueuedAt,
            result.StartedAt,
            result.CompletedAt,
            result.State == OcrResultState.Ready
                ? result.Elements.Where(element => element.ParentElementId is null)
                    .OrderBy(element => element.ReadingOrder)
                    .Select(element => MapElement(element, result.Elements))
                    .ToArray()
                : []);

    private static OcrElementDto MapElement(
        OcrElement element,
        IReadOnlyCollection<OcrElement> all) => new(
            element.Id,
            element.Kind.ToString(),
            element.Text,
            element.Confidence,
            element.TextType.ToString(),
            element.ReadingOrder,
            element.Polygon,
            all.Where(candidate => candidate.ParentElementId == element.Id)
                .OrderBy(candidate => candidate.ReadingOrder)
                .Select(candidate => MapElement(candidate, all))
                .ToArray());
}
