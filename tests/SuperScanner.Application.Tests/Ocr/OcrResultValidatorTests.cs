using SuperScanner.Application.Ocr;
using SuperScanner.Domain.Ocr;

namespace SuperScanner.Application.Tests.Ocr;

public sealed class OcrResultValidatorTests
{
    [Fact]
    public void Validate_RejectsMoreThanConfiguredElements()
    {
        var error = Assert.Throws<OcrProviderException>(() => OcrResultValidator.Validate(
            DocumentWithWords(3), new OcrLimits(2, 1_000)));

        Assert.Equal("ocr_invalid_response", error.SafeCode);
        Assert.False(error.Retryable);
    }

    [Fact]
    public void Validate_RejectsMissingParentWithoutLeakingProviderText()
    {
        var document = new NormalizedOcrDocument("private-name", "Fake", "v1",
        [
            Element("word", "missing", OcrElementKind.Word, "private-name", 0)
        ]);

        var error = Assert.Throws<OcrProviderException>(() =>
            OcrResultValidator.Validate(document, new(10, 1_000)));

        Assert.Equal("ocr_invalid_response", error.SafeCode);
        Assert.DoesNotContain("private-name", error.Message);
    }

    [Fact]
    public void Validate_RejectsDuplicateReadingOrderWithinParent()
    {
        var document = new NormalizedOcrDocument("A B", "Fake", "v1",
        [
            Element("block", null, OcrElementKind.Block, "A B", 0),
            Element("line-a", "block", OcrElementKind.Line, "A", 0),
            Element("line-b", "block", OcrElementKind.Line, "B", 0)
        ]);

        AssertInvalid(document);
    }

    [Fact]
    public void Validate_RejectsNonFiniteCoordinates()
    {
        var invalid = Element("block", null, OcrElementKind.Block, "A", 0) with
        {
            Polygon = [new(double.NaN, .1), new(.9, .1), new(.9, .2), new(.1, .2)]
        };

        AssertInvalid(new("A", "Fake", "v1", [invalid]));
    }

    [Fact]
    public void Validate_RejectsRecognizedCharactersOverLimit()
    {
        var document = new NormalizedOcrDocument("123456", "Fake", "v1",
            [Element("block", null, OcrElementKind.Block, "123456", 0)]);

        AssertInvalid(document, new(10, 5));
    }

    [Fact]
    public void Validate_AcceptsEmptyRecognition()
    {
        var document = new NormalizedOcrDocument(string.Empty, "Fake", "v1", []);

        var validated = OcrResultValidator.Validate(document, new(10, 100));

        Assert.Same(document, validated);
    }

    private static void AssertInvalid(NormalizedOcrDocument document, OcrLimits? limits = null)
    {
        var error = Assert.Throws<OcrProviderException>(() =>
            OcrResultValidator.Validate(document, limits ?? new(10, 1_000)));
        Assert.Equal("ocr_invalid_response", error.SafeCode);
        Assert.False(error.Retryable);
    }

    private static NormalizedOcrDocument DocumentWithWords(int count)
    {
        var elements = new List<NormalizedOcrElement>
        {
            Element("block", null, OcrElementKind.Block, "Words", 0),
            Element("line", "block", OcrElementKind.Line, "Words", 0)
        };
        elements.AddRange(Enumerable.Range(0, count)
            .Select(index => Element($"word-{index}", "line", OcrElementKind.Word, "Word", index)));
        return new("Words", "Fake", "v1", elements);
    }

    private static NormalizedOcrElement Element(
        string id,
        string? parent,
        OcrElementKind kind,
        string text,
        int order) => new(id, parent, kind, text, .95, OcrTextType.Printed, order,
            [new(.1, .1), new(.9, .1), new(.9, .2), new(.1, .2)]);
}
