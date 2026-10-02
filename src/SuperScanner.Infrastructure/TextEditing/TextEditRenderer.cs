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
    bool[]? ChangedPixelMask, string? FailureCode, string? DiagnosticReason = null);

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
        using var tile = MagickTextLayout.CreateInk(text, style, fontPath, fit, height);
        var tilePixels = tile.GetPixels().ToByteArray(PixelMapping.RGBA)
            ?? throw new InvalidDataException("Text pixels are unavailable.");
        var bounds = MagickTextLayout.PixelBounds(box, width, height);
        if (tile.Width > bounds.Width || tile.Height > bounds.Height)
            throw new TextGlyphOverflowException();
        var boxLeft = bounds.Left;
        var boxTop = bounds.Top;
        var boxWidth = bounds.Width;
        var boxHeight = bounds.Height;
        var originX = style.Alignment switch
        {
            TextAlignment.Left => boxLeft,
            TextAlignment.Right => boxLeft + boxWidth - tile.Width,
            _ => boxLeft + (boxWidth - tile.Width) / 2.0
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
    public const string RendererVersion = "renderer-v2";

    public Task<TextEditRenderResult> RenderAsync(TextEditRenderRequest request,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request.RendererVersion != RendererVersion ||
            request.LayoutVersion != TextLayoutEngine.LayoutVersion ||
            request.SourceBytes is null or { Length: 0 or > 25_000_000 } ||
            request.ReplacementText is null || request.SelectedPolygons is null ||
            request.ProtectedPolygons is null ||
            (request.SelectedPolygons.Count == 0 && string.IsNullOrWhiteSpace(request.ReplacementText)) ||
            request.ReplacementText.Length > 4_000 ||
            request.ApprovedBox is null || request.Style is null)
            return Task.FromResult(Fail("text_edit_render_invalid"));

        FontCatalogueEntry font;
        try { font = catalogue.Get(request.Style.FontId, request.Style.FontVersion); }
        catch (KeyNotFoundException) { return Task.FromResult(Fail("text_edit_font_unavailable")); }
        if (!font.SupportsWeight(request.Style.Weight))
            return Task.FromResult(Fail("text_edit_font_unavailable"));
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
            var adding = request.SelectedPolygons.Count == 0;
            var deleting = string.IsNullOrWhiteSpace(request.ReplacementText);
            var clearBox = adding ? request.ApprovedBox :
                SelectedClearBox(request.SelectedPolygons, width, height);
            var repaired = adding
                ? new BackgroundReconstructionResult((byte[])source.Clone(),
                    new bool[width * height], null,
                    PreservedLineMask: new bool[width * height])
                : BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
                    source, width, height, request.SelectedPolygons, request.ProtectedPolygons,
                    clearBox, options.MaskDilationPixels));
            if (repaired.FailureCode is not null)
                return Task.FromResult(new TextEditRenderResult(null, null, null,
                    repaired.FailureCode, repaired.DiagnosticReason));

            var placementBox = deleting ? request.ApprovedBox : AvoidHorizontalRules(request.ApprovedBox,
                repaired.PreservedLineMask!, width, height);
            if (placementBox is null) return Task.FromResult(Fail("text_edit_overflow"));
            TextLayoutResult? fit = null;
            if (!deleting)
            {
                fit = MagickTextLayout.Fit(request.ReplacementText, placementBox,
                    width, height, request.Style, fontPath, options);
                if (!fit.Fits) return Task.FromResult(Fail("text_edit_overflow"));
            }

            ct.ThrowIfCancellationRequested();
            var rendered = repaired.Pixels!;
            if (!deleting)
                painter.Paint(rendered, width, height, placementBox,
                    request.ReplacementText, request.Style, fontPath, fit!);
            var protectedPixels = adding ? [] : BackgroundReconstructor.Rasterize(
                request.ProtectedPolygons, width, height);
            for (var i = 0; i < protectedPixels.Length; i++)
            {
                if (!protectedPixels[i] && repaired.PreservedLineMask?[i] != true) continue;
                for (var channel = 0; channel < 3; channel++)
                    if (rendered[i * 3 + channel] != source[i * 3 + channel])
                        return Task.FromResult(Fail("text_edit_placement_overlap"));
            }
            var changedMask = new bool[width * height];
            for (var i = 0; i < changedMask.Length; i++)
            {
                var delta = Math.Max(Math.Abs(source[i * 3] - rendered[i * 3]),
                    Math.Max(Math.Abs(source[i * 3 + 1] - rendered[i * 3 + 1]),
                        Math.Abs(source[i * 3 + 2] - rendered[i * 3 + 2])));
                changedMask[i] = delta > 0;
                // The repair mask may reach glyph parts the OCR box missed;
                // every other change must stay inside the two boxes.
                if (delta > 0 && !Inside(i % width, i / width,
                    width, height, request.ApprovedBox) &&
                    !Inside(i % width, i / width, width, height, clearBox) &&
                    repaired.RepairMask?[i] != true)
                    return Task.FromResult(Fail("text_edit_containment_failed"));
            }

            using var outputImage = new MagickImage(MagickColors.White,
                (uint)width, (uint)height);
            outputImage.ImportPixels(rendered,
                new PixelImportSettings((uint)width, (uint)height,
                    StorageType.Char, PixelMapping.RGB));
            outputImage.Strip();
            // JPEG would alter pixels throughout the page even when the edit
            // is confined to one box. PNG preserves every pixel outside it.
            var png = outputImage.ToByteArray(MagickFormat.Png);
            if (png.Length > 25_000_000)
                return Task.FromResult(Fail("text_edit_render_invalid"));
            using var decoded = new MagickImage(png);
            var decodedRgb = decoded.GetPixels().ToByteArray(PixelMapping.RGB)
                ?? throw new InvalidDataException("Output pixels are unavailable.");
            for (var i = 0; i < changedMask.Length; i++)
            {
                if (Inside(i % width, i / width, width, height, request.ApprovedBox) ||
                    Inside(i % width, i / width, width, height, clearBox) ||
                    repaired.RepairMask?[i] == true) continue;
                for (var channel = 0; channel < 3; channel++)
                    if (source[i * 3 + channel] != decodedRgb[i * 3 + channel])
                        return Task.FromResult(Fail("text_edit_containment_failed"));
            }
            var hash = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();
            return Task.FromResult(new TextEditRenderResult(png, hash, changedMask, null));
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

    private static NormalizedBox? AvoidHorizontalRules(NormalizedBox box,
        bool[] lines, int width, int height)
    {
        var bounds = MagickTextLayout.PixelBounds(box, width, height);
        var bestTop = bounds.Top;
        var bestHeight = 0;
        var runTop = bounds.Top;
        var foundRule = false;
        for (var y = bounds.Top; y <= bounds.Top + bounds.Height; y++)
        {
            var blocked = y == bounds.Top + bounds.Height ||
                lines.AsSpan(y * width + bounds.Left, bounds.Width).Contains(true);
            if (!blocked) continue;
            if (y < bounds.Top + bounds.Height) foundRule = true;
            if (y - runTop > bestHeight)
            {
                bestTop = runTop;
                bestHeight = y - runTop;
            }
            runTop = y + 1;
        }
        if (!foundRule) return box;
        if (bestHeight < 2) return null;
        return new NormalizedBox(box.X, bestTop / (double)height,
            box.Width, bestHeight / (double)height);
    }

    private static TextEditRenderResult Fail(string code) => new(null, null, null, code);

    private static NormalizedBox SelectedClearBox(
        IReadOnlyList<IReadOnlyList<OcrPoint>> polygons, int width, int height)
    {
        var points = polygons.SelectMany(polygon => polygon).ToArray();
        if (points.Length == 0)
            return new NormalizedBox(0, 0, 1, 1);
        var left = Math.Max(0, points.Min(point => point.X) - 2.0 / width);
        var top = Math.Max(0, points.Min(point => point.Y) - 2.0 / height);
        var right = Math.Min(1, points.Max(point => point.X) + 2.0 / width);
        var bottom = Math.Min(1, points.Max(point => point.Y) + 2.0 / height);
        return new NormalizedBox(left, top, right - left, bottom - top);
    }

    private static bool Inside(int x, int y, int width, int height, NormalizedBox box) =>
        (x + .5) / width >= box.X && (x + .5) / width <= box.X + box.Width &&
        (y + .5) / height >= box.Y && (y + .5) / height <= box.Y + box.Height;
}
