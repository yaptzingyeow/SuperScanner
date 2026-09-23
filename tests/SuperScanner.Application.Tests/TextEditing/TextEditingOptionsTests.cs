using SuperScanner.Infrastructure.TextEditing;

namespace SuperScanner.Application.Tests.TextEditing;

public sealed class TextEditingOptionsTests
{
    [Theory]
    [InlineData(0, 100, 0.7, false)]
    [InlineData(50, 0, 0.7, false)]
    [InlineData(50, 100, 0.7, true)]
    public void Options_require_positive_limits(
        int words, int characters, double minimumScale, bool valid)
    {
        var options = ValidOptions() with
        {
            MaxSelectionWords = words,
            MaxReplacementCharacters = characters,
            MinimumFontScale = minimumScale
        };

        Assert.Equal(valid, options.IsValid());
    }

    [Theory]
    [InlineData(-0.1, 0.7, 2, 0.01, 3, false)]
    [InlineData(-0.02, 0, 2, 0.01, 3, false)]
    [InlineData(-0.02, 0.7, -1, 0.01, 3, false)]
    [InlineData(-0.02, 0.7, 2, -0.01, 3, false)]
    [InlineData(-0.02, 0.7, 2, 0.01, 0, false)]
    [InlineData(-0.02, 0.7, 2, 0.01, 3, true)]
    public void Options_validate_rendering_and_retry_bounds(
        double spacing, double scale, int dilation, double tolerance, int attempts, bool valid)
    {
        var options = ValidOptions() with
        {
            MinimumLetterSpacing = spacing,
            MinimumFontScale = scale,
            MaskDilationPixels = dilation,
            ContainmentTolerance = tolerance,
            MaxAttempts = attempts
        };

        Assert.Equal(valid, options.IsValid());
    }

    private static TextEditingOptions ValidOptions() => new()
    {
        Enabled = false,
        MaxSelectionWords = 50,
        MaxReplacementCharacters = 100,
        MaxReplacementBoxArea = 0.5,
        MaxQueuedEditsPerPage = 2,
        MinimumLetterSpacing = -0.02,
        MinimumFontScale = 0.7,
        MaskDilationPixels = 2,
        ContainmentTolerance = 0.01,
        MaxAttempts = 3,
        FontManifestPath = "assets/fonts/manifest.json"
    };
}
