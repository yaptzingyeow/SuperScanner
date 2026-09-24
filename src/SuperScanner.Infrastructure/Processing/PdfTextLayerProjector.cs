using SuperScanner.Domain.Ocr;

namespace SuperScanner.Infrastructure.Processing;

public static class PdfTextLayerProjector
{
    public static IReadOnlyList<PdfTextLayerWord> Project(
        IReadOnlyCollection<OcrElement> elements,
        double pageWidthPoints,
        double pageHeightPoints,
        PdfTextLayerLimits limits)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(limits);
        ValidatePageDimensions(pageWidthPoints, pageHeightPoints);
        ValidateLimits(limits);

        var projected = new List<PdfTextLayerWord>(Math.Min(elements.Count, limits.MaximumWordsPerPage));
        var characters = 0;

        foreach (var element in elements
                     .Where(candidate => candidate.Kind == OcrElementKind.Word)
                     .OrderBy(candidate => candidate.ReadingOrder)
                     .ThenBy(candidate => candidate.Id))
        {
            if (projected.Count >= limits.MaximumWordsPerPage)
            {
                break;
            }

            var text = element.Text?.Trim();
            if (string.IsNullOrEmpty(text) ||
                text.Length > limits.MaximumCharactersPerWord ||
                characters + text.Length > limits.MaximumCharactersPerPage)
            {
                continue;
            }

            IReadOnlyList<OcrPoint> polygon;
            try
            {
                polygon = element.Polygon;
            }
            catch
            {
                continue;
            }

            if (!TryProject(
                    polygon,
                    pageWidthPoints,
                    pageHeightPoints,
                    limits,
                    out var geometry))
            {
                continue;
            }

            projected.Add(new PdfTextLayerWord(
                text,
                geometry.X,
                geometry.Y,
                geometry.Width,
                geometry.Height,
                geometry.AngleDegrees,
                element.ReadingOrder));
            characters += text.Length;
        }

        return projected;
    }

    private static bool TryProject(
        IReadOnlyList<OcrPoint> polygon,
        double pageWidth,
        double pageHeight,
        PdfTextLayerLimits limits,
        out ProjectedGeometry geometry)
    {
        geometry = default;
        if (polygon.Count != 4 || polygon.Any(point =>
                !double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
                point.X is < 0 or > 1 || point.Y is < 0 or > 1))
        {
            return false;
        }

        var left = Math.Clamp(polygon.Min(point => point.X) * pageWidth, 0, pageWidth);
        var right = Math.Clamp(polygon.Max(point => point.X) * pageWidth, 0, pageWidth);
        var imageTop = Math.Clamp(polygon.Min(point => point.Y) * pageHeight, 0, pageHeight);
        var imageBottom = Math.Clamp(polygon.Max(point => point.Y) * pageHeight, 0, pageHeight);
        var topWidth = Distance(polygon[0], polygon[1], pageWidth, pageHeight);
        var bottomWidth = Distance(polygon[3], polygon[2], pageWidth, pageHeight);
        var leftHeight = Distance(polygon[0], polygon[3], pageWidth, pageHeight);
        var rightHeight = Distance(polygon[1], polygon[2], pageWidth, pageHeight);
        var width = (topWidth + bottomWidth) / 2;
        var height = (leftHeight + rightHeight) / 2;

        if (!double.IsFinite(width) || !double.IsFinite(height) ||
            width < limits.MinimumDimensionPoints || height < limits.MinimumDimensionPoints)
        {
            return false;
        }

        var topLeft = polygon[0];
        var topRight = polygon[1];
        var angle = Math.Atan2(
                -(topRight.Y - topLeft.Y) * pageHeight,
                (topRight.X - topLeft.X) * pageWidth) *
            180 / Math.PI;
        if (!double.IsFinite(angle) || Math.Abs(angle) > limits.MaximumAbsoluteAngleDegrees)
        {
            angle = 0;
        }

        geometry = new ProjectedGeometry(
            left,
            pageHeight - imageBottom,
            width,
            height,
            angle);
        return true;
    }

    private static double Distance(OcrPoint first, OcrPoint second, double pageWidth, double pageHeight)
    {
        var x = (second.X - first.X) * pageWidth;
        var y = (second.Y - first.Y) * pageHeight;
        return Math.Sqrt(x * x + y * y);
    }

    private static void ValidatePageDimensions(double width, double height)
    {
        if (!double.IsFinite(width) || width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "PDF page width must be finite and positive.");
        if (!double.IsFinite(height) || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "PDF page height must be finite and positive.");
    }

    private static void ValidateLimits(PdfTextLayerLimits limits)
    {
        if (limits.MaximumWordsPerPage <= 0 ||
            limits.MaximumCharactersPerPage <= 0 ||
            limits.MaximumCharactersPerWord <= 0 ||
            !double.IsFinite(limits.MinimumDimensionPoints) ||
            limits.MinimumDimensionPoints <= 0 ||
            !double.IsFinite(limits.MinimumHorizontalScalePercent) ||
            !double.IsFinite(limits.MaximumHorizontalScalePercent) ||
            limits.MinimumHorizontalScalePercent <= 0 ||
            limits.MaximumHorizontalScalePercent < limits.MinimumHorizontalScalePercent ||
            !double.IsFinite(limits.MaximumAbsoluteAngleDegrees) ||
            limits.MaximumAbsoluteAngleDegrees < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "PDF text-layer limits are invalid.");
        }
    }

    private readonly record struct ProjectedGeometry(
        double X,
        double Y,
        double Width,
        double Height,
        double AngleDegrees);
}
