using ImageMagick;
using ImageMagick.Drawing;
using ArksScanner.Application.Abstractions;
using ArksScanner.Application.TextEditing;
using ArksScanner.Domain.Ocr;

namespace ArksScanner.Infrastructure.TextEditing;

public sealed class TextStyleEstimator(
    IObjectStore store,
    IFontCatalogue catalogue,
    string fontRoot) : ITextStyleEstimator
{
    private const int MaximumInputBytes = 25 * 1024 * 1024;

    public async Task<TextStyleEstimate> EstimateAsync(string sourceObjectKey,
        IReadOnlyList<OcrElement> words, CancellationToken ct)
    {
        if (words.Count == 0) throw new ArgumentException("At least one word is required.", nameof(words));
        await using var source = await store.OpenReadAsync(sourceObjectKey, ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaximumInputBytes)
                throw new InvalidDataException("Text style source exceeds the input limit.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        buffer.Position = 0;
        using var image = new MagickImage(buffer);
        var originalWidth = image.Width;
        var originalHeight = image.Height;
        if ((long)originalWidth * originalHeight > 40_000_000)
            throw new InvalidDataException("Text style source exceeds the pixel limit.");
        image.ColorSpace = ColorSpace.sRGB;
        image.Alpha(AlphaOption.Remove);
        image.Resize(new MagickGeometry(1024, 1024) { Greater = true });

        var points = words.SelectMany(word => word.Polygon).ToArray();
        var x1 = points.Min(point => point.X);
        var x2 = points.Max(point => point.X);
        var y1 = points.Min(point => point.Y);
        var y2 = points.Max(point => point.Y);
        var rgb = image.GetPixels().ToByteArray(PixelMapping.RGB)
            ?? throw new InvalidDataException("The image pixels could not be read.");
        var background = BorderMedian(rgb, (int)image.Width, (int)image.Height, x1, y1, x2, y2);
        var foreground = ForegroundMedian(rgb, (int)image.Width, (int)image.Height,
            x1, y1, x2, y2, background);
        var contrast = foreground is null ? 0 :
            Math.Max(Math.Abs(foreground.Value.R - background.R),
                Math.Max(Math.Abs(foreground.Value.G - background.G),
                    Math.Abs(foreground.Value.B - background.B)));
        var color = foreground ?? (R: (byte)0, G: (byte)0, B: (byte)0);
        var colorHex = $"#{color.R:x2}{color.G:x2}{color.B:x2}";

        var fontSize = Math.Clamp((y2 - y1) * originalHeight * 0.75, 3, 144);
        var angle = words.Average(word =>
            Math.Atan2((word.Polygon[1].Y - word.Polygon[0].Y) * originalHeight,
                (word.Polygon[1].X - word.Polygon[0].X) * originalWidth) * 180 / Math.PI);
        angle = Math.Clamp(angle, -45, 45);
        var phrase = string.Join(' ', words.Select(word => word.Text));
        var observedWidth = (x2 - x1) * originalWidth;
        var candidates = RankFonts(phrase, observedWidth, fontSize);
        var bestScore = candidates.Count > 0 ? candidates[0].Score : 0;
        var confidence = contrast < 12 ? 0.15 :
            Math.Clamp(0.25 + 0.35 * Math.Min(contrast / 100.0, 1) + 0.2 * bestScore, 0, 0.8);
        var weight = candidates.Count > 0
            ? catalogue.Get(candidates[0].CatalogueId, candidates[0].Version).Weight
            : 400;
        return new TextStyleEstimate(candidates, confidence, colorHex, fontSize,
            weight, 0, angle, "left");
    }

    private IReadOnlyList<FontCandidate> RankFonts(string text, double observedWidth, double fontSize)
    {
        var ranked = new List<FontCandidate>();
        foreach (var face in catalogue.Entries.Where(entry => entry.Enabled && entry.SelectableForNewEdits))
        {
            var path = Path.GetFullPath(Path.Combine(fontRoot, face.RendererAssetPath));
            var expectedRoot = Path.GetFullPath(Path.Combine(fontRoot, "assets/fonts")) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var metrics = new Drawables().Font(path).FontPointSize(fontSize).FontTypeMetrics(text);
                if (metrics is null) continue;
                var relativeError = Math.Abs(metrics.TextWidth - observedWidth) / Math.Max(observedWidth, 1);
                ranked.Add(new FontCandidate(face.CatalogueId, face.Version, 1 / (1 + relativeError)));
            }
            catch (MagickException)
            {
                // A missing/unsupported face is never treated as an exact match.
            }
        }
        if (ranked.Count == 0)
        {
            ranked.AddRange(catalogue.Entries.Where(entry => entry.Enabled && entry.SelectableForNewEdits)
                .Select(entry => new FontCandidate(entry.CatalogueId, entry.Version, 0)));
        }
        var ordered = ranked.OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.CatalogueId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Version, StringComparer.Ordinal).ToArray();
        // Show distinct families first so the quick recommendations are useful.
        var firstByFamily = ordered.GroupBy(candidate => candidate.CatalogueId)
            .Select(group => group.First()).Take(3).ToArray();
        var chosen = firstByFamily.Select(candidate => (candidate.CatalogueId, candidate.Version))
            .ToHashSet();
        return firstByFamily.Concat(ordered.Where(candidate =>
            !chosen.Contains((candidate.CatalogueId, candidate.Version)))).ToArray();
    }

    private static (byte R, byte G, byte B) BorderMedian(byte[] rgb, int width, int height,
        double x1, double y1, double x2, double y2)
    {
        var samples = new List<(byte R, byte G, byte B)>();
        var left = Math.Clamp((int)(x1 * width) - 3, 0, width - 1);
        var right = Math.Clamp((int)(x2 * width) + 3, 0, width - 1);
        var top = Math.Clamp((int)(y1 * height) - 3, 0, height - 1);
        var bottom = Math.Clamp((int)(y2 * height) + 3, 0, height - 1);
        for (var x = left; x <= right; x += Math.Max(1, (right - left) / 32))
        {
            samples.Add(Read(rgb, width, x, top));
            samples.Add(Read(rgb, width, x, bottom));
        }
        return Median(samples);
    }

    private static (byte R, byte G, byte B)? ForegroundMedian(byte[] rgb, int width, int height,
        double x1, double y1, double x2, double y2, (byte R, byte G, byte B) background)
    {
        var samples = new List<(byte R, byte G, byte B)>();
        var left = Math.Clamp((int)(x1 * width), 0, width - 1);
        var right = Math.Clamp((int)(x2 * width), 0, width - 1);
        var top = Math.Clamp((int)(y1 * height), 0, height - 1);
        var bottom = Math.Clamp((int)(y2 * height), 0, height - 1);
        var stride = Math.Max(1, (right - left + 1) * (bottom - top + 1) / 10_000);
        for (var y = top; y <= bottom; y++)
        for (var x = left; x <= right; x += stride)
        {
            var pixel = Read(rgb, width, x, y);
            var difference = Math.Max(Math.Abs(pixel.R - background.R),
                Math.Max(Math.Abs(pixel.G - background.G), Math.Abs(pixel.B - background.B)));
            if (difference >= 12 && pixel.R + pixel.G + pixel.B <
                background.R + background.G + background.B)
                samples.Add(pixel);
        }
        return samples.Count == 0 ? null : Median(samples);
    }

    private static (byte R, byte G, byte B) Read(byte[] rgb, int width, int x, int y)
    {
        var offset = (y * width + x) * 3;
        return (rgb[offset], rgb[offset + 1], rgb[offset + 2]);
    }

    private static (byte R, byte G, byte B) Median(List<(byte R, byte G, byte B)> samples)
    {
        if (samples.Count == 0) return (255, 255, 255);
        var middle = samples.Count / 2;
        return (samples.Select(sample => sample.R).Order().ElementAt(middle),
            samples.Select(sample => sample.G).Order().ElementAt(middle),
            samples.Select(sample => sample.B).Order().ElementAt(middle));
    }
}
