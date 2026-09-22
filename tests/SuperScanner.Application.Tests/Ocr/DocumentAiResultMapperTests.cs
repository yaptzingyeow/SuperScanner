using Google.Cloud.DocumentAI.V1;
using Google.Protobuf;
using SuperScanner.Application.Ocr;
using SuperScanner.Domain.Ocr;
using SuperScanner.Infrastructure.Ocr;

namespace SuperScanner.Application.Tests.Ocr;

public sealed class DocumentAiResultMapperTests
{
    [Fact]
    public void Map_NormalizesHierarchyReadingOrderGeometryAndHandwriting()
    {
        var result = DocumentAiResultMapper.Map(LoadFixture());

        Assert.Equal("Printed Note\nSecond", result.FullText);
        Assert.Equal("GoogleDocumentAi", result.ProviderName);
        Assert.Equal("pretrained-ocr-v2", result.ModelVersion);
        Assert.Equal(7, result.Elements.Count);

        AssertElement(result, "p1-b1", null, OcrElementKind.Block, "Printed Note", .90, OcrTextType.Printed, 0);
        AssertElement(result, "p1-b1-l1", "p1-b1", OcrElementKind.Line, "Printed Note", .91, OcrTextType.Printed, 0);
        AssertElement(result, "p1-b1-l1-w1", "p1-b1-l1", OcrElementKind.Word, "Printed", .96, OcrTextType.Printed, 0);
        AssertElement(result, "p1-b1-l1-w2", "p1-b1-l1", OcrElementKind.Word, "Note", .93, OcrTextType.Handwritten, 1);
        AssertElement(result, "p2-b1", null, OcrElementKind.Block, "Second", .88, OcrTextType.Printed, 1);

        var absoluteFallback = result.Elements.Single(element => element.ClientId == "p2-b1");
        var expectedPolygon = new[]
        {
            new OcrPoint(.1, .1), new OcrPoint(.9, .1),
            new OcrPoint(.9, .2), new OcrPoint(.1, .2)
        };
        Assert.Collection(absoluteFallback.Polygon,
            point => AssertPoint(expectedPolygon[0], point),
            point => AssertPoint(expectedPolygon[1], point),
            point => AssertPoint(expectedPolygon[2], point),
            point => AssertPoint(expectedPolygon[3], point));
        Assert.Same(result, OcrResultValidator.Validate(result, new(100, 1_000)));
    }

    [Fact]
    public void Map_RejectsAbsoluteVerticesWhenPageDimensionsAreZero()
    {
        var document = LoadFixture();
        document.Pages[1].Dimension.Width = 0;

        AssertInvalid(document);
    }

    [Fact]
    public void Map_RejectsPolygonWithFewerThanThreeVertices()
    {
        var document = LoadFixture();
        var vertices = document.Pages[0].Blocks[0].Layout.BoundingPoly.NormalizedVertices;
        vertices.RemoveAt(vertices.Count - 1);
        vertices.RemoveAt(vertices.Count - 1);

        AssertInvalid(document);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(1.01)]
    public void Map_RejectsNonFiniteOrMateriallyOutOfRangeGeometry(double x)
    {
        var document = LoadFixture();
        document.Pages[0].Blocks[0].Layout.BoundingPoly.NormalizedVertices[0].X = (float)x;

        AssertInvalid(document);
    }

    [Fact]
    public void Map_RejectsInvalidTextAnchorWithoutLeakingText()
    {
        var document = LoadFixture();
        document.Pages[0].Tokens[0].Layout.TextAnchor.TextSegments[0].EndIndex = 10_000;

        var error = Assert.Throws<OcrProviderException>(() => DocumentAiResultMapper.Map(document));

        Assert.Equal("ocr_invalid_response", error.SafeCode);
        Assert.DoesNotContain("Printed", error.Message);
    }

    private static Document LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Ocr", "google-document-ai-response.json");
        return JsonParser.Default.Parse<Document>(File.ReadAllText(path));
    }

    private static void AssertElement(
        NormalizedOcrDocument result,
        string id,
        string? parentId,
        OcrElementKind kind,
        string text,
        double confidence,
        OcrTextType textType,
        int readingOrder)
    {
        var element = result.Elements.Single(item => item.ClientId == id);
        Assert.Equal(parentId, element.ParentClientId);
        Assert.Equal(kind, element.Kind);
        Assert.Equal(text, element.Text);
        Assert.Equal(confidence, element.Confidence, precision: 5);
        Assert.Equal(textType, element.TextType);
        Assert.Equal(readingOrder, element.ReadingOrder);
        Assert.Equal(4, element.Polygon.Count);
    }

    private static void AssertInvalid(Document document)
    {
        var error = Assert.Throws<OcrProviderException>(() => DocumentAiResultMapper.Map(document));
        Assert.Equal("ocr_invalid_response", error.SafeCode);
        Assert.False(error.Retryable);
    }

    private static void AssertPoint(OcrPoint expected, OcrPoint actual)
    {
        Assert.Equal(expected.X, actual.X, precision: 5);
        Assert.Equal(expected.Y, actual.Y, precision: 5);
    }
}
