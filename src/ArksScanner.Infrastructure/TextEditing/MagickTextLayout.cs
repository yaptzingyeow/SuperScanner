using ImageMagick;
using ImageMagick.Drawing;
using ArksScanner.Domain.TextEditing;

namespace ArksScanner.Infrastructure.TextEditing;

// Shared by request preparation and the worker: measure the actual rotated ink,
// excluding transparent drawing padding, against the same integer pixel bounds.
internal static class MagickTextLayout
{
    public static TextLayoutResult Fit(string text, NormalizedBox box, int width, int height,
        TextEditStyle style, string fontPath, TextEditingOptions options, int faceWeight = 0)
    {
        var steps = faceWeight > 0 ? Math.Max(0, (style.Weight - faceWeight) / 100) : 0;
        var fit = TextLayoutEngine.Fit(new TextLayoutRequest(text, box, width, height,
            style.FontSize, style.LetterSpacing,
            style.LetterSpacing > .1 ? style.LetterSpacing : options.MinimumLetterSpacing,
            options.MinimumFontScale), (value, pixels) =>
        {
            // The box is the OCR ink box, so compare ink with ink: the font's full line height
            // (ascent + descent) is taller than the printed letters and would shrink every edit.
            using var ink = DrawInk(value, fontPath, pixels, 0, MagickColors.Black);
            return new TextMeasurement(ink.Width, ink.Height);
        }) with { SyntheticWeightSteps = steps };
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
            style.AngleDegrees, fit.SyntheticWeightSteps, style.Strikethrough);
    }

    // Each 100 of synthetic weight thickens strokes by this share of the font size per side.
    private const double SyntheticStrokePerStep = .009;

    /// <summary>The text's actual ink, trimmed of transparent padding (optionally rotated).</summary>
    private static MagickImage DrawInk(string text, string fontPath, double pixels, double kerning, MagickColor color,
        double angleDegrees = 0, int syntheticWeightSteps = 0, bool strikethrough = false)
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
            var drawing = new Drawables().Font(fontPath).FontPointSize(pixels)
                .TextKerning(kerning).FillColor(color);
            // Synthetic bold: outline the glyphs in their own colour, so a face can be drawn heavier.
            if (syntheticWeightSteps > 0)
                drawing = drawing.StrokeColor(color)
                    .StrokeWidth(2 * SyntheticStrokePerStep * syntheticWeightSteps * pixels);
            drawing.Text(padding, padding + metrics.Ascent, text).Draw(tile);
            if (strikethrough)
            {
                // Through the lower-case letters: about 0.3 em above the baseline, as in word processors.
                var thickness = Math.Max(1, pixels * (.06 + SyntheticStrokePerStep * syntheticWeightSteps));
                var middle = padding + metrics.Ascent - pixels * .3;
                new Drawables().FillColor(color).StrokeColor(MagickColors.Transparent)
                    .Rectangle(padding, middle - thickness / 2, padding + metrics.TextWidth, middle + thickness / 2)
                    .Draw(tile);
            }
            tile.BackgroundColor = MagickColors.Transparent;
            if (Math.Abs(angleDegrees) > .001) tile.Rotate(angleDegrees);
            tile.Trim();
            return tile;
        }
        catch { tile.Dispose(); throw; }
    }
}
