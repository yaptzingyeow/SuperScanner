using ImageMagick;
using ImageMagick.Drawing;
using ArksScanner.Domain.TextEditing;

namespace ArksScanner.Infrastructure.TextEditing;

// Shared by request preparation and the worker: measure the actual rotated ink,
// excluding transparent drawing padding, against the same integer pixel bounds.
internal static class MagickTextLayout
{
    public static TextLayoutResult Fit(string text, NormalizedBox box, int width, int height,
        TextEditStyle style, string fontPath, TextEditingOptions options)
    {
        var fit = TextLayoutEngine.Fit(new TextLayoutRequest(text, box, width, height,
            style.FontSize, style.LetterSpacing,
            style.LetterSpacing > .1 ? style.LetterSpacing : options.MinimumLetterSpacing,
            options.MinimumFontScale), (value, pixels) =>
        {
            // The box is the OCR ink box, so compare ink with ink: the font's full line height
            // (ascent + descent) is taller than the printed letters and would shrink every edit.
            using var ink = DrawInk(value, fontPath, pixels, 0, MagickColors.Black);
            return new TextMeasurement(ink.Width, ink.Height);
        });
        if (!fit.Fits) return fit;
        var bounds = PixelBounds(box, width, height);
        for (var attempt = 0; attempt < 12; attempt++)
        {
            using var ink = CreateInk(text, style, fontPath, fit, height);
            if (ink.Width <= bounds.Width && ink.Height <= bounds.Height)
                return fit with { Width = ink.Width, Height = ink.Height };
            var ratio = Math.Min(bounds.Width / (double)ink.Width, bounds.Height / (double)ink.Height);
            var scale = Math.Max(options.MinimumFontScale, fit.FontScale * Math.Min(.98, ratio * .99));
            if (scale >= fit.FontScale) return fit with { Fits = false, Overflow = true };
            fit = fit with { FontScale = scale, FontSize = style.FontSize * scale };
        }
        return fit with { Fits = false, Overflow = true };
    }

    public static (int Left, int Top, int Width, int Height) PixelBounds(NormalizedBox box, int width, int height)
    {
        var left = Math.Max(0, (int)Math.Ceiling(box.X * width - .5));
        var top = Math.Max(0, (int)Math.Ceiling(box.Y * height - .5));
        var right = Math.Min(width - 1, (int)Math.Floor((box.X + box.Width) * width - .5));
        var bottom = Math.Min(height - 1, (int)Math.Floor((box.Y + box.Height) * height - .5));
        return (left, top, Math.Max(0, right - left + 1), Math.Max(0, bottom - top + 1));
    }

    public static MagickImage CreateInk(string text, TextEditStyle style, string fontPath,
        TextLayoutResult fit, int height)
    {
        var pixels = fit.FontSize * height;
        return DrawInk(text, fontPath, pixels, fit.LetterSpacing * pixels, new MagickColor(style.ColorHex),
            style.AngleDegrees);
    }

    /// <summary>The text's actual ink, trimmed of transparent padding (optionally rotated).</summary>
    private static MagickImage DrawInk(string text, string fontPath, double pixels, double kerning, MagickColor color,
        double angleDegrees = 0)
    {
        var metrics = new Drawables().Font(fontPath).FontPointSize(pixels)
            .TextKerning(kerning).FontTypeMetrics(text)
            ?? throw new InvalidDataException("Text metrics are unavailable.");
        var padding = (int)Math.Ceiling(pixels) + 8;
        var tile = new MagickImage(MagickColors.Transparent,
            (uint)Math.Max(1, Math.Ceiling(metrics.TextWidth) + padding * 2),
            (uint)Math.Max(1, Math.Ceiling(metrics.TextHeight) + padding * 2));
        try
        {
            new Drawables().Font(fontPath).FontPointSize(pixels)
                .TextKerning(kerning).FillColor(color)
                .Text(padding, padding + metrics.Ascent, text).Draw(tile);
            tile.BackgroundColor = MagickColors.Transparent;
            if (Math.Abs(angleDegrees) > .001) tile.Rotate(angleDegrees);
            tile.Trim();
            return tile;
        }
        catch { tile.Dispose(); throw; }
    }
}
