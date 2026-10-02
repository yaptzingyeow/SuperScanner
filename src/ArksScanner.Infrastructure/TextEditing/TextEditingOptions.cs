namespace ArksScanner.Infrastructure.TextEditing;

public sealed record TextEditingOptions
{
    public const string SectionName = "TextEditing";

    public bool Enabled { get; init; }
    public int MaxSelectionWords { get; init; } = 50;
    public int MaxReplacementCharacters { get; init; } = 4_000;
    public double MaxReplacementBoxArea { get; init; } = 0.5;
    public int MaxQueuedEditsPerPage { get; init; } = 2;
    public double MinimumLetterSpacing { get; init; } = -0.02;
    public double MinimumFontScale { get; init; } = 0.7;
    public int MaskDilationPixels { get; init; } = 2;
    public double ContainmentTolerance { get; init; } = 0.01;
    public int MaxAttempts { get; init; } = 3;
    public string FontManifestPath { get; init; } = "assets/fonts/manifest.json";

    public bool IsValid() =>
        MaxSelectionWords > 0 &&
        MaxReplacementCharacters > 0 &&
        double.IsFinite(MaxReplacementBoxArea) && MaxReplacementBoxArea is > 0 and <= 1 &&
        MaxQueuedEditsPerPage > 0 &&
        double.IsFinite(MinimumLetterSpacing) && MinimumLetterSpacing is > -0.1 and <= 0 &&
        double.IsFinite(MinimumFontScale) && MinimumFontScale is > 0 and <= 1 &&
        MaskDilationPixels is >= 0 and <= 32 &&
        double.IsFinite(ContainmentTolerance) && ContainmentTolerance is >= 0 and <= 1 &&
        MaxAttempts > 0 &&
        !string.IsNullOrWhiteSpace(FontManifestPath) &&
        !Path.IsPathRooted(FontManifestPath) &&
        !FontManifestPath.Split('/', '\\').Contains("..");
}
