using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SuperScanner.Infrastructure.Processing;

namespace SuperScanner.Application.Tests.Processing;

public sealed class PdfBrandWatermarkTests
{
    private static readonly string Font = Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "NotoSans-Bold.ttf");

    [Fact]
    public void Stamp_is_a_wide_transparent_label()
    {
        using var image = new MagickImage(PdfBrandWatermark.ImagePng(Font));

        Assert.True(image.Width > image.Height * 6, $"{image.Width}x{image.Height}");
        Assert.True(image.HasAlpha);
        Assert.Equal(0, image.GetPixels().GetPixel(0, 0).ToColor()!.A); // rounded corner stays clear
        var preview = Environment.GetEnvironmentVariable("SUPERSCANNER_STAMP_PREVIEW");
        if (!string.IsNullOrEmpty(preview)) image.Write(preview);
    }

    [Fact]
    public void Draw_places_the_stamp_bottom_right_as_an_image_not_as_searchable_text()
    {
        using var pdf = new PdfDocument();
        var page = pdf.AddPage();
        page.Width = PdfSharp.Drawing.XUnit.FromPoint(595);
        page.Height = PdfSharp.Drawing.XUnit.FromPoint(842);

        PdfBrandWatermark.Draw(page, Font);

        using var saved = new MemoryStream();
        pdf.Save(saved, closeStream: false);
        saved.Position = 0;
        using var reopened = PdfReader.Open(saved, PdfDocumentOpenMode.Import);
        var resources = reopened.Pages[0].Resources;
        Assert.NotNull(resources.Elements.GetDictionary("/XObject"));
        Assert.DoesNotContain("/Font", resources.Elements.Keys);
    }
}
