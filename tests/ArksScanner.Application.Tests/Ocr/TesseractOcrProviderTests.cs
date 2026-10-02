using ArksScanner.Application.Ocr;
using ArksScanner.Domain.Ocr;
using ArksScanner.Infrastructure.Ocr;

namespace ArksScanner.Application.Tests.Ocr;

public sealed class TesseractOcrProviderTests
{
    // Tesseract "tsv" output: a 1000x500 page, one block with two lines (the second in a new paragraph).
    private const string Tsv =
        "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n" +
        "1\t1\t0\t0\t0\t0\t0\t0\t1000\t500\t-1\t\n" +
        "2\t1\t1\t0\t0\t0\t100\t50\t400\t120\t-1\t\n" +
        "3\t1\t1\t1\t0\t0\t100\t50\t400\t40\t-1\t\n" +
        "4\t1\t1\t1\t1\t0\t100\t50\t400\t40\t-1\t\n" +
        "5\t1\t1\t1\t1\t1\t100\t50\t150\t40\t96.5\tTenancy\n" +
        "5\t1\t1\t1\t1\t2\t300\t50\t200\t40\t91\tAgreement\n" +
        "5\t1\t1\t1\t1\t3\t520\t50\t10\t40\t95\t \n" +
        "3\t1\t1\t2\t0\t0\t100\t130\t250\t40\t-1\t\n" +
        "4\t1\t1\t2\t1\t0\t100\t130\t250\t40\t-1\t\n" +
        "5\t1\t1\t2\t1\t1\t100\t130\t250\t40\t80\tBetween\n";

    [Fact]
    public void Parse_builds_blocks_lines_and_words_with_page_relative_boxes()
    {
        var document = TesseractOcrProvider.Parse(Tsv, "tesseract 5.5.0");

        Assert.Equal("Tesseract", document.ProviderName);
        Assert.Equal("Tenancy Agreement\nBetween", document.FullText);
        var block = Assert.Single(document.Elements, e => e.Kind == OcrElementKind.Block);
        var lines = document.Elements.Where(e => e.Kind == OcrElementKind.Line).ToList();
        Assert.Equal(["Tenancy Agreement", "Between"], lines.Select(l => l.Text));
        Assert.All(lines, line => Assert.Equal(block.ClientId, line.ParentClientId));
        var words = document.Elements.Where(e => e.Kind == OcrElementKind.Word).ToList();
        Assert.Equal(["Tenancy", "Agreement", "Between"], words.Select(w => w.Text));
        Assert.Equal([0, 1, 0], words.Select(w => w.ReadingOrder));

        var tenancy = words[0];
        Assert.Equal(.965, tenancy.Confidence, 3);
        Assert.Equal(new OcrPoint(.1, .1), tenancy.Polygon[0]);
        Assert.Equal(new OcrPoint(.25, .18), tenancy.Polygon[2]);
        Assert.Equal(new OcrPoint(.1, .1), block.Polygon[0]);
        Assert.Equal(new OcrPoint(.5, .34), block.Polygon[2]);
    }

    [Fact]
    public void Parse_output_passes_the_shared_OCR_result_validation()
    {
        var document = TesseractOcrProvider.Parse(Tsv);

        var validated = OcrResultValidator.Validate(document, new OcrLimits(10_000, 1_000_000));

        Assert.Equal(6, validated.Elements.Count);
    }

    [Fact]
    public void Parse_of_a_blank_page_returns_no_elements()
    {
        var document = TesseractOcrProvider.Parse(
            "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n" +
            "1\t1\t0\t0\t0\t0\t0\t0\t800\t600\t-1\t\n");

        Assert.Empty(document.Elements);
        Assert.Equal(string.Empty, document.FullText);
    }

    [Fact]
    public async Task A_missing_engine_is_reported_as_a_non_retryable_OCR_failure()
    {
        var provider = new TesseractOcrProvider(new TesseractOptions { ExecutablePath = "tesseract-not-installed-" + Guid.NewGuid().ToString("N") });

        var error = await Assert.ThrowsAsync<OcrProviderException>(() => provider.RecognizeAsync(
            new OcrInput(new MemoryStream([1, 2, 3]), "image/jpeg", "en"), default));

        Assert.Equal("ocr_engine_missing", error.SafeCode);
        Assert.False(error.Retryable);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void Tesseract_is_a_valid_provider_in_every_environment(string environment)
    {
        Assert.True(new OcrOptions { Enabled = true, Provider = "Tesseract" }.IsValid(environment));
        Assert.False(new OcrOptions { Enabled = true, Provider = "Tesseract",
            Tesseract = new TesseractOptions { Languages = "eng; rm -rf" } }.IsValid(environment));
    }
}
