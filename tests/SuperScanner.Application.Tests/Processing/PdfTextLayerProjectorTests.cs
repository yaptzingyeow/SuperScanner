using System.Reflection;
using SuperScanner.Domain.Ocr;
using SuperScanner.Infrastructure.Processing;

namespace SuperScanner.Application.Tests.Processing;

public sealed class PdfTextLayerProjectorTests
{
    [Fact]
    public void PdfExportOptions_RejectsUnsafeOrUnboundedValues()
    {
        Assert.True(new PdfExportOptions().IsValid());
        Assert.False(new PdfExportOptions { MaximumWordsPerPage = 50_001 }.IsValid());
        Assert.False(new PdfExportOptions { MaximumCharactersPerPage = 2_000_001 }.IsValid());
        Assert.False(new PdfExportOptions { MinimumHorizontalScalePercent = 0 }.IsValid());
        Assert.False(new PdfExportOptions { MaximumAbsoluteAngleDegrees = 46 }.IsValid());
    }

    [Fact]
    public void Project_ConvertsTopOriginCoordinatesToPdfBottomOrigin()
    {
        var word = Word("Hello", 2, Poly(.10, .20, .30, .10));

        var projected = PdfTextLayerProjector.Project([word], 600, 800, PdfTextLayerLimits.Default);

        var result = Assert.Single(projected);
        Assert.Equal(60, result.X, 6);
        Assert.Equal(560, result.Y, 6);
        Assert.Equal(180, result.Width, 6);
        Assert.Equal(80, result.Height, 6);
    }

    [Fact]
    public void Project_ReturnsOnlyWordsInDeterministicReadingOrder()
    {
        var resultId = Guid.NewGuid();
        var line = Element(resultId, null, OcrElementKind.Line, "line", 0, Poly(.1, .1, .8, .1));
        var second = Element(resultId, line.Id, OcrElementKind.Word, "second", 2, Poly(.3, .1, .2, .1));
        var first = Element(resultId, line.Id, OcrElementKind.Word, "first", 1, Poly(.1, .1, .2, .1));

        var projected = PdfTextLayerProjector.Project([second, line, first], 100, 100, PdfTextLayerLimits.Default);

        Assert.Equal(["first", "second"], projected.Select(word => word.Text));
    }

    [Fact]
    public void Project_PreservesMildClockwiseRotationAndBoundsIt()
    {
        var word = Word("tilted", 0,
        [
            new(.10, .20), new(.40, .25), new(.39, .35), new(.09, .30)
        ]);

        var projected = Assert.Single(PdfTextLayerProjector.Project(
            [word], 600, 800, PdfTextLayerLimits.Default));

        Assert.InRange(projected.AngleDegrees, -13, -12);
        Assert.InRange(projected.Width, 184, 185);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Project_SkipsEmptyText(string text)
    {
        Assert.Empty(PdfTextLayerProjector.Project(
            [Word(text, 0, Poly(.1, .1, .2, .1))], 100, 100, PdfTextLayerLimits.Default));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("not-json")]
    [InlineData("[{\"x\":-0.1,\"y\":0.1},{\"x\":0.2,\"y\":0.1},{\"x\":0.2,\"y\":0.2},{\"x\":0.1,\"y\":0.2}]")]
    public void Project_SkipsMalformedOrOutOfRangeGeometry(string polygonJson)
    {
        var word = Word("secret", 0, Poly(.1, .1, .2, .1));
        SetPolygonJson(word, polygonJson);

        Assert.Empty(PdfTextLayerProjector.Project([word], 100, 100, PdfTextLayerLimits.Default));
    }

    [Fact]
    public void Project_SkipsCollapsedBoxes()
    {
        var point = new OcrPoint(.2, .2);
        Assert.Empty(PdfTextLayerProjector.Project(
            [Word("collapsed", 0, [point, point, point, point])], 100, 100, PdfTextLayerLimits.Default));
    }

    [Fact]
    public void Project_ClipsCoordinatesAtPageBounds()
    {
        var word = Word("edge", 0, Poly(0, 0, 1, 1));

        var result = Assert.Single(PdfTextLayerProjector.Project(
            [word], 612, 792, PdfTextLayerLimits.Default));

        Assert.Equal(0, result.X);
        Assert.Equal(0, result.Y);
        Assert.Equal(612, result.Width);
        Assert.Equal(792, result.Height);
    }

    [Fact]
    public void Project_StopsAtMaximumWordCount()
    {
        var words = Enumerable.Range(0, 3)
            .Select(index => Word($"w{index}", index, Poly(.1, .1 + index * .1, .2, .05)))
            .ToArray();
        var limits = PdfTextLayerLimits.Default with { MaximumWordsPerPage = 2 };

        var projected = PdfTextLayerProjector.Project(words, 100, 100, limits);

        Assert.Equal(2, projected.Count);
    }

    [Fact]
    public void Project_StopsBeforeMaximumCharacterCountIsExceeded()
    {
        var words = new[]
        {
            Word("1234", 0, Poly(.1, .1, .2, .05)),
            Word("5678", 1, Poly(.1, .2, .2, .05))
        };
        var limits = PdfTextLayerLimits.Default with { MaximumCharactersPerPage = 6 };

        var projected = PdfTextLayerProjector.Project(words, 100, 100, limits);

        Assert.Equal("1234", Assert.Single(projected).Text);
    }

    private static OcrElement Word(string text, int readingOrder, IReadOnlyList<OcrPoint> polygon) =>
        Element(Guid.NewGuid(), null, OcrElementKind.Word, text, readingOrder, polygon);

    private static OcrElement Element(
        Guid resultId,
        Guid? parentId,
        OcrElementKind kind,
        string text,
        int readingOrder,
        IReadOnlyList<OcrPoint> polygon) =>
        OcrElement.Create(
            Guid.NewGuid(), resultId, parentId, kind, text, .99,
            OcrTextType.Printed, readingOrder, polygon);

    private static OcrPoint[] Poly(double x, double y, double width, double height) =>
    [
        new(x, y), new(x + width, y),
        new(x + width, y + height), new(x, y + height)
    ];

    private static void SetPolygonJson(OcrElement element, string value)
    {
        typeof(OcrElement)
            .GetProperty(nameof(OcrElement.PolygonJson), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(element, value);
    }
}
