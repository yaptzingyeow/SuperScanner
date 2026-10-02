using ArksScanner.Domain.Ocr;
using ArksScanner.Domain.TextEditing;

namespace ArksScanner.Infrastructure.TextEditing;

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
        // A word lying mostly inside the selection cannot be kept intact: unsafe. Neighbouring OCR
        // boxes that merely touch (a comma right after a word) keep their pixels; the rest of the
        // selection is erased as usual.
        foreach (var polygon in request.ProtectedPolygons)
        {
            var word = Rasterize([polygon], width, height);
            int area = 0, covered = 0;
            for (var i = 0; i < word.Length; i++)
            {
                if (!word[i]) continue;
                area++;
                if (selected[i]) covered++;
            }
            if (area > 0 && covered * 4 >= area * 3) return Fail("selected-protected-overlap");
        }
        var selectedCount = 0;
        for (var i = 0; i < selected.Length; i++)
        {
            if (!selected[i]) continue;
            if (protectedPixels[i]) selected[i] = false;
            else selectedCount++;
        }
        if (selectedCount == 0) return Fail("selected-protected-overlap");

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
        var inkCutoff = InkCutoff(source, selected, lines);
        var foreground = new bool[selected.Length];
        var background = new byte[height * 3];
        // Darker of the side samples: on mottled card one side may sit on a
        // darker blotch, and that texture must not be mistaken for old ink.
        var paperFloor = new int[height];
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
            // Textured paper is not rejected: the repair copies nearby
            // background (FillFromNearbyBackground) rather than a flat colour.
            for (var channel = 0; channel < 3; channel++)
                background[y * 3 + channel] = (byte)((source[left + channel] +
                    source[right + channel]) / 2);
            var paper = Math.Min(Math.Min(leftBrightness, rightBrightness), borderSamples.Min());
            paperFloor[y] = paper;
            rowStart = y * width;
            for (var x = minX; x <= maxX; x++)
            {
                var index = rowStart + x;
                if (!selected[index] || lines[index]) continue;
                var value = Brightness(source, index * 3);
                if (paper - value > 24 && value < inkCutoff)
                {
                    foreground[index] = true;
                    foundForeground = true;
                }
                // A paper highlight is not old ink and does not need repair.
            }
        }
        if (!foundForeground) return Fail("no-foreground");

        // Ink-dark limit for this selection: darker than every sampled paper
        // row by the usual margin and, on patterned card, darker than the
        // pattern behind the letters.
        var darkLimit = inkCutoff;
        for (var y = 0; y < height; y++)
            if (background[y * 3] != 0) darkLimit = Math.Min(darkLimit, paperFloor[y] - 24);
        ExtendToConnectedGlyphs(source, selected, foreground, approved, protectedPixels, lines,
            width, height, darkLimit, request.DilationPixels + HaloReach);

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
                var paper = paperFloor[y];
                for (var x = 1; x < width - 1; x++)
                {
                    var index = y * width + x;
                    if (repair[index] || !approved[index] || protectedPixels[index] || lines[index] ||
                        paper - Brightness(source, index * 3) <= 24 ||
                        Brightness(source, index * 3) >= inkCutoff) continue;
                    if (repair[index - 1] || repair[index + 1] ||
                        (y > 0 && repair[index - width]) ||
                        (y + 1 < height && repair[index + width]))
                        expanded[index] = true;
                }
            }
            repair = expanded;
        }

        AddSharpeningHalo(source, repair, approved, protectedPixels, lines, width, height);

        // OCR polygons can end one pixel before an antialiased glyph does.
        // Reject a box that would leave a visible fragment of the old text.
        for (var y = 0; y < height; y++)
        {
            if (background[y * 3] == 0) continue;
            var paper = paperFloor[y];
            foreach (var x in new[] { minX - 1, minX - 2, minX - 3,
                maxX + 1, maxX + 2, maxX + 3 })
            {
                if (x < 0 || x >= width) continue;
                var index = y * width + x;
                if (!protectedPixels[index] && !lines[index] && paper - Brightness(source, index * 3) > 24 &&
                    Brightness(source, index * 3) < inkCutoff &&
                    !repair[index]) return Fail("old-ink-remains");
            }
        }

        var output = (byte[])source.Clone();
        // Never copy ink-dark pixels (other text, card borders, artwork) into
        // the repair, even when their surroundings match.
        var forbidden = new bool[repair.Length];
        for (var i = 0; i < forbidden.Length; i++)
            forbidden[i] = repair[i] || protectedPixels[i] || lines[i] ||
                Brightness(source, i * 3) < darkLimit;
        var filled = FillFromNearbyBackground(output, repair, forbidden, width, height);
        for (var i = 0; i < repair.Length; i++)
        {
            // Fallback for blocks with no clean background window nearby.
            if (!repair[i] || filled[i]) continue;
            var row = i / width;
            // If dilation reaches a row without selected glyphs, use the nearest
            // sampled row rather than making a dark stripe across the paper.
            var sampleRow = row;
            for (var distance = 1; background[sampleRow * 3] == 0 &&
                distance < height; distance++)
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

    // OCR polygons often stop short of tall or decorative glyph parts (serifs,
    // ascenders). Follow ink-dark pixels connected to the selected ink up to
    // about a third of the word height outside the selection and treat them as the
    // same glyphs. A component that reaches that margin is a larger dark area
    // (border, panel, artwork) and is left alone, as is anything touching a
    // protected word.
    private static void ExtendToConnectedGlyphs(byte[] source, bool[] selected, bool[] foreground,
        bool[] approved, bool[] protectedPixels, bool[] lines, int width, int height,
        int darkLimit, int reach)
    {
        int minX = width, minY = height, maxX = -1, maxY = -1;
        for (var i = 0; i < selected.Length; i++)
        {
            if (!selected[i]) continue;
            minX = Math.Min(minX, i % width); maxX = Math.Max(maxX, i % width);
            minY = Math.Min(minY, i / width); maxY = Math.Max(maxY, i / width);
        }
        var margin = Math.Max(6, (maxY - minY + 1) * 35 / 100);
        var left = minX - margin;
        var top = minY - margin;
        var right = maxX + margin;
        var bottom = maxY + margin;
        bool InRegion(int x, int y) => x >= left && x <= right && y >= top && y <= bottom &&
            x >= 0 && y >= 0 && x < width && y < height;
        bool OnEdge(int x, int y) => x <= Math.Max(0, left) || y <= Math.Max(0, top) ||
            x >= Math.Min(width - 1, right) || y >= Math.Min(height - 1, bottom);

        var visited = new bool[selected.Length];
        var queue = new Queue<int>();
        var component = new List<int>();
        for (var seed = 0; seed < foreground.Length; seed++)
        {
            if (!foreground[seed] || visited[seed]) continue;
            component.Clear();
            var touchesEdge = false;
            var touchesProtected = false;
            visited[seed] = true;
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                component.Add(index);
                var x = index % width;
                var y = index / width;
                touchesEdge |= OnEdge(x, y);
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if ((dx == 0 && dy == 0) || !InRegion(nx, ny)) continue;
                    var next = ny * width + nx;
                    if (visited[next] || lines[next]) continue;
                    if (Brightness(source, next * 3) >= darkLimit) continue;
                    if (protectedPixels[next]) { touchesProtected = true; continue; }
                    visited[next] = true;
                    queue.Enqueue(next);
                }
            }
            if (touchesEdge || touchesProtected) continue;
            var outside = component.Where(index => !approved[index]).ToList();
            foreach (var index in outside)
            {
                foreground[index] = true;
                var cx = index % width;
                var cy = index / width;
                // Let the antialiased fringe and sharpening rim be repaired too.
                for (var dy = -reach; dy <= reach; dy++)
                for (var dx = -reach; dx <= reach; dx++)
                {
                    var nx = cx + dx;
                    var ny = cy + dy;
                    if (!InRegion(nx, ny)) continue;
                    var neighbour = ny * width + nx;
                    if (!protectedPixels[neighbour] && !lines[neighbour]) approved[neighbour] = true;
                }
            }
        }
    }

    private const int HaloReach = 5;
    private const int HaloContext = 10;

    // Sharpened scans leave a light rim around dark strokes. Removing the ink
    // alone would leave that rim as a letter-shaped outline, so pixels close
    // to the repair that are clearly lighter than the background a little
    // further out are repaired too (inside the approved box only).
    private static void AddSharpeningHalo(byte[] source, bool[] repair, bool[] approved,
        bool[] protectedPixels, bool[] lines, int width, int height)
    {
        int minX = width, minY = height, maxX = -1, maxY = -1;
        for (var i = 0; i < approved.Length; i++)
        {
            if (!approved[i]) continue;
            minX = Math.Min(minX, i % width); maxX = Math.Max(maxX, i % width);
            minY = Math.Min(minY, i / width); maxY = Math.Max(maxY, i / width);
        }
        if (maxX < 0) return;
        minX = Math.Max(0, minX - HaloContext); minY = Math.Max(0, minY - HaloContext);
        maxX = Math.Min(width - 1, maxX + HaloContext); maxY = Math.Min(height - 1, maxY + HaloContext);
        var rw = maxX - minX + 1;
        var rh = maxY - minY + 1;

        // Chessboard distance to the repair, capped at HaloContext.
        var distance = new int[rw * rh];
        for (var y = 0; y < rh; y++)
        for (var x = 0; x < rw; x++)
            distance[y * rw + x] = repair[(minY + y) * width + minX + x] ? 0 : HaloContext + 1;
        for (var step = 1; step <= HaloContext; step++)
        {
            var next = (int[])distance.Clone();
            for (var y = 0; y < rh; y++)
            for (var x = 0; x < rw; x++)
            {
                if (distance[y * rw + x] <= step) continue;
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var ny = y + dy;
                    var nx = x + dx;
                    if (ny < 0 || nx < 0 || ny >= rh || nx >= rw) continue;
                    if (distance[ny * rw + nx] == step - 1) next[y * rw + x] = step;
                }
            }
            distance = next;
        }

        // Mean brightness of the background ring (distance 6..10) around each pixel.
        var sum = new long[(rw + 1) * (rh + 1)];
        var count = new int[(rw + 1) * (rh + 1)];
        for (var y = 0; y < rh; y++)
        {
            long rowSum = 0;
            var rowCount = 0;
            for (var x = 0; x < rw; x++)
            {
                var index = (minY + y) * width + minX + x;
                var d = distance[y * rw + x];
                if (d > HaloReach && d <= HaloContext && !protectedPixels[index] && !lines[index])
                {
                    rowSum += Brightness(source, index * 3);
                    rowCount++;
                }
                sum[(y + 1) * (rw + 1) + x + 1] = sum[y * (rw + 1) + x + 1] + rowSum;
                count[(y + 1) * (rw + 1) + x + 1] = count[y * (rw + 1) + x + 1] + rowCount;
            }
        }
        for (var y = 0; y < rh; y++)
        for (var x = 0; x < rw; x++)
        {
            var d = distance[y * rw + x];
            if (d == 0 || d > HaloReach) continue;
            var index = (minY + y) * width + minX + x;
            if (!approved[index] || protectedPixels[index] || lines[index]) continue;
            int y0 = Math.Max(0, y - HaloContext), y1 = Math.Min(rh, y + HaloContext + 1);
            int x0 = Math.Max(0, x - HaloContext), x1 = Math.Min(rw, x + HaloContext + 1);
            var n = count[y1 * (rw + 1) + x1] - count[y0 * (rw + 1) + x1] - count[y1 * (rw + 1) + x0] + count[y0 * (rw + 1) + x0];
            if (n < 8) continue;
            var mean = (sum[y1 * (rw + 1) + x1] - sum[y0 * (rw + 1) + x1] - sum[y1 * (rw + 1) + x0] + sum[y0 * (rw + 1) + x0]) / (double)n;
            if (Brightness(source, index * 3) > mean + 12) repair[index] = true;
        }
    }

    // Brightness below which a selected pixel counts as ink. Otsu's split of
    // the selection separates near-black letters from a mid-grey printed
    // pattern behind them; when the two classes are not clearly apart (plain
    // paper, faint ink) no extra limit applies and the paper margin decides.
    private static int InkCutoff(byte[] source, bool[] selected, bool[] lines)
    {
        var histogram = new long[256];
        long total = 0;
        for (var i = 0; i < selected.Length; i++)
        {
            if (!selected[i] || lines[i]) continue;
            histogram[Brightness(source, i * 3)]++;
            total++;
        }
        if (total == 0) return 256;
        double sumAll = 0;
        for (var v = 0; v < 256; v++) sumAll += v * (double)histogram[v];
        double sumDark = 0, bestVariance = -1;
        long dark = 0;
        var threshold = 0;
        for (var v = 0; v < 256; v++)
        {
            dark += histogram[v];
            sumDark += v * (double)histogram[v];
            var light = total - dark;
            if (dark == 0 || light == 0) continue;
            var meanDark = sumDark / dark;
            var meanLight = (sumAll - sumDark) / light;
            var variance = dark * (double)light * (meanDark - meanLight) * (meanDark - meanLight);
            if (variance > bestVariance) { bestVariance = variance; threshold = v; }
        }
        long darkCount = 0;
        double darkSum = 0, lightSum = 0;
        for (var v = 0; v < 256; v++)
        {
            if (v <= threshold) { darkCount += histogram[v]; darkSum += v * (double)histogram[v]; }
            else lightSum += v * (double)histogram[v];
        }
        var lightCount = total - darkCount;
        if (darkCount == 0 || lightCount == 0) return 256;
        // Clearly separated classes only: black print on a lighter pattern.
        return lightSum / lightCount - darkSum / darkCount >= 90 ? threshold + 1 : 256;
    }

    private const int Block = 24;
    private const int Ring = 8;
    private const int SearchX = 160;
    private const int SearchY = 60;

    // Exemplar repair: each block of repair pixels is replaced by the nearby
    // background window whose known surroundings match best (sum of squared
    // differences). Copying real pixels keeps paper grain, mottled print and
    // gradients that a flat colour would erase. Sources never include repair,
    // protected-word or rule pixels, and the procedure is deterministic, so a
    // preview and the applied revision are identical.
    private static bool[] FillFromNearbyBackground(byte[] output, bool[] repair, bool[] forbidden,
        int width, int height)
    {
        var filled = new bool[repair.Length];
        var known = new bool[repair.Length];
        int minX = width, minY = height, maxX = -1, maxY = -1;
        for (var i = 0; i < repair.Length; i++)
        {
            known[i] = !repair[i];
            if (!repair[i]) continue;
            minX = Math.Min(minX, i % width);
            maxX = Math.Max(maxX, i % width);
            minY = Math.Min(minY, i / width);
            maxY = Math.Max(maxY, i / width);
        }
        if (maxX < 0) return filled;

        var integral = new int[(width + 1) * (height + 1)];
        for (var y = 0; y < height; y++)
        {
            var rowSum = 0;
            for (var x = 0; x < width; x++)
            {
                rowSum += forbidden[y * width + x] ? 1 : 0;
                integral[(y + 1) * (width + 1) + x + 1] = integral[y * (width + 1) + x + 1] + rowSum;
            }
        }

        var pending = new List<(int Y, int X)>();
        for (var by = minY; by <= maxY; by += Block)
        for (var bx = minX; bx <= maxX; bx += Block)
        {
            var any = false;
            for (var y = by; y < Math.Min(height, by + Block) && !any; y++)
            for (var x = bx; x < Math.Min(width, bx + Block) && !any; x++)
                any = repair[y * width + x];
            if (any) pending.Add((by, bx));
        }

        while (pending.Count > 0)
        {
            // Fill in waves, best-surrounded blocks first, so later blocks
            // match against pixels that earlier waves have already restored.
            var ranked = pending
                .Select(block => (Block: block, Known: KnownCount(known, width, height, block)))
                .OrderByDescending(entry => entry.Known)
                .ThenBy(entry => entry.Block.Y).ThenBy(entry => entry.Block.X)
                .Select(entry => entry.Block).ToList();
            var take = Math.Max(1, ranked.Count / 4);
            foreach (var block in ranked.Take(take))
                FillBlock(output, repair, known, filled, integral, width, height, block.Y, block.X);
            pending = ranked.Skip(take).ToList();
        }
        return filled;
    }

    private static int KnownCount(bool[] known, int width, int height, (int Y, int X) block)
    {
        var count = 0;
        for (var y = Math.Max(0, block.Y - Ring); y < Math.Min(height, block.Y + Block + Ring); y++)
        for (var x = Math.Max(0, block.X - Ring); x < Math.Min(width, block.X + Block + Ring); x++)
            if (known[y * width + x]) count++;
        return count;
    }

    private static void FillBlock(byte[] output, bool[] repair, bool[] known, bool[] filled,
        int[] integral, int width, int height, int by, int bx)
    {
        var wy0 = Math.Max(0, by - Ring);
        var wx0 = Math.Max(0, bx - Ring);
        var wh = Math.Min(height, by + Block + Ring) - wy0;
        var ww = Math.Min(width, bx + Block + Ring) - wx0;

        // Template: known pixels of the window, every other pixel for the
        // coarse search and every pixel for the final refinement.
        var coarse = new List<int>();
        var dense = new List<int>();
        for (var y = 0; y < wh; y++)
        for (var x = 0; x < ww; x++)
        {
            if (!known[(wy0 + y) * width + wx0 + x]) continue;
            dense.Add(y * width + x);
            if (y % 2 == 0 && x % 2 == 0) coarse.Add(y * width + x);
        }
        if (dense.Count < 16) return;
        var targetBase = wy0 * width + wx0;

        long Score(List<int> offsets, int sy, int sx, long limit)
        {
            var sourceBase = sy * width + sx;
            long sum = 0;
            foreach (var offset in offsets)
            {
                var t = (targetBase + offset) * 3;
                var s = (sourceBase + offset) * 3;
                var d0 = output[t] - output[s];
                var d1 = output[t + 1] - output[s + 1];
                var d2 = output[t + 2] - output[s + 2];
                sum += d0 * d0 + d1 * d1 + d2 * d2;
                if (sum >= limit) return sum;
            }
            return sum;
        }

        bool Usable(int sy, int sx) =>
            sy >= 0 && sx >= 0 && sy + wh <= height && sx + ww <= width &&
            integral[(sy + wh) * (width + 1) + sx + ww] - integral[sy * (width + 1) + sx + ww] -
            integral[(sy + wh) * (width + 1) + sx] + integral[sy * (width + 1) + sx] == 0;

        var best = long.MaxValue;
        int bestY = -1, bestX = -1;
        for (var sy = Math.Max(0, wy0 - SearchY); sy <= Math.Min(height - wh, wy0 + SearchY); sy += 2)
        for (var sx = Math.Max(0, wx0 - SearchX); sx <= Math.Min(width - ww, wx0 + SearchX); sx += 2)
        {
            if (!Usable(sy, sx)) continue;
            var score = Score(coarse, sy, sx, best);
            if (score < best) { best = score; bestY = sy; bestX = sx; }
        }
        if (bestY < 0) return;
        var centreY = bestY;
        var centreX = bestX;
        best = long.MaxValue;
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            if (!Usable(centreY + dy, centreX + dx)) continue;
            var score = Score(dense, centreY + dy, centreX + dx, best);
            if (score < best) { best = score; bestY = centreY + dy; bestX = centreX + dx; }
        }

        for (var y = by; y < Math.Min(height, by + Block); y++)
        for (var x = bx; x < Math.Min(width, bx + Block); x++)
        {
            var target = y * width + x;
            if (!repair[target] || known[target]) continue;
            var source = ((bestY + y - wy0) * width + bestX + x - wx0) * 3;
            output[target * 3] = output[source];
            output[target * 3 + 1] = output[source + 1];
            output[target * 3 + 2] = output[source + 2];
            known[target] = true;
            filled[target] = true;
        }
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
