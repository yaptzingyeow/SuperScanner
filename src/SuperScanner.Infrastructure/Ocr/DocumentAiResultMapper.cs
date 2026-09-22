using Google.Cloud.DocumentAI.V1;
using SuperScanner.Application.Ocr;
using SuperScanner.Domain.Ocr;

namespace SuperScanner.Infrastructure.Ocr;

public static class DocumentAiResultMapper
{
    private const double DriftTolerance = 1e-9;

    public static NormalizedOcrDocument Map(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var elements = new List<NormalizedOcrElement>();
        var globalBlockOrder = 0;

        for (var pageIndex = 0; pageIndex < document.Pages.Count; pageIndex++)
        {
            MapPage(document.Text ?? string.Empty, document.Pages[pageIndex], pageIndex + 1,
                elements, ref globalBlockOrder);
        }

        var modelVersion = document.Revisions
            .LastOrDefault(revision => !string.IsNullOrWhiteSpace(revision.Processor))
            ?.Processor ?? "document-ocr";

        return new NormalizedOcrDocument(
            document.Text ?? string.Empty,
            OcrProviderNames.GoogleDocumentAi,
            modelVersion,
            elements);
    }

    private static void MapPage(
        string documentText,
        Document.Types.Page page,
        int pageNumber,
        ICollection<NormalizedOcrElement> elements,
        ref int globalBlockOrder)
    {
        var blocks = new List<MappedRange>();
        for (var blockIndex = 0; blockIndex < page.Blocks.Count; blockIndex++)
        {
            var layout = RequireLayout(page.Blocks[blockIndex].Layout);
            var range = GetRange(layout.TextAnchor);
            var id = $"p{pageNumber}-b{blockIndex + 1}";
            elements.Add(CreateElement(id, null, OcrElementKind.Block,
                DocumentAiTextAnchorReader.Read(documentText, layout.TextAnchor),
                layout, page, OcrTextType.Printed, globalBlockOrder++));
            blocks.Add(new MappedRange(id, range));
        }

        var lines = new List<MappedRange>();
        var lineCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var paragraphIndex = 0; paragraphIndex < page.Paragraphs.Count; paragraphIndex++)
        {
            var layout = RequireLayout(page.Paragraphs[paragraphIndex].Layout);
            var range = GetRange(layout.TextAnchor);
            var block = FindParent(blocks, range);
            var lineNumber = Next(lineCounts, block.Id);
            var id = $"{block.Id}-l{lineNumber}";
            elements.Add(CreateElement(id, block.Id, OcrElementKind.Line,
                DocumentAiTextAnchorReader.Read(documentText, layout.TextAnchor),
                layout, page, OcrTextType.Printed, lineNumber - 1));
            lines.Add(new MappedRange(id, range));
        }

        var wordCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var token in page.Tokens)
        {
            var layout = RequireLayout(token.Layout);
            var range = GetRange(layout.TextAnchor);
            var line = FindParent(lines, range);
            var wordNumber = Next(wordCounts, line.Id);
            var id = $"{line.Id}-w{wordNumber}";
            var textType = token.StyleInfo?.Handwritten == true
                ? OcrTextType.Handwritten
                : OcrTextType.Printed;
            elements.Add(CreateElement(id, line.Id, OcrElementKind.Word,
                DocumentAiTextAnchorReader.Read(documentText, layout.TextAnchor),
                layout, page, textType, wordNumber - 1));
        }
    }

    private static NormalizedOcrElement CreateElement(
        string id,
        string? parentId,
        OcrElementKind kind,
        string text,
        Document.Types.Page.Types.Layout layout,
        Document.Types.Page page,
        OcrTextType textType,
        int readingOrder) => new(
            id,
            parentId,
            kind,
            text,
            NormalizeConfidence(layout.Confidence),
            textType,
            readingOrder,
            NormalizePolygon(layout.BoundingPoly, page.Dimension));

    private static IReadOnlyList<OcrPoint> NormalizePolygon(
        BoundingPoly? polygon,
        Document.Types.Page.Types.Dimension? dimension)
    {
        if (polygon is null)
            throw InvalidResponse();

        if (polygon.NormalizedVertices.Count > 0)
        {
            if (polygon.NormalizedVertices.Count != 4)
                throw InvalidResponse();

            return polygon.NormalizedVertices
                .Select(vertex => new OcrPoint(
                    NormalizeCoordinate(vertex.X),
                    NormalizeCoordinate(vertex.Y)))
                .ToArray();
        }

        if (polygon.Vertices.Count != 4 || dimension is null ||
            !double.IsFinite(dimension.Width) || !double.IsFinite(dimension.Height) ||
            dimension.Width <= 0 || dimension.Height <= 0)
        {
            throw InvalidResponse();
        }

        return polygon.Vertices
            .Select(vertex => new OcrPoint(
                NormalizeCoordinate(vertex.X / dimension.Width),
                NormalizeCoordinate(vertex.Y / dimension.Height)))
            .ToArray();
    }

    private static double NormalizeCoordinate(double value)
    {
        if (!double.IsFinite(value) || value < -DriftTolerance || value > 1 + DriftTolerance)
            throw InvalidResponse();

        return Math.Clamp(value, 0, 1);
    }

    private static double NormalizeConfidence(double value)
    {
        if (!double.IsFinite(value) || value < -DriftTolerance || value > 1 + DriftTolerance)
            throw InvalidResponse();

        return Math.Clamp(value, 0, 1);
    }

    private static Document.Types.Page.Types.Layout RequireLayout(
        Document.Types.Page.Types.Layout? layout) => layout ?? throw InvalidResponse();

    private static AnchorRange GetRange(Document.Types.TextAnchor? anchor)
    {
        if (anchor is null || anchor.TextSegments.Count == 0)
            throw InvalidResponse();

        long? start = null;
        long? end = null;
        foreach (var segment in anchor.TextSegments)
        {
            if (segment.StartIndex < 0 || segment.EndIndex < segment.StartIndex)
                throw InvalidResponse();

            start = start is null ? segment.StartIndex : Math.Min(start.Value, segment.StartIndex);
            end = end is null ? segment.EndIndex : Math.Max(end.Value, segment.EndIndex);
        }

        if (start is null || end is null || end <= start)
            throw InvalidResponse();

        return new AnchorRange(start.Value, end.Value);
    }

    private static MappedRange FindParent(IEnumerable<MappedRange> candidates, AnchorRange child) =>
        candidates.FirstOrDefault(candidate => candidate.Range.Contains(child))
        ?? throw InvalidResponse();

    private static int Next(IDictionary<string, int> counts, string parentId)
    {
        counts.TryGetValue(parentId, out var count);
        count++;
        counts[parentId] = count;
        return count;
    }

    private static OcrProviderException InvalidResponse() =>
        new("ocr_invalid_response", retryable: false);

    private sealed record MappedRange(string Id, AnchorRange Range);

    private readonly record struct AnchorRange(long Start, long End)
    {
        public bool Contains(AnchorRange child) => child.Start >= Start && child.End <= End;
    }
}
