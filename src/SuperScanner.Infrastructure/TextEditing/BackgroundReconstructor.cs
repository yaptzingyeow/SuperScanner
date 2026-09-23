using SuperScanner.Domain.Ocr;
using SuperScanner.Domain.TextEditing;

namespace SuperScanner.Infrastructure.TextEditing;

public sealed record BackgroundReconstructionRequest(
    byte[] SourceRgb,
    int Width,
    int Height,
    IReadOnlyList<IReadOnlyList<OcrPoint>> SelectedPolygons,
    IReadOnlyList<IReadOnlyList<OcrPoint>> ProtectedPolygons,
    NormalizedBox ApprovedBox,
    int DilationPixels);

public sealed record BackgroundReconstructionResult(byte[]? Pixels,
    bool[]? RepairMask, string? FailureCode);

public static class BackgroundReconstructor
{
    private const string Unsafe = "text_edit_unsafe_background";

    public static BackgroundReconstructionResult Reconstruct(BackgroundReconstructionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SourceRgb);
        ArgumentNullException.ThrowIfNull(request.ApprovedBox);
        ArgumentNullException.ThrowIfNull(request.SelectedPolygons);
        ArgumentNullException.ThrowIfNull(request.ProtectedPolygons);
        if (request.Width <= 0 || request.Height <= 0 ||
            (long)request.Width * request.Height > 40_000_000 ||
            request.SourceRgb.Length != (long)request.Width * request.Height * 3 ||
            request.DilationPixels is < 0 or > 32 || request.SelectedPolygons.Count == 0 ||
            request.SelectedPolygons.Any(polygon => !Valid(polygon)) ||
            request.ProtectedPolygons.Any(polygon => !Valid(polygon)))
            return Fail();

        var width = request.Width;
        var height = request.Height;
        var selected = Rasterize(request.SelectedPolygons, width, height);
        var protectedPixels = Rasterize(request.ProtectedPolygons, width, height);
        var approved = new bool[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            approved[y * width + x] = InsideBox(x, y, width, height, request.ApprovedBox);

        // Refuse a replacement box that could cover neighbouring recognized content.
        for (var i = 0; i < approved.Length; i++)
            if (approved[i] && protectedPixels[i]) return Fail();

        var minX = width;
        var maxX = -1;
        for (var i = 0; i < selected.Length; i++)
        {
            if (!selected[i]) continue;
            if (!approved[i]) return Fail();
            minX = Math.Min(minX, i % width);
            maxX = Math.Max(maxX, i % width);
        }
        if (maxX < minX || minX < 2 || maxX + 2 >= width) return Fail();

        var source = request.SourceRgb;
        var foreground = new bool[selected.Length];
        var background = new byte[height * 3];
        var foundForeground = false;
        for (var y = 0; y < height; y++)
        {
            var rowStart = y * width;
            if (!selected.AsSpan(rowStart, width).Contains(true)) continue;
            var left = (rowStart + minX - 2) * 3;
            var right = (rowStart + maxX + 2) * 3;
            var leftBrightness = Brightness(source, left);
            var rightBrightness = Brightness(source, right);
            if (leftBrightness < 120 || rightBrightness < 120 ||
                Math.Abs(leftBrightness - rightBrightness) > 35)
                return Fail();
            var borderLow = 255;
            var borderHigh = 0;
            for (var distance = 1; distance <= 5; distance++)
            {
                var leftX = minX - distance;
                var rightX = maxX + distance;
                if (leftX < 0 || rightX >= width) continue;
                var leftSample = Brightness(source, (rowStart + leftX) * 3);
                var rightSample = Brightness(source, (rowStart + rightX) * 3);
                borderLow = Math.Min(borderLow, Math.Min(leftSample, rightSample));
                borderHigh = Math.Max(borderHigh, Math.Max(leftSample, rightSample));
            }
            if (borderHigh - borderLow > 24) return Fail();
            for (var channel = 0; channel < 3; channel++)
                background[y * 3 + channel] = (byte)((source[left + channel] +
                    source[right + channel]) / 2);
            var paper = Brightness(background, y * 3);
            for (var x = minX; x <= maxX; x++)
            {
                var index = rowStart + x;
                if (!selected[index]) continue;
                var value = Brightness(source, index * 3);
                if (paper - value > 24)
                {
                    foreground[index] = true;
                    foundForeground = true;
                }
                else if (value - paper > 24)
                    return Fail();
            }
        }
        if (!foundForeground) return Fail();

        var repair = new bool[foreground.Length];
        for (var index = 0; index < foreground.Length; index++)
        {
            if (!foreground[index]) continue;
            var cx = index % width;
            var cy = index / width;
            for (var dy = -request.DilationPixels; dy <= request.DilationPixels; dy++)
            for (var dx = -request.DilationPixels; dx <= request.DilationPixels; dx++)
            {
                if (dx * dx + dy * dy > request.DilationPixels * request.DilationPixels) continue;
                var x = cx + dx;
                var y = cy + dy;
                if (x < 0 || y < 0 || x >= width || y >= height) continue;
                var target = y * width + x;
                if (approved[target] && !protectedPixels[target]) repair[target] = true;
            }
        }

        var output = (byte[])source.Clone();
        for (var i = 0; i < repair.Length; i++)
        {
            if (!repair[i]) continue;
            var row = i / width;
            // If dilation reaches a row without selected glyphs, use the nearest
            // sampled row rather than making a dark stripe across the paper.
            var sampleRow = row;
            while (sampleRow > 0 && background[sampleRow * 3] == 0) sampleRow--;
            if (background[sampleRow * 3] == 0) return Fail();
            for (var channel = 0; channel < 3; channel++)
                output[i * 3 + channel] = background[sampleRow * 3 + channel];
        }
        return new BackgroundReconstructionResult(output, repair, null);
    }

