using System.Text.Json;

namespace SuperScanner.Domain.Ocr;

public sealed class OcrElement
{
    private const int MaxTextLength = 100_000;

    private OcrElement()
    {
    }

    public Guid Id { get; private set; }
    public Guid PageOcrResultId { get; private set; }
    public Guid? ParentElementId { get; private set; }
    public OcrElementKind Kind { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public double Confidence { get; private set; }
    public OcrTextType TextType { get; private set; }
    public int ReadingOrder { get; private set; }
    public string PolygonJson { get; private set; } = "[]";
    public IReadOnlyList<OcrPoint> Polygon =>
        JsonSerializer.Deserialize<OcrPoint[]>(PolygonJson) ?? [];

    public static OcrElement Create(
        Guid id,
        Guid pageOcrResultId,
        Guid? parentElementId,
        OcrElementKind kind,
        string text,
        double confidence,
        OcrTextType textType,
        int readingOrder,
        IReadOnlyList<OcrPoint> polygon)
    {
        if (id == Guid.Empty) throw new ArgumentException("An OCR element ID is required.", nameof(id));
        if (pageOcrResultId == Guid.Empty)
            throw new ArgumentException("An OCR result ID is required.", nameof(pageOcrResultId));
        if (parentElementId == id)
            throw new ArgumentException("An OCR element cannot parent itself.", nameof(parentElementId));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaxTextLength) throw new ArgumentException("OCR element text is too long.", nameof(text));
        if (!double.IsFinite(confidence) || confidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(confidence));
        if (!Enum.IsDefined(textType)) throw new ArgumentOutOfRangeException(nameof(textType));
        if (readingOrder < 0) throw new ArgumentOutOfRangeException(nameof(readingOrder));
        ArgumentNullException.ThrowIfNull(polygon);
        if (polygon.Count != 4) throw new ArgumentException("An OCR polygon requires four points.", nameof(polygon));
        if (polygon.Any(point =>
                !double.IsFinite(point.X) ||
                !double.IsFinite(point.Y) ||
                point.X is < 0 or > 1 ||
                point.Y is < 0 or > 1))
        {
            throw new ArgumentOutOfRangeException(nameof(polygon));
        }

        return new OcrElement
        {
            Id = id,
            PageOcrResultId = pageOcrResultId,
            ParentElementId = parentElementId,
            Kind = kind,
            Text = text,
            Confidence = confidence,
            TextType = textType,
            ReadingOrder = readingOrder,
            PolygonJson = JsonSerializer.Serialize(polygon)
        };
    }
}
