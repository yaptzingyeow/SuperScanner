namespace SuperScanner.Infrastructure.Processing;

public sealed record PdfTextLayerWord(
    string Text,
    double X,
    double Y,
    double Width,
    double Height,
    double AngleDegrees,
    int ReadingOrder);

public sealed record PdfTextLayerLimits
{
    public static PdfTextLayerLimits Default { get; } = new();

    public int MaximumWordsPerPage { get; init; } = 10_000;
    public int MaximumCharactersPerPage { get; init; } = 100_000;
    public int MaximumCharactersPerWord { get; init; } = 4_096;
    public double MinimumDimensionPoints { get; init; } = 0.1;
    public double MinimumHorizontalScalePercent { get; init; } = 50;
    public double MaximumHorizontalScalePercent { get; init; } = 200;
    public double MaximumAbsoluteAngleDegrees { get; init; } = 20;
}
