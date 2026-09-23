using SuperScanner.Domain.Ocr;
using SuperScanner.Domain.TextEditing;

namespace SuperScanner.Application.TextEditing;

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

        ArgumentNullException.ThrowIfNull(selectedWordIds);
        if (selectedWordIds.Count == 0 || selectedWordIds.Count > maxSelectionWords ||
            selectedWordIds.Any(id => id == Guid.Empty) ||
            selectedWordIds.Distinct().Count() != selectedWordIds.Count)
            throw new InvalidTextSelectionException();

        var selectedSet = selectedWordIds.ToHashSet();
        var words = selection.Elements.Where(element => selectedSet.Contains(element.Id)).ToArray();
        if (words.Length != selectedSet.Count || words.Any(word => word.Kind != OcrElementKind.Word))
            throw new InvalidTextSelectionException();
        if (words.Any(word => word.TextType != OcrTextType.Printed))
            throw new UnsupportedTextSelectionException();
        if (words.Any(word => !HasUsablePolygon(word.Polygon)))
            throw new InvalidTextSelectionException();

        var lineId = words[0].ParentElementId;
        if (lineId is null || words.Any(word => word.ParentElementId != lineId))
            throw new InvalidTextSelectionException();
        var lineWords = selection.Elements
            .Where(element => element.Kind == OcrElementKind.Word && element.ParentElementId == lineId)
            .OrderBy(element => element.ReadingOrder).ToArray();
        var ordered = lineWords.Where(word => selectedSet.Contains(word.Id)).ToArray();
        if (ordered.Length != words.Length ||
            ordered.Select(word => word.ReadingOrder).Distinct().Count() != ordered.Length ||
            ordered[^1].ReadingOrder - ordered[0].ReadingOrder != ordered.Length - 1)
            throw new InvalidTextSelectionException();

        var allPoints = ordered.SelectMany(word => word.Polygon).ToArray();
        var minX = allPoints.Min(point => point.X);
        var minY = allPoints.Min(point => point.Y);
        var maxX = allPoints.Max(point => point.X);
        var maxY = allPoints.Max(point => point.Y);
        var box = new NormalizedBox(minX, minY, maxX - minX, maxY - minY);
        if (selection.Elements.Where(element => element.Kind == OcrElementKind.Word &&
                !selectedSet.Contains(element.Id))
            .Any(word => HasUsablePolygon(word.Polygon) && Overlaps(word.Polygon, box)))
            throw new InvalidTextSelectionException();

        var style = await estimator.EstimateAsync(selection.SourceObjectKey, ordered, ct);
        return new TextStyleProposalDto(selection.ActiveRevisionId, ocrResultId,
            ordered.Select(word => word.Id).ToArray(),
            string.Join(' ', ordered.Select(word => word.Text)), box, style);
    }

    private static bool HasUsablePolygon(IReadOnlyList<OcrPoint> polygon)
    {
        if (polygon.Count != 4 || polygon.Any(point =>
                !double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
                point.X is < 0 or > 1 || point.Y is < 0 or > 1))
            return false;
        var twiceArea = 0.0;
        for (var index = 0; index < 4; index++)
        {
            var next = (index + 1) % 4;
            twiceArea += polygon[index].X * polygon[next].Y -
                polygon[next].X * polygon[index].Y;
        }
        return Math.Abs(twiceArea) > 0.000001;
    }

    private static bool Overlaps(IReadOnlyList<OcrPoint> polygon, NormalizedBox box)
    {
        var x1 = polygon.Min(point => point.X);
        var x2 = polygon.Max(point => point.X);
        var y1 = polygon.Min(point => point.Y);
        var y2 = polygon.Max(point => point.Y);
        var width = Math.Min(x2, box.X + box.Width) - Math.Max(x1, box.X);
        var height = Math.Min(y2, box.Y + box.Height) - Math.Max(y1, box.Y);
        return width > 0.001 && height > 0.001;
    }
}
