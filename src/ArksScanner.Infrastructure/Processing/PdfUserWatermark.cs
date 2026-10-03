using ImageMagick;
using ImageMagick.Drawing;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using ArksScanner.Domain.Documents;

namespace ArksScanner.Infrastructure.Processing;

/// <summary>
/// Draws a user's own watermark ("FOR GOVERNMENT USE ONLY") on an export page, once or repeated
/// across the page. The text is placed as a transparent image so it is never part of the
/// searchable text layer and cannot be copied or edited out as text.
/// </summary>
public static class PdfUserWatermark
{
    /// <summary>Upper bound on repeated copies per page, keeping files small and rendering fast.</summary>
    public const int MaximumTiles = 300;
    private const double PixelsPerPoint = 4;
    private const int MaximumImageWidth = 6000;

    private static readonly IReadOnlyDictionary<string, (string Regular, string? Bold)> FontFiles =
        new Dictionary<string, (string, string?)>(StringComparer.Ordinal)
        {
            ["noto-sans"] = ("NotoSans-Regular.ttf", "NotoSans-Bold.ttf"),
            ["noto-serif"] = ("NotoSerif-Regular.ttf", "NotoSerif-Bold.ttf"),
            ["liberation-sans"] = ("LiberationSans-Regular.ttf", "LiberationSans-Bold.ttf"),
            ["liberation-serif"] = ("LiberationSerif-Regular.ttf", "LiberationSerif-Bold.ttf"),
            ["carlito"] = ("Carlito-Regular.ttf", "Carlito-Bold.ttf"),
            ["poppins"] = ("Poppins-Regular.ttf", "Poppins-Bold.ttf"),
            ["lato"] = ("Lato-Regular.ttf", "Lato-Bold.ttf"),
            ["liberation-mono"] = ("LiberationMono-Regular.ttf", "LiberationMono-Bold.ttf"),
            ["oswald"] = ("Oswald-Regular.ttf", null),
            ["montserrat"] = ("Montserrat-Regular.ttf", null),
            ["caveat"] = ("Caveat-Regular.ttf", null),
            ["dancing-script"] = ("DancingScript-Regular.ttf", null),
        };

    /// <summary>The font file for a watermark (the bold face when chosen and available).</summary>
    public static string FontPath(ExportWatermark watermark, string fontsDirectory)
    {
        ArgumentNullException.ThrowIfNull(watermark);
        var files = FontFiles[watermark.FontId];
        return Path.Combine(fontsDirectory, watermark.Bold && files.Bold is not null ? files.Bold : files.Regular);
    }

    public static void Draw(PdfPage page, ExportWatermark watermark, string fontsDirectory)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(watermark);
        var pageWidth = page.Width.Point;
        var pageHeight = page.Height.Point;
        var textPoints = Math.Min(pageWidth, pageHeight) * watermark.SizePercent / 100;

        var png = Render(watermark, FontPath(watermark, fontsDirectory), textPoints, out var pixelWidth, out var pixelHeight);
        var scale = textPoints / (pixelHeight / 1.35); // the image is 1.35 text heights tall
        var width = pixelWidth * scale;
        var height = pixelHeight * scale;

        using var stream = new MemoryStream(png, writable: false);
        using var image = XImage.FromStream(stream);
        using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

        if (watermark.Layout == "Single")
        {
            var centerY = watermark.Position switch
            {
                "Top" => pageHeight * .15,
                "Bottom" => pageHeight * .85,
                _ => pageHeight / 2,
            };
            var state = graphics.Save();
            graphics.TranslateTransform(pageWidth / 2, centerY);
            graphics.RotateTransform(-watermark.AngleDegrees);
            graphics.DrawImage(image, -width / 2, -height / 2, width, height);
            graphics.Restore(state);
            return;
        }

        // Tiled: rows of copies in a brick pattern, rotated about the page centre and large enough
        // to cover every corner of the page at any angle.
        var gap = textPoints * watermark.Spacing;
        var stepX = width + gap * 1.5;
        var stepY = height + gap;
        var radius = Math.Sqrt(pageWidth * pageWidth + pageHeight * pageHeight) / 2 + Math.Max(width, height);
        var columns = (int)Math.Ceiling(2 * radius / stepX) + 1;
        var rows = (int)Math.Ceiling(2 * radius / stepY) + 1;
        while ((long)columns * rows > MaximumTiles)
        {
            stepX *= 1.15;
            stepY *= 1.15;
            columns = (int)Math.Ceiling(2 * radius / stepX) + 1;
            rows = (int)Math.Ceiling(2 * radius / stepY) + 1;
        }

        var saved = graphics.Save();
        graphics.TranslateTransform(pageWidth / 2, pageHeight / 2);
        graphics.RotateTransform(-watermark.AngleDegrees);
        for (var row = 0; row < rows; row++)
        {
            var y = -radius + row * stepY;
            var offset = row % 2 == 0 ? 0 : stepX / 2;
            for (var column = 0; column < columns; column++)
            {
                var x = -radius - stepX / 2 + offset + column * stepX;
                graphics.DrawImage(image, x - width / 2, y - height / 2, width, height);
            }
        }
        graphics.Restore(saved);
    }

    /// <summary>Renders the text in its colour and opacity on a transparent background.</summary>
    private static byte[] Render(ExportWatermark watermark, string font, double textPoints, out int width, out int height)
    {
        var pixelsPerPoint = PixelsPerPoint;
        var fontPixels = textPoints * pixelsPerPoint;
        var metrics = new Drawables().Font(font).FontPointSize(fontPixels).FontTypeMetrics(watermark.Text)
            ?? throw new InvalidOperationException("Could not measure the watermark text.");
        // Keep very long or large text within a sane bitmap size.
        if (metrics.TextWidth + fontPixels > MaximumImageWidth)
        {
            var shrink = MaximumImageWidth / (metrics.TextWidth + fontPixels);
            fontPixels *= shrink;
            metrics = new Drawables().Font(font).FontPointSize(fontPixels).FontTypeMetrics(watermark.Text)!;
        }

        width = (int)Math.Ceiling(metrics.TextWidth + fontPixels * .4);
        height = (int)Math.Ceiling(fontPixels * 1.35);
        var color = MagickColor.FromRgb(Convert.ToByte(watermark.Color[1..3], 16),
            Convert.ToByte(watermark.Color[3..5], 16), Convert.ToByte(watermark.Color[5..7], 16));
        color.A = (byte)Math.Round(watermark.Opacity * 255);
        using var image = new MagickImage(MagickColors.Transparent, (uint)width, (uint)height);
        new Drawables()
            .Font(font).FontPointSize(fontPixels).FillColor(color)
            .TextAlignment(TextAlignment.Center)
            .Text(width / 2.0, height / 2.0 + (metrics.Ascent + metrics.Descent) / 2, watermark.Text)
            .Draw(image);
        return image.ToByteArray(MagickFormat.Png32);
    }
}
