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

public sealed class PdfExportOptions
{
    public const string SectionName = "PdfExport";

    public bool SearchableTextEnabled { get; init; } = true;
    /// <summary>Stamp "Scanned with Arks Scanner" in the bottom-right corner of every exported page.</summary>
    public bool BrandWatermark { get; init; } = true;
    public int MaximumWordsPerPage { get; init; } = 10_000;
    public int MaximumCharactersPerPage { get; init; } = 100_000;
    public int MaximumCharactersPerWord { get; init; } = 4_096;
    public double MinimumDimensionPoints { get; init; } = 0.1;
    public double MinimumHorizontalScalePercent { get; init; } = 50;
    public double MaximumHorizontalScalePercent { get; init; } = 200;
    public double MaximumAbsoluteAngleDegrees { get; init; } = 20;

    public PdfTextLayerLimits ToLimits() => new()
    {
        MaximumWordsPerPage = MaximumWordsPerPage,
        MaximumCharactersPerPage = MaximumCharactersPerPage,
        MaximumCharactersPerWord = MaximumCharactersPerWord,
        MinimumDimensionPoints = MinimumDimensionPoints,
        MinimumHorizontalScalePercent = MinimumHorizontalScalePercent,
        MaximumHorizontalScalePercent = MaximumHorizontalScalePercent,
        MaximumAbsoluteAngleDegrees = MaximumAbsoluteAngleDegrees
    };

    public bool IsValid() =>
        MaximumWordsPerPage is > 0 and <= 50_000 &&
        MaximumCharactersPerPage is > 0 and <= 2_000_000 &&
        MaximumCharactersPerWord is > 0 and <= 100_000 &&
        double.IsFinite(MinimumDimensionPoints) && MinimumDimensionPoints > 0 &&
        double.IsFinite(MinimumHorizontalScalePercent) && MinimumHorizontalScalePercent is >= 10 and <= 100 &&
        double.IsFinite(MaximumHorizontalScalePercent) && MaximumHorizontalScalePercent is >= 100 and <= 500 &&
        MinimumHorizontalScalePercent <= MaximumHorizontalScalePercent &&
        double.IsFinite(MaximumAbsoluteAngleDegrees) && MaximumAbsoluteAngleDegrees is >= 0 and <= 45;
}
