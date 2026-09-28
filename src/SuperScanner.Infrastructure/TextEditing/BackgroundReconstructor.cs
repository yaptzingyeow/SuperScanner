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
    bool[]? RepairMask, string? FailureCode, string? DiagnosticReason = null,
    bool[]? PreservedLineMask = null);

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
            return Fail("invalid-input");

        var width = request.Width;
        var height = request.Height;
        var selected = Rasterize(request.SelectedPolygons, width, height);
        var protectedPixels = Rasterize(request.ProtectedPolygons, width, height);
        var approved = new bool[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            approved[y * width + x] = InsideBox(x, y, width, height, request.ApprovedBox);

        // The placement rectangle is a layout guide, not an eraser. A nearby
        // word may be inside it; only actual selected/repair pixels may change.
        for (var i = 0; i < selected.Length; i++)
            if (selected[i] && protectedPixels[i]) return Fail("selected-protected-overlap");

        var minX = width;
        var maxX = -1;
        for (var i = 0; i < selected.Length; i++)
        {
            if (!selected[i]) continue;
            if (!approved[i]) return Fail("selected-outside-clear-box");
            minX = Math.Min(minX, i % width);
            maxX = Math.Max(maxX, i % width);
        }
        if (maxX < minX || minX < 2 || maxX + 2 >= width) return Fail("edge-boundary");

        var source = request.SourceRgb;
        var lines = DetectHorizontalRules(source, selected, width, height, minX, maxX);
        var foreground = new bool[selected.Length];
        var background = new byte[height * 3];
        var foundForeground = false;
        for (var y = 0; y < height; y++)
        {
            var rowStart = y * width;
            if (!selected.AsSpan(rowStart, width).Contains(true)) continue;
            var leftPaperX = FindPaperSample(source, protectedPixels, rowStart,
                minX, -1, width);
            var rightPaperX = FindPaperSample(source, protectedPixels, rowStart,
                maxX, 1, width);
            if (leftPaperX < 0 || rightPaperX < 0)
            {
                // A form rule or neighbouring word may occupy this row's side
                // samples. Use nearby paper only when both sides agree there.
                var sampled = false;
                for (var distance = 1; distance <= 16 && !sampled; distance++)
                foreach (var sampleY in new[] { y - distance, y + distance })
                {
                    if (sampleY < 0 || sampleY >= height) continue;
                    var sampleStart = sampleY * width;
                    var lx = FindPaperSample(source, protectedPixels, sampleStart, minX, -1, width);
                    var rx = FindPaperSample(source, protectedPixels, sampleStart, maxX, 1, width);
                    if (lx < 0 || rx < 0) continue;
                    var l = (sampleStart + lx) * 3;
                    var r = (sampleStart + rx) * 3;
                    if (Math.Abs(Brightness(source, l) - Brightness(source, r)) > 24) continue;
                    rowStart = sampleStart;
                    leftPaperX = lx;
                    rightPaperX = rx;
                    sampled = true;
                    break;
                }
                if (!sampled) return Fail("paper-sample-missing");
            }
            var left = (rowStart + leftPaperX) * 3;
            var right = (rowStart + rightPaperX) * 3;
            var leftBrightness = Brightness(source, left);
            var rightBrightness = Brightness(source, right);
            if (Math.Abs(leftBrightness - rightBrightness) > 35)
                return Fail("paper-sides-differ");
            var borderSamples = new List<int>(14);
            // The first pixel outside an OCR polygon can still be antialiased
            // ink from the selected glyph; the approved box gives it room to
            // be repaired without touching a neighbouring recognized word.
            for (var distance = 2; distance <= 8; distance++)
            {
                var leftX = minX - distance;
                var rightX = maxX + distance;
                if (leftX < 0 || rightX >= width) continue;
                if (!protectedPixels[rowStart + leftX])
                {
                    var leftSample = Brightness(source, (rowStart + leftX) * 3);
                    if (leftSample >= 120)
                        borderSamples.Add(leftSample);
                }
                if (!protectedPixels[rowStart + rightX])
                {
                    var rightSample = Brightness(source, (rowStart + rightX) * 3);
                    if (rightSample >= 120)
                        borderSamples.Add(rightSample);
                }
            }
            if (borderSamples.Count < 4) return Fail("border-samples-missing");
            borderSamples.Sort();
            // One light and one dark paper-grain pixel should not reject an
            // otherwise flat scan. Repeated high-frequency variation still fails.
            if (borderSamples[^2] - borderSamples[1] > 32)
                return Fail("border-variation");
            for (var channel = 0; channel < 3; channel++)
                background[y * 3 + channel] = (byte)((source[left + channel] +
                    source[right + channel]) / 2);
            var paper = Brightness(background, y * 3);
            rowStart = y * width;
            for (var x = minX; x <= maxX; x++)
            {
                var index = rowStart + x;
                if (!selected[index] || lines[index]) continue;
                var value = Brightness(source, index * 3);
                if (paper - value > 24)
                {
                    foreground[index] = true;
                    foundForeground = true;
                }
                // A paper highlight is not old ink and does not need repair.
            }
        }
        if (!foundForeground) return Fail("no-foreground");

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
                if (approved[target] && !protectedPixels[target] && !lines[target]) repair[target] = true;
            }
        }

        // OCR boxes often stop a few pixels before the final antialiased stroke.
        // Follow only dark pixels connected to the selected ink, within its
        // independently bounded clear area and never into another OCR word.
        for (var pass = 0; pass < 3; pass++)
        {
            var expanded = (bool[])repair.Clone();
            for (var y = 0; y < height; y++)
            {
                if (background[y * 3] == 0) continue;
                var paper = Brightness(background, y * 3);
                for (var x = 1; x < width - 1; x++)
                {
                    var index = y * width + x;
                    if (repair[index] || !approved[index] || protectedPixels[index] || lines[index] ||
                        paper - Brightness(source, index * 3) <= 24) continue;
                    if (repair[index - 1] || repair[index + 1] ||
                        (y > 0 && repair[index - width]) ||
                        (y + 1 < height && repair[index + width]))
                        expanded[index] = true;
                }
            }
            repair = expanded;
        }

        // OCR polygons can end one pixel before an antialiased glyph does.
        // Reject a box that would leave a visible fragment of the old text.
        for (var y = 0; y < height; y++)
        {
            if (background[y * 3] == 0) continue;
            var paper = Brightness(background, y * 3);
            foreach (var x in new[] { minX - 1, minX - 2, minX - 3,
                maxX + 1, maxX + 2, maxX + 3 })
            {
                if (x < 0 || x >= width) continue;
                var index = y * width + x;
                if (!protectedPixels[index] && !lines[index] && paper - Brightness(source, index * 3) > 24 &&
                    !repair[index]) return Fail("old-ink-remains");
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
            for (var distance = 1; background[sampleRow * 3] == 0 &&
                distance <= request.DilationPixels + 1; distance++)
            {
                if (row - distance >= 0 && background[(row - distance) * 3] != 0)
                    sampleRow = row - distance;
                else if (row + distance < height &&
                    background[(row + distance) * 3] != 0)
                    sampleRow = row + distance;
            }
            if (background[sampleRow * 3] == 0) return Fail("paper-row-missing");
            for (var channel = 0; channel < 3; channel++)
                output[i * 3 + channel] = background[sampleRow * 3 + channel];
        }
        return new BackgroundReconstructionResult(output, repair, null, PreservedLineMask: lines);
    }

    private static bool[] DetectHorizontalRules(byte[] source, bool[] selected,
        int width, int height, int minX, int maxX)
    {
        var mask = new bool[selected.Length];
        var first = Array.FindIndex(selected, value => value) / width;
        var last = Array.FindLastIndex(selected, value => value) / width;
        var left = Math.Max(0, minX - 8);
        var right = Math.Min(width - 1, maxX + 8);
        // Require a continuous rule extending beyond both ends of the words.
        // Thick dark regions are not treated as rules or silently preserved.
        var rows = new bool[height];
        for (var y = Math.Max(0, first - 8); y <= Math.Min(height - 1, last + 8); y++)
        {
            rows[y] = right - left >= 20;
            for (var x = left; x <= right && rows[y]; x++)
                rows[y] = Brightness(source, (y * width + x) * 3) < 120;
        }
        for (var y = 0; y < height; y++)
        {
            if (!rows[y]) continue;
            var start = y;
            while (y + 1 < height && rows[y + 1]) y++;
            if (y - start + 1 > Math.Max(3, (last - first + 1) / 4)) continue;
            for (var row = start; row <= y; row++)
            for (var x = left; x <= right; x++) mask[row * width + x] = true;
        }
        return mask;
    }

    private static BackgroundReconstructionResult Fail(string reason) =>
        new(null, null, Unsafe, reason);

    private static bool Valid(IReadOnlyList<OcrPoint> polygon) => polygon is { Count: 4 } &&
        polygon.All(point => double.IsFinite(point.X) && double.IsFinite(point.Y) &&
            point.X is >= 0 and <= 1 && point.Y is >= 0 and <= 1);

    private static int Brightness(byte[] pixels, int start) =>
        (pixels[start] * 299 + pixels[start + 1] * 587 + pixels[start + 2] * 114) / 1000;

    private static int FindPaperSample(byte[] source, bool[] protectedPixels,
        int rowStart, int edge, int direction, int width)
    {
        for (var distance = 2; distance <= 8; distance++)
        {
            var x = edge + distance * direction;
            if (x < 0 || x >= width) break;
            if (!protectedPixels[rowStart + x] &&
                Brightness(source, (rowStart + x) * 3) >= 120)
                return x;
        }
        return -1;
    }

    private static bool InsideBox(int x, int y, int width, int height, NormalizedBox box) =>
        (x + .5) / width >= box.X && (x + .5) / width <= box.X + box.Width &&
        (y + .5) / height >= box.Y && (y + .5) / height <= box.Y + box.Height;

    internal static bool[] Rasterize(IReadOnlyList<IReadOnlyList<OcrPoint>> polygons,
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
