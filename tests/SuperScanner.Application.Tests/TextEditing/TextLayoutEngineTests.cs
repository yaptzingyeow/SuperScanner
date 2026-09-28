using SuperScanner.Domain.TextEditing;
using SuperScanner.Infrastructure.TextEditing;

namespace SuperScanner.Application.Tests.TextEditing;

public sealed class TextLayoutEngineTests
{
    [Fact]
    public void Exact_fit_keeps_initial_size_and_spacing()
    {
        var result = TextLayoutEngine.Fit(Request("Hello", 0.125), Measure);
        Assert.True(result.Fits);
        Assert.False(result.Overflow);
        Assert.Equal(1, result.FontScale);
        Assert.Equal(0, result.LetterSpacing);
    }

    [Fact]
    public void Reduces_spacing_before_font_size()
    {
        var result = TextLayoutEngine.Fit(Request("Long replacement", 0.12), Measure);
        Assert.Equal("letterSpacing", result.Steps[0].Kind);
        Assert.InRange(result.FontScale, 0.7, 1);
    }

    [Fact]
    public void Shared_hello_fixture_matches_browser_contract()
    {
        var result = TextLayoutEngine.Fit(Request("Hello", 0.1), Measure);
        Assert.Equal(-0.02, result.LetterSpacing, 6);
        Assert.Equal(100.0 / 121.0, result.FontScale, 6);
        Assert.Equal(100, result.Width, 6);
    }

    [Fact]
    public void Reports_overflow_when_minimum_scale_cannot_fit()
    {
        var result = TextLayoutEngine.Fit(Request("A much longer replacement", 0.02), Measure);
        Assert.True(result.Overflow);
        Assert.False(result.Fits);
        Assert.Equal(0.7, result.FontScale, 5);
    }

    [Fact]
    public void Rejects_empty_text_and_invalid_dimensions()
    {
        Assert.Throws<ArgumentException>(() => TextLayoutEngine.Fit(Request(" ", 0.1), Measure));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextLayoutEngine.Fit(
            Request("Hello", 0.1) with { ImageWidth = 0 }, Measure));
    }

    [Fact]
    public void Unicode_punctuation_counts_as_one_spacing_unit_each()
    {
        var result = TextLayoutEngine.Fit(Request("It’s—fine", 0.3), Measure);
        Assert.True(result.Fits);
        Assert.Equal(9, result.GraphemeCount);
    }

    [Fact]
    public void Wide_box_preserves_explicit_large_letter_spacing()
    {
        var result = TextLayoutEngine.Fit(Request("AB", 0.8) with
        { LetterSpacing = 1.55, MinimumLetterSpacing = 1.55 }, Measure);
        Assert.True(result.Fits);
        Assert.Equal(1.55, result.LetterSpacing);
    }

    [Fact]
    public void Narrow_box_reports_overflow_without_reducing_explicit_large_spacing()
    {
        var result = TextLayoutEngine.Fit(Request("AB", 0.05) with
        { LetterSpacing = 1.55, MinimumLetterSpacing = 1.55 }, Measure);
        Assert.True(result.Overflow);
        Assert.Equal(1.55, result.LetterSpacing);
    }

    [Fact]
    public void Text_style_accepts_three_but_rejects_larger_letter_spacing()
    {
        static TextEditStyle Style(double spacing) => new("noto-sans", "archive-main-regular",
            .05, 400, "#202020", spacing, .75, 0, TextAlignment.Left);
        Assert.Equal(3, Style(3).LetterSpacing);
        Assert.Throws<ArgumentOutOfRangeException>(() => Style(3.01));
    }

    private static TextLayoutRequest Request(string text, double boxWidth) => new(
        text, new NormalizedBox(0.1, 0.2, boxWidth, 0.2), 1000, 1000,
        0.05, 0, -0.02, 0.7);

    private static TextMeasurement Measure(string text, double fontSizePixels) =>
        new(text.EnumerateRunes().Count() * fontSizePixels * 0.5, fontSizePixels);
}
