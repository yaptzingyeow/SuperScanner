using System.Security.Cryptography;
using ImageMagick;
using ImageMagick.Drawing;
using SuperScanner.Domain.Ocr;
using SuperScanner.Domain.TextEditing;
using TextAlignment = SuperScanner.Domain.TextEditing.TextAlignment;

namespace SuperScanner.Infrastructure.TextEditing;

public sealed record TextEditRenderRequest(
    byte[] SourceBytes,
    IReadOnlyList<IReadOnlyList<OcrPoint>> SelectedPolygons,
    IReadOnlyList<IReadOnlyList<OcrPoint>> ProtectedPolygons,
    NormalizedBox ApprovedBox,
    string ReplacementText,
    TextEditStyle Style,
    string RendererVersion,
    string LayoutVersion);

public sealed record TextEditRenderResult(byte[]? Output, string? Sha256Hex,
    bool[]? ChangedPixelMask, string? FailureCode);

public interface ITextEditRenderer
{
    Task<TextEditRenderResult> RenderAsync(TextEditRenderRequest request, CancellationToken ct);
}

public interface ITextGlyphPainter
{
    void Paint(byte[] rgb, int width, int height, NormalizedBox box,
        string text, TextEditStyle style, string fontPath, TextLayoutResult fit);
}

public sealed class MagickGlyphPainter : ITextGlyphPainter
{
    public void Paint(byte[] rgb, int width, int height, NormalizedBox box,
        string text, TextEditStyle style, string fontPath, TextLayoutResult fit)
    {
        var fontPixels = fit.FontSize * height;
        var measured = new Drawables().Font(fontPath).FontPointSize(fontPixels)
            .TextKerning(fit.LetterSpacing * fontPixels).FontTypeMetrics(text)
            ?? throw new InvalidDataException("Text metrics are unavailable.");
        var tileWidth = Math.Max(1, (int)Math.Ceiling(fit.Width + 12));
        var tileHeight = Math.Max(1, (int)Math.Ceiling(fit.Height + 12));
        using var tile = new MagickImage(MagickColors.Transparent,
            (uint)tileWidth, (uint)tileHeight);
        new Drawables().Font(fontPath).FontPointSize(fontPixels)
            .TextKerning(fit.LetterSpacing * fontPixels)
            .FillColor(new MagickColor(style.ColorHex))
            .Text(6, 6 + measured.Ascent, text).Draw(tile);
        if (Math.Abs(style.AngleDegrees) > .001)
            tile.Rotate(style.AngleDegrees);

        var tilePixels = tile.GetPixels().ToByteArray(PixelMapping.RGBA)
            ?? throw new InvalidDataException("Text pixels are unavailable.");
        var boxLeft = box.X * width;
        var boxTop = box.Y * height;
        var boxWidth = box.Width * width;
        var boxHeight = box.Height * height;
        var originX = style.Alignment switch
        {
            TextAlignment.Left => boxLeft,
            TextAlignment.Right => boxLeft + boxWidth - tile.Width,
            _ => boxLeft + (boxWidth - tile.Width) / 2
        };
        var originY = boxTop + (boxHeight - tile.Height) * style.Baseline;
        var left = (int)Math.Round(originX);
        var top = (int)Math.Round(originY);
        for (var ty = 0; ty < tile.Height; ty++)
        for (var tx = 0; tx < tile.Width; tx++)
        {
            var tileOffset = ((int)(ty * tile.Width + tx)) * 4;
            var alpha = tilePixels[tileOffset + 3];
            if (alpha == 0) continue;
            var x = left + tx;
            var y = top + ty;
            if (x < 0 || y < 0 || x >= width || y >= height ||
                !Inside(x, y, width, height, box))
                throw new TextGlyphOverflowException();
            var outputOffset = (y * width + x) * 3;
            for (var channel = 0; channel < 3; channel++)
                rgb[outputOffset + channel] = (byte)((tilePixels[tileOffset + channel] * alpha +
                    rgb[outputOffset + channel] * (255 - alpha) + 127) / 255);
        }
    }

    private static bool Inside(int x, int y, int width, int height, NormalizedBox box) =>
        (x + .5) / width >= box.X && (x + .5) / width <= box.X + box.Width &&
        (y + .5) / height >= box.Y && (y + .5) / height <= box.Y + box.Height;
}

public sealed class TextGlyphOverflowException : Exception;

