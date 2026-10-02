using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using ArksScanner.Infrastructure.Processing;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace ArksScanner.Application.Tests.Processing;

public sealed class PdfSharpTextLayerWriterTests
{
    [Fact]
    public void Write_EmbedsOrderedInvisibleSearchableText()
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);
        var writer = Writer();

        writer.Write(page,
        [
            new("Yap", 50, 700, 36, 12, 0, 0),
            new("Tzing", 90, 700, 42, 12, 0, 1),
            new("Yeow", 136, 700, 42, 12, 0, 2)
        ]);

        using var output = new MemoryStream();
        document.Save(output, false);
        var pdf = Encoding.Latin1.GetString(output.ToArray());
        var content = DecodeLastContent(page);
        using var extractedDocument = PdfPigDocument.Open(output.ToArray());
        var extracted = ContentOrderTextExtractor.GetText(extractedDocument.GetPage(1));

        Assert.Contains("3 Tr", content);
        Assert.Contains("/ToUnicode", pdf);
        Assert.Equal("Yap Tzing Yeow", string.Join(' ', extracted.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
    }

    [Fact]
    public void Write_UsesRotationAndBoundedHorizontalScaling()
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        var writer = Writer();

        writer.Write(page, [new("rotated", 10, 20, 500, 12, 8, 0)]);

        var content = DecodeLastContent(page);
        Assert.Contains("3 Tr", content);
        Assert.Contains(" cm", content);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    public void Write_RejectsZeroDimensionsWithoutTextInError(double width, double height)
    {
        using var document = new PdfDocument();
        var exception = Assert.Throws<PdfTextLayerWriteException>(() => Writer().Write(
            document.AddPage(), [new("sensitive-value", 1, 1, width, height, 0, 0)]));

        Assert.Equal("pdf_text_geometry_invalid", exception.Code);
        Assert.DoesNotContain("sensitive-value", exception.ToString());
    }

    [Fact]
    public void Write_RejectsOversizedWordLists()
    {
        using var document = new PdfDocument();
        var words = Enumerable.Range(0, 10_001)
            .Select(index => new PdfTextLayerWord("x", 1, 1, 1, 1, 0, index))
            .ToArray();

        var exception = Assert.Throws<PdfTextLayerWriteException>(() =>
            Writer().Write(document.AddPage(), words));

        Assert.Equal("pdf_text_size_limit", exception.Code);
    }

    [Fact]
    public void Write_RejectsUnsupportedNonBmpGlyphsWithSafeCode()
    {
        using var document = new PdfDocument();

        var exception = Assert.Throws<PdfTextLayerWriteException>(() => Writer().Write(
            document.AddPage(), [new("private-😀", 1, 1, 10, 10, 0, 0)]));

        Assert.Equal("pdf_text_glyph_unsupported", exception.Code);
        Assert.DoesNotContain("private-", exception.ToString());
    }

    [Fact]
    public void Write_RejectsMissingFontWithSafeCode()
    {
        using var document = new PdfDocument();
        var writer = new PdfSharpTextLayerWriter(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ttf"));

        var exception = Assert.Throws<PdfTextLayerWriteException>(() => writer.Write(
            document.AddPage(), [new("private-text", 1, 1, 10, 10, 0, 0)]));

        Assert.Equal("pdf_text_font_unavailable", exception.Code);
        Assert.DoesNotContain("private-text", exception.ToString());
    }

    [Fact]
    public void Write_RejectsCorruptFontWithSafeCode()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arksscanner-corrupt-font-{Guid.NewGuid():N}.ttf");
        try
        {
            File.WriteAllBytes(path, [0, 0, 0, 0]);
            using var document = new PdfDocument();

            var exception = Assert.Throws<PdfTextLayerWriteException>(() =>
                new PdfSharpTextLayerWriter(path).Write(
                    document.AddPage(), [new("private-text", 1, 1, 10, 10, 0, 0)]));

            Assert.Equal("pdf_text_font_unavailable", exception.Code);
            Assert.DoesNotContain("private-text", exception.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static PdfSharpTextLayerWriter Writer() =>
        new(Path.Combine(FindRepositoryRoot(), "assets", "fonts", "NotoSans-Regular.ttf"));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "assets", "fonts")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static string DecodeLastContent(PdfPage page)
    {
        var item = page.Contents.Elements.Last();
        var content = item is PdfReference reference
            ? (PdfDictionary)reference.Value
            : (PdfDictionary)item;
        content.Stream!.TryUncompress();
        return Encoding.ASCII.GetString(content.Stream.Value);
    }
}
