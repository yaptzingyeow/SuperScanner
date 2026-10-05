using ArksScanner.Domain.Documents;
using ArksScanner.Infrastructure.Processing;

namespace ArksScanner.Application.Tests.Processing;

public sealed class PdfUserWatermarkFontTests
{
    private static readonly string Fonts = Path.Combine(AppContext.BaseDirectory, "assets", "fonts");

    private static string FontFor(string text, string fontId = "noto-sans", bool bold = true) =>
        Path.GetFileName(PdfUserWatermark.FontPath(
            new ExportWatermark(text, "Single", fontId, bold, "#C62828", .3, 8, 35, 1.5), Fonts));

    [Fact]
    public void Latin_text_keeps_the_chosen_font_and_weight()
    {
        Assert.Equal("NotoSans-Bold.ttf", FontFor("CONFIDENTIAL"));
        Assert.Equal("LiberationSerif-Regular.ttf", FontFor("SULIT", "liberation-serif", bold: false));
    }

    [Theory]
    [InlineData("仅供政府使用", "Hani")]
    [InlineData("للاستخدام الحكومي فقط", "Arab")]
    [InlineData("ลับ", "Thai")]
    public void Text_the_chosen_font_cannot_draw_falls_back_to_a_font_that_can(string text, string script)
    {
        var file = FontFor(text);
        Assert.NotEqual("NotoSans-Bold.ttf", file);
        Assert.True(File.Exists(Path.Combine(Fonts, file)), file);
        Assert.Contains(script, PdfUserWatermark.ScriptsOfFile(file, Fonts));
    }
}
