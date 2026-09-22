using Google.Cloud.DocumentAI.V1;
using SuperScanner.Application.Ocr;
using SuperScanner.Infrastructure.Ocr;

namespace SuperScanner.Application.Tests.Ocr;

public sealed class DocumentAiTextAnchorReaderTests
{
    [Fact]
    public void Read_ReturnsSingleSegment()
    {
        var anchor = Anchor(Segment(2, 5));

        Assert.Equal("cde", DocumentAiTextAnchorReader.Read("abcdef", anchor));
    }

    [Fact]
    public void Read_TreatsOmittedStartAsZero()
    {
        var anchor = new Document.Types.TextAnchor();
        anchor.TextSegments.Add(new Document.Types.TextAnchor.Types.TextSegment { EndIndex = 3 });

        Assert.Equal("abc", DocumentAiTextAnchorReader.Read("abcdef", anchor));
    }

    [Fact]
    public void Read_ConcatenatesMultipleSegmentsInProviderOrder()
    {
        var anchor = Anchor(Segment(0, 3), Segment(6, 9));

        Assert.Equal("abcghi", DocumentAiTextAnchorReader.Read("abcdefghi", anchor));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Read_ReturnsEmptyForMissingOrEmptyAnchor(bool useNull)
    {
        var anchor = useNull ? null : new Document.Types.TextAnchor();

        Assert.Equal(string.Empty, DocumentAiTextAnchorReader.Read("abcdef", anchor));
    }

    [Fact]
    public void Read_UsesUtf8ByteOffsetsAfterMultibyteText()
    {
        var anchor = Anchor(Segment(8, 12));

        Assert.Equal("name", DocumentAiTextAnchorReader.Read("éclair name", anchor));
    }

    [Theory]
    [InlineData(0L, 7L)]
    [InlineData(5L, 4L)]
    [InlineData(-1L, 1L)]
    [InlineData(1L, 2L)]
    public void Read_RejectsInvalidRangeOrUtf8Boundary(long start, long end)
    {
        var anchor = Anchor(Segment(start, end));

        var error = Assert.Throws<OcrProviderException>(() =>
            DocumentAiTextAnchorReader.Read("étest", anchor));

        Assert.Equal("ocr_invalid_response", error.SafeCode);
        Assert.False(error.Retryable);
        Assert.Equal("OCR provider operation failed.", error.Message);
    }

    private static Document.Types.TextAnchor Anchor(
        params Document.Types.TextAnchor.Types.TextSegment[] segments)
    {
        var anchor = new Document.Types.TextAnchor();
        anchor.TextSegments.Add(segments);
        return anchor;
    }

    private static Document.Types.TextAnchor.Types.TextSegment Segment(long start, long end) =>
        new() { StartIndex = start, EndIndex = end };
}
