using ImageMagick;
using ImageMagick.Drawing;
using PdfSharp.Drawing;

namespace ArksScanner.Infrastructure.Processing;

/// <summary>
/// The "Scanned with Arks Scanner" stamp drawn in the bottom-right corner of every exported PDF
/// page. It is placed as a small image rather than text, so it never becomes part of the
/// searchable text layer (search, copy and the searchable-page count ignore it).
/// </summary>
public static class PdfBrandWatermark
{
    public const string Text = "Scanned with Arks Scanner";

    /// <summary>Stamp height as a share of the page's shorter side (about 15 pt on A4).</summary>
    private const double HeightShare = .025;
    private const int Scale = 8; // render at 8× for crisp print output

    private static readonly object Gate = new();
    private static (string Font, byte[] Png)? cached;

    /// <summary>The stamp artwork as a transparent PNG (rendered once per font).</summary>
    public static byte[] ImagePng(string fontPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontPath);
        lock (Gate)
        {
            if (cached is not { } hit || hit.Font != fontPath) cached = (fontPath, Render(fontPath));
            return cached.Value.Png;
        }
    }

    public static void Draw(PdfSharp.Pdf.PdfPage page, string fontPath)
    {
        ArgumentNullException.ThrowIfNull(page);
        using var stream = new MemoryStream(ImagePng(fontPath), writable: false);
        using var image = XImage.FromStream(stream);
        var height = Math.Clamp(Math.Min(page.Width.Point, page.Height.Point) * HeightShare, 1, 48);
        var width = height * image.PixelWidth / image.PixelHeight;
        var margin = height * .7;
        using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        graphics.DrawImage(image, page.Width.Point - margin - width, page.Height.Point - margin - height, width, height);
    }

    /// <summary>A white rounded label with the Arks Scanner mark (Scan-A) and the product name.</summary>
    private static byte[] Render(string font)
    {
        const int height = 14 * Scale;
        const int badge = 10 * Scale;
        const int pad = 4 * Scale;
        var textSize = 7.2 * Scale;

        // Measure the text so the label fits it exactly.
        var metrics = new Drawables().Font(font).FontPointSize(textSize).FontTypeMetrics(Text)
            ?? throw new InvalidOperationException("Could not measure the watermark text.");
        var width = (int)Math.Ceiling(pad + badge + pad * .8 + metrics.TextWidth + pad);

        using var image = new MagickImage(MagickColors.Transparent, (uint)width, height);
        new Drawables()
            // label: white, slightly translucent, thin grey border
            .FillColor(new MagickColor(255, 255, 255, 225)).StrokeColor(new MagickColor(30, 107, 80, 90))
            .StrokeWidth(Scale * .5)
            .RoundRectangle(Scale * .5, Scale * .5, width - Scale * .5, height - Scale * .5, height / 2.0, height / 2.0)
            // mark: green tile, white "A", mint scan beam as its crossbar (64-unit logo grid)
            .StrokeColor(MagickColors.Transparent).FillColor(new MagickColor(30, 107, 80, 255))
            .RoundRectangle(pad, (height - badge) / 2.0, pad + badge, (height + badge) / 2.0, badge * 15 / 64.0, badge * 15 / 64.0)
            .FillColor(MagickColors.Transparent).StrokeColor(MagickColors.White).StrokeWidth(badge * 5.4 / 64)
            .StrokeLineCap(LineCap.Round).StrokeLineJoin(LineJoin.Round)
            .Polyline(Mark(pad, (height - badge) / 2.0, badge, 21, 47), Mark(pad, (height - badge) / 2.0, badge, 32, 18),
                Mark(pad, (height - badge) / 2.0, badge, 43, 47))
            .StrokeColor(MagickColors.Transparent).FillColor(new MagickColor(93, 242, 198, 255))
            .RoundRectangle(pad + badge * 15 / 64.0, (height - badge) / 2.0 + badge * 35 / 64.0,
                pad + badge * 49 / 64.0, (height - badge) / 2.0 + badge * 38.4 / 64.0, badge * 1.7 / 64, badge * 1.7 / 64)
            // text
            .Font(font).FontPointSize(textSize).FillColor(new MagickColor(43, 53, 48, 255))
            .Text(pad + badge + pad * .8, height / 2.0 + (metrics.Ascent + metrics.Descent) / 2, Text)
            .Draw(image);
        return image.ToByteArray(MagickFormat.Png32);
    }

    private static PointD Mark(double left, double top, double size, double x, double y) =>
        new(left + size * x / 64, top + size * y / 64);
}
