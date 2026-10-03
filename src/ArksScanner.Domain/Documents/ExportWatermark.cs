using System.Text.RegularExpressions;

namespace ArksScanner.Domain.Documents;

/// <summary>
/// A user's own watermark drawn on every page of an export, e.g. "FOR GOVERNMENT USE ONLY":
/// once (Single) or repeated across the whole page (Tiled).
/// </summary>
public sealed partial record ExportWatermark
{
    public const int MaximumTextLength = 120;

    /// <summary>Watermark fonts (bundled with the worker) and whether a bold face exists.</summary>
    public static readonly IReadOnlyDictionary<string, bool> Fonts = new Dictionary<string, bool>(StringComparer.Ordinal)
    {
        ["noto-sans"] = true, ["noto-serif"] = true, ["liberation-sans"] = true, ["liberation-serif"] = true,
        ["carlito"] = true, ["poppins"] = true, ["lato"] = true, ["liberation-mono"] = true,
        ["oswald"] = false, ["montserrat"] = false, ["caveat"] = false, ["dancing-script"] = false,
    };

    public ExportWatermark(
        string text,
        string layout = "Single",
        string fontId = "noto-sans",
        bool bold = true,
        string color = "#C62828",
        double opacity = .25,
        double sizePercent = 8,
        double angleDegrees = 35,
        double spacing = 1.5,
        string position = "Center")
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaximumTextLength || trimmed.Any(char.IsControl))
            throw new ArgumentException("Watermark text must be 1-120 printable characters.", nameof(text));
        if (layout is not ("Single" or "Tiled"))
            throw new ArgumentException("Unsupported watermark layout.", nameof(layout));
        if (fontId is null || !Fonts.TryGetValue(fontId, out var hasBold))
            throw new ArgumentException("Unsupported watermark font.", nameof(fontId));
        if (color is null || !ColorPattern().IsMatch(color))
            throw new ArgumentException("Watermark colour must be #RRGGBB.", nameof(color));
        if (!double.IsFinite(opacity) || opacity is < .05 or > 1)
            throw new ArgumentOutOfRangeException(nameof(opacity));
        if (!double.IsFinite(sizePercent) || sizePercent is < 2 or > 25)
            throw new ArgumentOutOfRangeException(nameof(sizePercent));
        if (!double.IsFinite(angleDegrees) || angleDegrees is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(angleDegrees));
        if (!double.IsFinite(spacing) || spacing is < .5 or > 4)
            throw new ArgumentOutOfRangeException(nameof(spacing));
        if (position is not ("Center" or "Top" or "Bottom"))
            throw new ArgumentException("Unsupported watermark position.", nameof(position));

        Text = trimmed;
        Layout = layout;
        FontId = fontId;
        Bold = bold && hasBold;
        Color = color.ToUpperInvariant();
        Opacity = opacity;
        SizePercent = sizePercent;
        AngleDegrees = angleDegrees;
        Spacing = spacing;
        Position = position;
    }

    public string Text { get; }
    /// <summary>Single (once per page) or Tiled (repeated across the page).</summary>
    public string Layout { get; }
    public string FontId { get; }
    public bool Bold { get; }
    public string Color { get; }
    /// <summary>0.05 (very faint) to 1 (solid).</summary>
    public double Opacity { get; }
    /// <summary>Text height as a percentage of the page's shorter side.</summary>
    public double SizePercent { get; }
    /// <summary>Counter-clockwise rotation; 0 is horizontal.</summary>
    public double AngleDegrees { get; }
    /// <summary>Gap between repeated copies, in multiples of the text height (Tiled only).</summary>
    public double Spacing { get; }
    /// <summary>Vertical placement of a Single watermark: Center, Top or Bottom.</summary>
    public string Position { get; }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();
}
