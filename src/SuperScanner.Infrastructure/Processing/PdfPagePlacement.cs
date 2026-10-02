namespace SuperScanner.Infrastructure.Processing;

public sealed record PdfPagePlacement(
    double PageWidth, double PageHeight,
    double ContentX, double ContentY, double ContentWidth, double ContentHeight)
{
    public static PdfPagePlacement Create(int pixelWidth, int pixelHeight, string layout)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        if (layout == "Original")
        {
            var width = pixelWidth * 72d / 96;
            var height = pixelHeight * 72d / 96;
            return new(width, height, 0, 0, width, height);
        }
        if (layout != "A4") throw new ArgumentException("Unsupported PDF page layout.", nameof(layout));
        var landscape = pixelWidth > pixelHeight;
        var pageWidth = (landscape ? 297d : 210d) * 72 / 25.4;
        var pageHeight = (landscape ? 210d : 297d) * 72 / 25.4;
        var margin = 5d * 72 / 25.4;
        var scale = Math.Min((pageWidth - 2 * margin) / pixelWidth,
            (pageHeight - 2 * margin) / pixelHeight);
        var contentWidth = pixelWidth * scale;
        var contentHeight = pixelHeight * scale;
        return new(pageWidth, pageHeight,
            (pageWidth - contentWidth) / 2, (pageHeight - contentHeight) / 2,
            contentWidth, contentHeight);
    }
}
