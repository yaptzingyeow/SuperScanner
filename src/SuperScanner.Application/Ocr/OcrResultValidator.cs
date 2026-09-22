using SuperScanner.Domain.Ocr;

namespace SuperScanner.Application.Ocr;

public static class OcrResultValidator
{
    public static NormalizedOcrDocument Validate(
        NormalizedOcrDocument document,
        OcrLimits limits)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(limits);
        if (limits.MaxElements < 1 || limits.MaxRecognizedCharacters < 1)
            throw Invalid();
        if (string.IsNullOrWhiteSpace(document.ProviderName) ||
            string.IsNullOrWhiteSpace(document.ModelVersion) ||
            document.FullText is null ||
            document.Elements is null ||
            document.Elements.Count > limits.MaxElements)
        {
            throw Invalid();
        }

        var elementCharacters = 0L;
        var byId = new Dictionary<string, NormalizedOcrElement>(StringComparer.Ordinal);
        foreach (var element in document.Elements)
        {
            if (string.IsNullOrWhiteSpace(element.ClientId) ||
                !byId.TryAdd(element.ClientId, element) ||
                element.Text is null ||
                !Enum.IsDefined(element.Kind) ||
                !Enum.IsDefined(element.TextType) ||
                !double.IsFinite(element.Confidence) ||
                element.Confidence is < 0 or > 1 ||
                element.ReadingOrder < 0 ||
                element.Polygon is null ||
                element.Polygon.Count != 4 ||
                element.Polygon.Any(point =>
                    !double.IsFinite(point.X) ||
                    !double.IsFinite(point.Y) ||
                    point.X is < 0 or > 1 ||
                    point.Y is < 0 or > 1))
            {
                throw Invalid();
            }

            elementCharacters += element.Text.Length;
        }

        if (Math.Max(document.FullText.Length, elementCharacters) > limits.MaxRecognizedCharacters)
            throw Invalid();

        foreach (var element in document.Elements)
        {
            if (element.ParentClientId is not null && !byId.ContainsKey(element.ParentClientId))
                throw Invalid();
            if (!HasValidParentKind(element, byId)) throw Invalid();
        }

        if (document.Elements
            .GroupBy(element => (element.ParentClientId, element.ReadingOrder))
            .Any(group => group.Count() > 1))
        {
            throw Invalid();
        }

        EnsureAcyclic(document.Elements, byId);
        return document;
    }

    private static bool HasValidParentKind(
        NormalizedOcrElement element,
        IReadOnlyDictionary<string, NormalizedOcrElement> byId) => element.Kind switch
        {
            OcrElementKind.Block => element.ParentClientId is null,
            OcrElementKind.Line => element.ParentClientId is not null &&
                byId[element.ParentClientId].Kind == OcrElementKind.Block,
            OcrElementKind.Word => element.ParentClientId is not null &&
                byId[element.ParentClientId].Kind == OcrElementKind.Line,
            _ => false
        };

    private static void EnsureAcyclic(
        IReadOnlyList<NormalizedOcrElement> elements,
        IReadOnlyDictionary<string, NormalizedOcrElement> byId)
    {
        foreach (var element in elements)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { element.ClientId };
            var parent = element.ParentClientId;
            while (parent is not null)
            {
                if (!visited.Add(parent)) throw Invalid();
                parent = byId[parent].ParentClientId;
            }
        }
    }

    private static OcrProviderException Invalid() => new("ocr_invalid_response", false);
}
