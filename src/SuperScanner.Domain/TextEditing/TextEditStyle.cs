using System.Text.RegularExpressions;

namespace SuperScanner.Domain.TextEditing;

public enum TextAlignment
{
    Left,
    Center,
    Right
}

public sealed record TextEditStyle
{
    private static readonly Regex IdentifierPattern =
        new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private static readonly Regex ColorPattern =
        new("^#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?$", RegexOptions.CultureInvariant);

    public TextEditStyle(
        string fontId,
        string fontVersion,
        double fontSize,
        int weight,
        string colorHex,
        double letterSpacing,
        double baseline,
        double angleDegrees,
        TextAlignment alignment)
    {
        ValidateIdentifier(fontId, nameof(fontId));
        ValidateIdentifier(fontVersion, nameof(fontVersion));
        if (!double.IsFinite(fontSize) || fontSize <= 0 || fontSize > 1)
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (weight < 100 || weight > 900 || weight % 100 != 0)
            throw new ArgumentOutOfRangeException(nameof(weight));
        if (string.IsNullOrWhiteSpace(colorHex) || !ColorPattern.IsMatch(colorHex))
            throw new ArgumentException("A hexadecimal RGB or RGBA colour is required.", nameof(colorHex));
        if (!double.IsFinite(letterSpacing) || letterSpacing is < -0.1 or > 0.1)
            throw new ArgumentOutOfRangeException(nameof(letterSpacing));
        if (!double.IsFinite(baseline) || baseline is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(baseline));
        if (!double.IsFinite(angleDegrees) || angleDegrees is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(angleDegrees));
        if (!Enum.IsDefined(alignment)) throw new ArgumentOutOfRangeException(nameof(alignment));

        FontId = fontId;
        FontVersion = fontVersion;
        FontSize = fontSize;
        Weight = weight;
        ColorHex = colorHex.ToUpperInvariant();
        LetterSpacing = letterSpacing;
        Baseline = baseline;
        AngleDegrees = angleDegrees;
        Alignment = alignment;
    }

    public string FontId { get; }
    public string FontVersion { get; }
    public double FontSize { get; }
    public int Weight { get; }
    public string ColorHex { get; }
    public double LetterSpacing { get; }
    public double Baseline { get; }
    public double AngleDegrees { get; }
    public TextAlignment Alignment { get; }

    internal static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || !IdentifierPattern.IsMatch(value))
            throw new ArgumentException("A stable catalogue identifier is required.", parameterName);
    }
}