    private static BackgroundReconstructionResult Fail() => new(null, null, Unsafe);

    private static bool Valid(IReadOnlyList<OcrPoint> polygon) => polygon is { Count: 4 } &&
        polygon.All(point => double.IsFinite(point.X) && double.IsFinite(point.Y) &&
            point.X is >= 0 and <= 1 && point.Y is >= 0 and <= 1);

    private static int Brightness(byte[] pixels, int start) =>
        (pixels[start] * 299 + pixels[start + 1] * 587 + pixels[start + 2] * 114) / 1000;

    private static bool InsideBox(int x, int y, int width, int height, NormalizedBox box) =>
        (x + .5) / width >= box.X && (x + .5) / width <= box.X + box.Width &&
        (y + .5) / height >= box.Y && (y + .5) / height <= box.Y + box.Height;

    private static bool[] Rasterize(IReadOnlyList<IReadOnlyList<OcrPoint>> polygons,
        int width, int height)
    {
        var mask = new bool[width * height];
        foreach (var polygon in polygons)
        {
            var left = Math.Max(0, (int)Math.Floor(polygon.Min(point => point.X) * width));
            var right = Math.Min(width - 1, (int)Math.Ceiling(polygon.Max(point => point.X) * width));
            var top = Math.Max(0, (int)Math.Floor(polygon.Min(point => point.Y) * height));
            var bottom = Math.Min(height - 1, (int)Math.Ceiling(polygon.Max(point => point.Y) * height));
            for (var y = top; y <= bottom; y++)
            for (var x = left; x <= right; x++)
                if (PointInPolygon((x + .5) / width, (y + .5) / height, polygon))
                    mask[y * width + x] = true;
        }
        return mask;
    }

    private static bool PointInPolygon(double x, double y, IReadOnlyList<OcrPoint> polygon)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var j = (i + polygon.Count - 1) % polygon.Count;
            var a = polygon[i];
            var b = polygon[j];
            if ((a.Y > y) != (b.Y > y) &&
                x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }
}
