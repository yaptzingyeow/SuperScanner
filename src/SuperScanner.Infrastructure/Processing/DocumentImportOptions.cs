namespace SuperScanner.Infrastructure.Processing;

public sealed class DocumentImportOptions
{
    public const string SectionName = "DocumentImport";

    public long MaxUploadBytes { get; set; } = 26_214_400;
    public int MaxPagesPerImport { get; set; } = 50;
    public int MaxPagesPerDocument { get; set; } = 50;
    public long MaxDecodedPixels { get; set; } = 250_000_000;
    public long MaxRenderedBytes { get; set; } = 524_288_000;
    public int InspectTimeoutSeconds { get; set; } = 15;
    public int PageRenderTimeoutSeconds { get; set; } = 45;
    public int MaxAttempts { get; set; } = 6;

    public bool IsValid() =>
        MaxUploadBytes > 0 && MaxPagesPerImport > 0 && MaxPagesPerDocument > 0 &&
        MaxDecodedPixels > 0 && MaxRenderedBytes > 0 && InspectTimeoutSeconds > 0 &&
        PageRenderTimeoutSeconds > 0 && MaxAttempts > 0;
}
