using System.Text.Json;
using ArksScanner.Domain.Documents;

namespace ArksScanner.Domain.Tests.Documents;

public sealed class ExportWatermarkTests
{
    [Fact]
    public void Keeps_the_users_settings_and_trims_the_text()
    {
        var watermark = new ExportWatermark("  For government use only  ", "Tiled", "liberation-serif", true,
            "#1a237e", .3, 6, -30, 2, "Top");

        Assert.Equal("For government use only", watermark.Text);
        Assert.Equal("Tiled", watermark.Layout);
        Assert.Equal("liberation-serif", watermark.FontId);
        Assert.True(watermark.Bold);
        Assert.Equal("#1A237E", watermark.Color);
        Assert.Equal(.3, watermark.Opacity);
        Assert.Equal(-30, watermark.AngleDegrees);
    }

    [Fact]
    public void Bold_is_dropped_for_fonts_without_a_bold_face()
    {
        Assert.False(new ExportWatermark("COPY", fontId: "caveat", bold: true).Bold);
    }

    [Theory]
    [InlineData("", "Single", "noto-sans", "#000000", .5, 8, 0, 1.5, "Center")]
    [InlineData("x", "Diagonal", "noto-sans", "#000000", .5, 8, 0, 1.5, "Center")]
    [InlineData("x", "Single", "comic-sans", "#000000", .5, 8, 0, 1.5, "Center")]
    [InlineData("x", "Single", "noto-sans", "red", .5, 8, 0, 1.5, "Center")]
    [InlineData("x", "Single", "noto-sans", "#000000", 0, 8, 0, 1.5, "Center")]
    [InlineData("x", "Single", "noto-sans", "#000000", .5, 40, 0, 1.5, "Center")]
    [InlineData("x", "Single", "noto-sans", "#000000", .5, 8, 120, 1.5, "Center")]
    [InlineData("x", "Single", "noto-sans", "#000000", .5, 8, 0, 9, "Center")]
    [InlineData("x", "Single", "noto-sans", "#000000", .5, 8, 0, 1.5, "Left")]
    public void Rejects_invalid_settings(string text, string layout, string font, string color, double opacity,
        double size, double angle, double spacing, string position)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new ExportWatermark(text, layout, font, false, color, opacity, size, angle, spacing, position));
    }

    [Fact]
    public void Rejects_text_that_is_too_long_or_contains_control_characters()
    {
        Assert.Throws<ArgumentException>(() => new ExportWatermark(new string('A', ExportWatermark.MaximumTextLength + 1)));
        Assert.Throws<ArgumentException>(() => new ExportWatermark("line\nbreak"));
    }

    [Fact]
    public void Survives_the_export_snapshot_round_trip()
    {
        var watermark = new ExportWatermark("FOR IC COPY ONLY", "Tiled", "oswald", false, "#555555", .2, 5, 30, 1, "Center");
        var entry = new DocumentExportSnapshotEntry(Guid.NewGuid(), 1, 1, "Magic", "previews/p.jpg", Watermark: watermark);

        var restored = JsonSerializer.Deserialize<DocumentExportSnapshotEntry>(JsonSerializer.Serialize(entry))!;

        Assert.Equal(watermark, restored.Watermark);
    }
}