public sealed class TextEditRenderer(
    IFontCatalogue catalogue,
    string fontRoot,
    TextEditingOptions options,
    ITextGlyphPainter painter) : ITextEditRenderer
{
    public const string RendererVersion = "renderer-v1";

    public Task<TextEditRenderResult> RenderAsync(TextEditRenderRequest request,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request.RendererVersion != RendererVersion ||
            request.LayoutVersion != TextLayoutEngine.LayoutVersion ||
            request.SourceBytes is null or { Length: 0 or > 25_000_000 } ||
            string.IsNullOrWhiteSpace(request.ReplacementText) ||
            request.ReplacementText.Length > 4_000 ||
            request.ApprovedBox is null || request.Style is null ||
            request.SelectedPolygons is null || request.ProtectedPolygons is null)
            return Task.FromResult(Fail("text_edit_render_invalid"));

        FontCatalogueEntry font;
        try { font = catalogue.Get(request.Style.FontId, request.Style.FontVersion); }
        catch (KeyNotFoundException) { return Task.FromResult(Fail("text_edit_font_unavailable")); }
        var fontPath = Path.GetFullPath(Path.Combine(fontRoot, font.RendererAssetPath));
        var expectedRoot = Path.GetFullPath(Path.Combine(fontRoot, "assets/fonts")) +
            Path.DirectorySeparatorChar;
        if (!fontPath.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(Fail("text_edit_font_unavailable"));

        try
        {
            using var sourceImage = new MagickImage(request.SourceBytes);
            var width = (int)sourceImage.Width;
            var height = (int)sourceImage.Height;
            if (width <= 0 || height <= 0 || width > 20_000 || height > 20_000 ||
                (long)width * height > 40_000_000)
                return Task.FromResult(Fail("text_edit_render_invalid"));
            var source = sourceImage.GetPixels().ToByteArray(PixelMapping.RGB)
                ?? throw new InvalidDataException("Source pixels are unavailable.");
            var repaired = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
                source, width, height, request.SelectedPolygons, request.ProtectedPolygons,
                request.ApprovedBox, options.MaskDilationPixels));
            if (repaired.FailureCode is not null)
                return Task.FromResult(Fail(repaired.FailureCode));

            var fit = TextLayoutEngine.Fit(new TextLayoutRequest(
                request.ReplacementText, request.ApprovedBox, width, height,
                request.Style.FontSize, request.Style.LetterSpacing,
                options.MinimumLetterSpacing, options.MinimumFontScale),
                (text, pixels) =>
                {
                    var measure = new Drawables().Font(fontPath).FontPointSize(pixels)
                        .FontTypeMetrics(text)
                        ?? throw new InvalidDataException("Text metrics are unavailable.");
                    return new TextMeasurement(measure.TextWidth, measure.TextHeight);
                });
            if (!fit.Fits) return Task.FromResult(Fail("text_edit_overflow"));

            ct.ThrowIfCancellationRequested();
            var rendered = repaired.Pixels!;
            painter.Paint(rendered, width, height, request.ApprovedBox,
                request.ReplacementText, request.Style, fontPath, fit);
            var changedMask = new bool[width * height];
            var threshold = options.ContainmentTolerance * 255;
            for (var i = 0; i < changedMask.Length; i++)
            {
                var delta = Math.Max(Math.Abs(source[i * 3] - rendered[i * 3]),
                    Math.Max(Math.Abs(source[i * 3 + 1] - rendered[i * 3 + 1]),
                        Math.Abs(source[i * 3 + 2] - rendered[i * 3 + 2])));
                changedMask[i] = delta > 0;
                if (delta > 0 && !Inside(i % width, i / width,
                    width, height, request.ApprovedBox))
                    return Task.FromResult(Fail("text_edit_containment_failed"));
            }

            using var outputImage = new MagickImage(MagickColors.White,
                (uint)width, (uint)height);
            outputImage.ImportPixels(rendered,
                new PixelImportSettings((uint)width, (uint)height,
                    StorageType.Char, PixelMapping.RGB));
            outputImage.Strip();
            outputImage.Quality = 98;
            var jpeg = outputImage.ToByteArray(MagickFormat.Jpeg);
            using var decoded = new MagickImage(jpeg);
            var decodedRgb = decoded.GetPixels().ToByteArray(PixelMapping.RGB)
                ?? throw new InvalidDataException("Output pixels are unavailable.");
            for (var i = 0; i < changedMask.Length; i++)
            {
                if (Inside(i % width, i / width, width, height, request.ApprovedBox)) continue;
                for (var channel = 0; channel < 3; channel++)
                    if (Math.Abs(source[i * 3 + channel] - decodedRgb[i * 3 + channel]) > threshold)
                        return Task.FromResult(Fail("text_edit_containment_failed"));
            }
            var hash = Convert.ToHexString(SHA256.HashData(jpeg)).ToLowerInvariant();
            return Task.FromResult(new TextEditRenderResult(jpeg, hash, changedMask, null));
        }
        catch (TextGlyphOverflowException)
        {
            return Task.FromResult(Fail("text_edit_overflow"));
        }
        catch (MagickException)
        {
            return Task.FromResult(Fail("text_edit_render_invalid"));
        }
        catch (InvalidDataException)
        {
            return Task.FromResult(Fail("text_edit_render_invalid"));
        }
        catch (ArgumentException)
        {
            return Task.FromResult(Fail("text_edit_render_invalid"));
        }
    }

    private static TextEditRenderResult Fail(string code) => new(null, null, null, code);

    private static bool Inside(int x, int y, int width, int height, NormalizedBox box) =>
        (x + .5) / width >= box.X && (x + .5) / width <= box.X + box.Width &&
        (y + .5) / height >= box.Y && (y + .5) / height <= box.Y + box.Height;
}
