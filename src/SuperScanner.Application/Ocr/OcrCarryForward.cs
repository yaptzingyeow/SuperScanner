using SuperScanner.Domain.Ocr;
using SuperScanner.Domain.TextEditing;

namespace SuperScanner.Application.Ocr;

/// <summary>
/// Builds the OCR of a page revision produced by a text edit from the OCR of its source
/// revision, without recognizing the page again. A text edit changes pixels only where the
/// selected words were and where the new text is drawn, so every other word keeps its text
/// and position; the selected words are dropped and the typed text is added in its box.
/// </summary>
public static class OcrCarryForward
{
    public const string ModelVersion = "carried-forward";

    public sealed record Result(string FullText, IReadOnlyList<OcrElement> Elements);

    public static Result Build(
        Guid resultId,
        IReadOnlyCollection<OcrElement> source,
        IReadOnlySet<Guid> removedWordIds,
        NormalizedBox? replacementBox,
        string replacementText)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(removedWordIds);
        var children = source.Where(element => element.ParentElementId is not null)
            .ToLookup(element => element.ParentElementId!.Value);
        var output = new List<OcrElement>();
        var blockTexts = new List<(int Order, string Text)>();

        // Copies an element and its kept descendants; null when nothing of it is left.
        OcrElement? Copy(OcrElement element, Guid? parentId)
        {
            if (element.Kind == OcrElementKind.Word && removedWordIds.Contains(element.Id)) return null;
            var id = Guid.NewGuid();
            var originals = children[element.Id].OrderBy(child => child.ReadingOrder).ToList();
            var kept = originals.Select(child => Copy(child, id)).OfType<OcrElement>().ToList();
            if (originals.Count > 0 && kept.Count == 0) return null;
            var changed = kept.Count != originals.Count ||
                kept.Zip(originals).Any(pair => pair.First.Text != pair.Second.Text);
            var text = changed
                ? string.Join(element.Kind == OcrElementKind.Line ? " " : "\n", kept.Select(child => child.Text))
                : element.Text;
            var copy = OcrElement.Create(id, resultId, parentId, element.Kind, text, element.Confidence,
                element.TextType, element.ReadingOrder,
                changed ? Bounds(kept.Select(child => child.Polygon)) : element.Polygon);
            output.Add(copy);
            return copy;
        }

        foreach (var root in source.Where(element => element.ParentElementId is null)
                     .OrderBy(element => element.ReadingOrder))
        {
            if (Copy(root, null) is { } copied) blockTexts.Add((copied.ReadingOrder, copied.Text));
        }

        if (replacementBox is not null && !string.IsNullOrWhiteSpace(replacementText))
        {
            var order = source.Where(element => element.ParentElementId is null)
                .Select(element => element.ReadingOrder).DefaultIfEmpty(-1).Max() + 1;
            var text = AddText(resultId, replacementBox, replacementText, order, output);
            blockTexts.Add((order, text));
        }

        return new(string.Join("\n\n", blockTexts.OrderBy(block => block.Order).Select(block => block.Text)), output);
    }

    /// <summary>Adds the typed text as one block: a line per text line, words spread by length.</summary>
    private static string AddText(Guid resultId, NormalizedBox box, string text, int order, List<OcrElement> output)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n')
            .Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
        var blockId = Guid.NewGuid();
        output.Add(OcrElement.Create(blockId, resultId, null, OcrElementKind.Block, string.Join('\n', lines), 1,
            OcrTextType.Printed, order, Rectangle(box.X, box.Y, box.Width, box.Height)));
        var lineHeight = box.Height / lines.Count;
        for (var l = 0; l < lines.Count; l++)
        {
            var lineId = Guid.NewGuid();
            var top = box.Y + l * lineHeight;
            output.Add(OcrElement.Create(lineId, resultId, blockId, OcrElementKind.Line, lines[l], 1,
                OcrTextType.Printed, l, Rectangle(box.X, top, box.Width, lineHeight)));
            var words = lines[l].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // Each word gets a share of the width by its length, with one character per gap.
            var units = (double)words.Sum(word => word.Length) + words.Length - 1;
            var cursor = 0d;
            for (var w = 0; w < words.Length; w++)
            {
                var left = box.X + box.Width * cursor / units;
                var width = box.Width * words[w].Length / units;
                output.Add(OcrElement.Create(Guid.NewGuid(), resultId, lineId, OcrElementKind.Word, words[w], 1,
                    OcrTextType.Printed, w, Rectangle(left, top, width, lineHeight)));
                cursor += words[w].Length + 1;
            }
        }

        return string.Join('\n', lines);
    }

    private static OcrPoint[] Rectangle(double x, double y, double width, double height)
    {
        double Clamp(double value) => Math.Clamp(value, 0, 1);
        return
        [
            new(Clamp(x), Clamp(y)), new(Clamp(x + width), Clamp(y)),
            new(Clamp(x + width), Clamp(y + height)), new(Clamp(x), Clamp(y + height)),
        ];
    }

    private static OcrPoint[] Bounds(IEnumerable<IReadOnlyList<OcrPoint>> polygons)
    {
        var points = polygons.SelectMany(polygon => polygon).ToList();
        var left = points.Min(point => point.X);
        var top = points.Min(point => point.Y);
        return Rectangle(left, top, points.Max(point => point.X) - left, points.Max(point => point.Y) - top);
    }
}
