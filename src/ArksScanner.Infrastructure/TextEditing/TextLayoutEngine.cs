using ArksScanner.Domain.TextEditing;

namespace ArksScanner.Infrastructure.TextEditing;

public sealed record TextLayoutRequest(
    string Text,
    NormalizedBox Box,
    int ImageWidth,
    int ImageHeight,
    double FontSizeNormalized,
    double LetterSpacing,
    double MinimumLetterSpacing,
    double MinimumFontScale);

public sealed record TextMeasurement(double Width, double Height);
public sealed record TextLayoutStep(string Kind, double Value);
public sealed record TextLayoutResult(
    bool Fits,
    bool Overflow,
    double FontSize,
    double FontScale,
    double LetterSpacing,
    double Width,
    double Height,
    int GraphemeCount,
    IReadOnlyList<TextLayoutStep> Steps);

public static class TextLayoutEngine
{
    public const string LayoutVersion = "layout-v1";

    public static TextLayoutResult Fit(TextLayoutRequest request,
        Func<string, double, TextMeasurement> measure)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(measure);
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Contains('\n') ||
            request.Text.Contains('\r') || request.Text.Length > 4_000)
            throw new ArgumentException("A nonempty single-line replacement is required.", nameof(request));
        ArgumentNullException.ThrowIfNull(request.Box);
        if (request.ImageWidth is <= 0 or > 20_000 || request.ImageHeight is <= 0 or > 20_000)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (!double.IsFinite(request.FontSizeNormalized) ||
            request.FontSizeNormalized is <= 0 or > 1 ||
            !double.IsFinite(request.LetterSpacing) ||
            request.LetterSpacing is < -0.1 or > 3 ||
            !double.IsFinite(request.MinimumLetterSpacing) ||
            request.MinimumLetterSpacing < -0.1 ||
            request.MinimumLetterSpacing > request.LetterSpacing ||
            !double.IsFinite(request.MinimumFontScale) ||
            request.MinimumFontScale is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(request));

        var count = request.Text.EnumerateRunes().Count();
        var initialPixels = request.FontSizeNormalized * request.ImageHeight;
        var availableWidth = request.Box.Width * request.ImageWidth;
        var availableHeight = request.Box.Height * request.ImageHeight;
        var measured = measure(request.Text, initialPixels);
        if (!double.IsFinite(measured.Width) || measured.Width < 0 ||
            !double.IsFinite(measured.Height) || measured.Height <= 0)
            throw new ArgumentException("Text metrics must be finite and nonnegative.", nameof(measure));

        var steps = new List<TextLayoutStep>();
        var spacing = request.LetterSpacing;
        var fullWidth = measured.Width + Math.Max(0, count - 1) * spacing * initialPixels;
        if (fullWidth > availableWidth && count > 1)
        {
            var needed = (availableWidth - measured.Width) / ((count - 1) * initialPixels);
            spacing = Math.Clamp(needed, request.MinimumLetterSpacing, request.LetterSpacing);
            if (spacing < request.LetterSpacing)
                steps.Add(new TextLayoutStep("letterSpacing", spacing));
            fullWidth = measured.Width + (count - 1) * spacing * initialPixels;
        }

        var requiredScale = Math.Min(1,
            Math.Min(fullWidth <= 0 ? 1 : availableWidth / fullWidth,
                availableHeight / measured.Height));
        var scale = Math.Clamp(requiredScale, request.MinimumFontScale, 1);
        if (scale < 1)
            steps.Add(new TextLayoutStep("fontSize", scale));
        var finalWidth = fullWidth * scale;
        var finalHeight = measured.Height * scale;
        var fits = finalWidth <= availableWidth + 0.000001 &&
            finalHeight <= availableHeight + 0.000001;
        return new TextLayoutResult(fits, !fits, request.FontSizeNormalized * scale,
            scale, spacing, finalWidth, finalHeight, count, steps);
    }
}
