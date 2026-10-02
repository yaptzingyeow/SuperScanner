using ArksScanner.Domain.Ocr;
using ArksScanner.Domain.TextEditing;

namespace ArksScanner.Application.TextEditing;

public sealed record ValidatedTextSelection(
    IReadOnlyList<Guid> WordIds,
    IReadOnlyList<OcrElement> Words,
    string OriginalText,
    NormalizedBox Box);

public static class TextSelectionValidator
{
    public static ValidatedTextSelection Validate(OwnedTextSelection selection,
        IReadOnlyList<Guid> selectedWordIds, int maxSelectionWords)
    {
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
        if (OverlapsUnselected(selection.Elements, selectedSet, box))
            throw new InvalidTextSelectionException();
        return new ValidatedTextSelection(ordered.Select(word => word.Id).ToArray(),
            ordered, string.Join(' ', ordered.Select(word => word.Text)), box);
    }

    /// <summary>Share of another word's box the selection may cover before it counts as a clash.</summary>
    public const double MaximumCoveredShare = .25;

    /// <summary>
    /// True when the selection box covers a real part (a quarter or more) of a word that is not
    /// selected. OCR boxes of neighbouring words commonly touch by a pixel or two; that is not a
    /// clash (the renderer leaves those pixels alone). Punctuation directly before or after the
    /// selection on the same line (a comma after "Jalan") is allowed too.
    /// </summary>
    public static bool OverlapsUnselected(IReadOnlyList<OcrElement> elements,
        IReadOnlyCollection<Guid> selectedIds, NormalizedBox box)
    {
        var selected = selectedIds.ToHashSet();
        var selectedWords = elements.Where(element => selected.Contains(element.Id)).ToArray();
        var lineId = selectedWords.Select(word => word.ParentElementId).Distinct().SingleOrDefault();
        var first = selectedWords.Length == 0 ? 0 : selectedWords.Min(word => word.ReadingOrder);
        var last = selectedWords.Length == 0 ? 0 : selectedWords.Max(word => word.ReadingOrder);
        bool AdjacentOnLine(OcrElement word) =>
            lineId is not null && word.ParentElementId == lineId &&
            (word.ReadingOrder == first - 1 || word.ReadingOrder == last + 1);
        return elements.Where(element => element.Kind == OcrElementKind.Word &&
                !selected.Contains(element.Id))
            .Any(word => HasUsablePolygon(word.Polygon) && CoveredShare(word.Polygon, box) >= MaximumCoveredShare &&
                !AdjacentOnLine(word));
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

    private static double CoveredShare(IReadOnlyList<OcrPoint> polygon, NormalizedBox box)
    {
        var x1 = polygon.Min(point => point.X);
        var x2 = polygon.Max(point => point.X);
        var y1 = polygon.Min(point => point.Y);
        var y2 = polygon.Max(point => point.Y);
        var width = Math.Min(x2, box.X + box.Width) - Math.Max(x1, box.X);
        var height = Math.Min(y2, box.Y + box.Height) - Math.Max(y1, box.Y);
        var area = (x2 - x1) * (y2 - y1);
        return width <= 0 || height <= 0 || area <= 0 ? 0 : width * height / area;
    }
}
