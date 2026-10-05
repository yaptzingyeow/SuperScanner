using ImageMagick;
using ImageMagick.Drawing;
using ArksScanner.Application.Abstractions;
using ArksScanner.Application.TextEditing;
using ArksScanner.Domain.Ocr;
using ArksScanner.Domain.TextEditing;

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
        var width = (int)image.Width;
        var height = (int)image.Height;
        if ((long)width * height > 40_000_000)
            throw new InvalidDataException("Text style source exceeds the pixel limit.");
        image.ColorSpace = ColorSpace.sRGB;
        image.Alpha(AlphaOption.Remove);

        var points = words.SelectMany(word => word.Polygon).ToArray();
        var x1 = points.Min(point => point.X);
        var x2 = points.Max(point => point.X);
        var y1 = points.Min(point => point.Y);
        var y2 = points.Max(point => point.Y);
        // Work on the selected words at full resolution, in the same pixels the edit is drawn on.
        var left = Math.Clamp((int)Math.Floor(x1 * width) - 2, 0, width - 1);
        var top = Math.Clamp((int)Math.Floor(y1 * height) - 2, 0, height - 1);
        var cropWidth = Math.Clamp((int)Math.Ceiling(x2 * width) + 2, left + 1, width) - left;
        var cropHeight = Math.Clamp((int)Math.Ceiling(y2 * height) + 2, top + 1, height) - top;
        using var crop = (MagickImage)image.CloneArea(left, top, (uint)cropWidth, (uint)cropHeight);
        var rgb = crop.GetPixels().ToByteArray(PixelMapping.RGB)
            ?? throw new InvalidDataException("The image pixels could not be read.");
        var background = BorderMedian(rgb, cropWidth, cropHeight, 0, 0, 1, 1);
        var foreground = ForegroundMedian(rgb, cropWidth, cropHeight, 0, 0, 1, 1, background);
        var contrast = foreground is null ? 0 :
            Math.Max(Math.Abs(foreground.Value.R - background.R),
                Math.Max(Math.Abs(foreground.Value.G - background.G),
                    Math.Abs(foreground.Value.B - background.B)));
        var color = foreground ?? (R: (byte)0, G: (byte)0, B: (byte)0);
        var colorHex = $"#{color.R:x2}{color.G:x2}{color.B:x2}";

        var angle = words.Average(word =>
            Math.Atan2((word.Polygon[1].Y - word.Polygon[0].Y) * height,
                (word.Polygon[1].X - word.Polygon[0].X) * width) * 180 / Math.PI);
        angle = Math.Clamp(angle, -45, 45);
        // OCR word text keeps trailing spaces and line breaks; compare against the printed phrase only.
        var phrase = string.Join(' ', words.Select(word => word.Text.Trim()).Where(text => text.Length > 0));
        var ink = foreground is null ? null
            : InkShape.Measure(rgb, cropWidth, cropHeight, Luma(background), Luma(foreground.Value));
        var (candidates, fontSize) = ink is { Height: > 2, Width: > 2 }
            ? RankByInk(phrase, ink, InkMask.From(rgb, cropWidth, ink, (Luma(background) + Luma(foreground!.Value)) / 2))
            : RankByBox(phrase, (x2 - x1) * width, Math.Clamp((y2 - y1) * height * 0.75, 3, 400));
        fontSize = Math.Clamp(fontSize, 3, 400);
        var bestScore = candidates.Count > 0 ? candidates[0].Score : 0;
        var confidence = contrast < 12 ? 0.15 :
            Math.Clamp(0.25 + 0.35 * Math.Min(contrast / 100.0, 1) + 0.2 * bestScore, 0, 0.8);
        var weight = candidates.Count > 0
            ? catalogue.Get(candidates[0].CatalogueId, candidates[0].Version).Weight
            : 400;
        return new TextStyleEstimate(candidates, confidence, colorHex, fontSize,
            weight, 0, angle, "left");
    }

    private const double ReferencePixels = 100;

    /// <summary>Scales each face to the observed ink height, then scores width and stroke weight.</summary>
    private (IReadOnlyList<FontCandidate> Candidates, double FontSize) RankByInk(string text, InkShape observed,
        InkMask observedMask)
    {
        var sample = text.Length > 40 ? text[..40] : text;
        var scored = new List<(FontCandidate Candidate, double Size)>();
        foreach (var (face, path) in SelectableFaces())
        {
            try
            {
                var rendered = RenderReference(sample, path);
                if (rendered is null || rendered.Value.Shape.Height <= 0) continue;
                var reference = rendered.Value.Shape;
                var scale = observed.Height / reference.Height;
                var widthError = Math.Abs(reference.Width * scale - observed.Width) / observed.Width;
                var weightError = Math.Abs(reference.Density - observed.Density) / Math.Max(observed.Density, .01);
                // Letter shapes decide between faces of similar width (e.g. Arial vs Calibri vs Poppins).
                var overlap = observedMask.Overlap(rendered.Value.Mask);
                // Scans are often stretched a few percent by perspective correction, so shape outweighs width.
                var score = 0.75 * overlap + 0.25 / (1 + 2 * widthError + weightError);
                scored.Add((new FontCandidate(face.CatalogueId, face.Version, score), ReferencePixels * scale));
            }
            catch (MagickException)
            {
                // A missing/unsupported face is never treated as an exact match.
            }
        }
        if (scored.Count == 0) return (Fallback(), observed.Height * 1.4);
        var ordered = scored.OrderByDescending(item => item.Candidate.Score)
            .ThenBy(item => item.Candidate.CatalogueId, StringComparer.Ordinal)
            .ThenBy(item => item.Candidate.Version, StringComparer.Ordinal).ToArray();
        return (DistinctFamiliesFirst(ordered.Select(item => item.Candidate).ToArray()), ordered[0].Size);
    }

    private static (InkShape Shape, InkMask Mask)? RenderReference(string text, string fontPath)
    {
        var metrics = new Drawables().Font(fontPath).FontPointSize(ReferencePixels).FontTypeMetrics(text);
        if (metrics is null) return null;
        var tileWidth = (int)Math.Ceiling(metrics.TextWidth) + 80;
        var tileHeight = (int)Math.Ceiling(ReferencePixels * 2) + 40;
        using var tile = new MagickImage(MagickColors.White, (uint)tileWidth, (uint)tileHeight);
        new Drawables().Font(fontPath).FontPointSize(ReferencePixels).FillColor(MagickColors.Black)
            .Text(40, 20 + metrics.Ascent, text).Draw(tile);
        var rgb = tile.GetPixels().ToByteArray(PixelMapping.RGB)
            ?? throw new InvalidDataException("The reference pixels could not be read.");
        var shape = InkShape.Measure(rgb, tileWidth, tileHeight, 255, 0);
        return shape is null ? null : (shape, InkMask.From(rgb, tileWidth, shape, 128));
    }

    private (IReadOnlyList<FontCandidate> Candidates, double FontSize) RankByBox(string text, double observedWidth, double fontSize)
    {
        var ranked = new List<FontCandidate>();
        foreach (var (face, path) in SelectableFaces())
        {
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
        if (ranked.Count == 0) return (Fallback(), fontSize);
        var ordered = ranked.OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.CatalogueId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Version, StringComparer.Ordinal).ToArray();
        return (DistinctFamiliesFirst(ordered), fontSize);
    }

    private IEnumerable<(FontCatalogueEntry Face, string Path)> SelectableFaces()
    {
        var expectedRoot = Path.GetFullPath(Path.Combine(fontRoot, "assets/fonts")) + Path.DirectorySeparatorChar;
        foreach (var face in catalogue.Entries.Where(entry => entry.Enabled && entry.SelectableForNewEdits))
        {
            var path = Path.GetFullPath(Path.Combine(fontRoot, face.RendererAssetPath));
            if (path.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase)) yield return (face, path);
        }
    }

    private IReadOnlyList<FontCandidate> Fallback() =>
        catalogue.Entries.Where(entry => entry.Enabled && entry.SelectableForNewEdits)
            .Select(entry => new FontCandidate(entry.CatalogueId, entry.Version, 0)).ToArray();

    /// <summary>Show distinct families first so the quick recommendations are useful.</summary>
    private static IReadOnlyList<FontCandidate> DistinctFamiliesFirst(FontCandidate[] ordered)
    {
        var firstByFamily = ordered.GroupBy(candidate => candidate.CatalogueId)
            .Select(group => group.First()).Take(3).ToArray();
        var chosen = firstByFamily.Select(candidate => (candidate.CatalogueId, candidate.Version)).ToHashSet();
        return firstByFamily.Concat(ordered.Where(candidate =>
            !chosen.Contains((candidate.CatalogueId, candidate.Version)))).ToArray();
    }

    private static double Luma((byte R, byte G, byte B) c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

    /// <summary>The ink of dark text: tight bounding box and how much of it is filled.</summary>
    private sealed record InkShape(int Left, int Top, double Width, double Height, double Density)
    {
        public static InkShape? Measure(byte[] rgb, int width, int height, double backgroundLuma, double inkLuma)
        {
            var threshold = (backgroundLuma + inkLuma) / 2;
            int left = width, top = height, right = -1, bottom = -1, count = 0;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var o = (y * width + x) * 3;
                if (0.299 * rgb[o] + 0.587 * rgb[o + 1] + 0.114 * rgb[o + 2] > threshold) continue;
                count++;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
            if (right < 0) return null;
            double w = right - left + 1, h = bottom - top + 1;
            return new InkShape(left, top, w, h, count / (w * h));
        }
    }

    /// <summary>The ink inside its bounding box, resampled to a fixed grid of coverage fractions so faces
    /// can be compared by shape (serifs, bowls and stroke contrast show up as differences).</summary>
    private sealed class InkMask
    {
        private const int GridWidth = 160, GridHeight = 40;
        private readonly double[] cells;
        private InkMask(double[] cells) => this.cells = cells;

        public static InkMask From(byte[] rgb, int width, InkShape ink, double threshold)
        {
            var cells = new double[GridWidth * GridHeight];
            for (var gy = 0; gy < GridHeight; gy++)
            for (var gx = 0; gx < GridWidth; gx++)
            {
                var x0 = ink.Left + (int)(gx * ink.Width / GridWidth);
                var x1 = ink.Left + Math.Max((int)((gx + 1) * ink.Width / GridWidth), (int)(gx * ink.Width / GridWidth) + 1);
                var y0 = ink.Top + (int)(gy * ink.Height / GridHeight);
                var y1 = ink.Top + Math.Max((int)((gy + 1) * ink.Height / GridHeight), (int)(gy * ink.Height / GridHeight) + 1);
                var dark = 0; var total = 0;
                for (var y = y0; y < y1; y++)
                for (var x = x0; x < x1; x++)
                {
                    var o = (y * width + x) * 3;
                    if (o + 2 >= rgb.Length) continue;
                    total++;
                    if (0.299 * rgb[o] + 0.587 * rgb[o + 1] + 0.114 * rgb[o + 2] <= threshold) dark++;
                }
                cells[gy * GridWidth + gx] = total == 0 ? 0 : dark / (double)total;
            }
            return new InkMask(cells);
        }

        /// <summary>Soft intersection over union of the two coverage grids (1 = identical shapes).</summary>
        public double Overlap(InkMask other)
        {
            double both = 0, either = 0;
            for (var i = 0; i < cells.Length; i++)
            {
                both += Math.Min(cells[i], other.cells[i]);
                either += Math.Max(cells[i], other.cells[i]);
            }
            return either == 0 ? 0 : both / either;
        }
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
        if (samples.Count == 0) return null;
        // Anti-aliased edges lighten the ink; the colour is the core of the strokes (darkest third).
        var core = samples.OrderBy(pixel => pixel.R + pixel.G + pixel.B)
            .Take(Math.Max(1, samples.Count / 3)).ToList();
        return Median(core);
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
