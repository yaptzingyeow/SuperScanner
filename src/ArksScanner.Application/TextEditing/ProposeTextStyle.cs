using ArksScanner.Domain.Ocr;
using ArksScanner.Domain.TextEditing;

namespace ArksScanner.Application.TextEditing;

public sealed class ProposeTextStyle(
    ITextSelectionRepository repository,
    ITextStyleEstimator estimator,
    int maxSelectionWords)
{
    public async Task<TextStyleProposalDto> HandleAsync(
        string ownerUid, Guid documentId, Guid pageId, Guid ocrResultId,
        IReadOnlyList<Guid> selectedWordIds, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUid);
        if (documentId == Guid.Empty || pageId == Guid.Empty || ocrResultId == Guid.Empty)
            throw new InvalidTextSelectionException();
        var selection = await repository.FindOwnedAsync(ownerUid, documentId, pageId, ocrResultId, ct)
            ?? throw new TextSelectionNotFoundException();
        if (selection.OcrResultId != ocrResultId || selection.OcrState != OcrResultState.Ready ||
            !string.Equals(selection.SourceObjectKey, selection.OcrSourceObjectKey, StringComparison.Ordinal))
            throw new StaleTextSelectionException();

        var selected = TextSelectionValidator.Validate(selection, selectedWordIds, maxSelectionWords);
        var style = await estimator.EstimateAsync(selection.SourceObjectKey, selected.Words, ct);
        return new TextStyleProposalDto(selection.ActiveRevisionId, ocrResultId,
            selected.WordIds, selected.OriginalText, selected.Box, style);
    }
}
