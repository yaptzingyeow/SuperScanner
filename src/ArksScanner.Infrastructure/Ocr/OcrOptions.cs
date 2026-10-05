using ArksScanner.Infrastructure.Processing;

namespace ArksScanner.Infrastructure.Ocr;

public sealed class OcrOptions
{
    public const string SectionName = "Ocr";

    public bool Enabled { get; init; }
    public string Provider { get; init; } = "Disabled";
    /// <summary>
    /// Recognize every page automatically after import and after each crop or look change.
    /// Off by default: pages are recognized only when a user asks, because with a paid
    /// provider every automatic run (and every later look change) is a new charge.
    /// </summary>
    public bool AutoRecognize { get; init; }
    /// <summary>"auto" lets the provider detect the language (any script); or a code such as "ja" as a hint.</summary>
    public string Language { get; init; } = "auto";
    public int MaxAttempts { get; init; } = 3;
    public int TimeoutSeconds { get; init; } = 30;
    public int MaxElements { get; init; } = 10_000;
    public int MaxRecognizedCharacters { get; init; } = 1_000_000;
    public GoogleDocumentAiOptions Google { get; init; } = new();
    public TesseractOptions Tesseract { get; init; } = new();

    public bool IsValid(string environmentName)
    {
        if (string.IsNullOrWhiteSpace(environmentName) ||
            !ArksScanner.Application.Ocr.OcrLanguage.IsValid(Language) ||
            MaxAttempts is < 1 or > PostgresJobQueue.DefaultMaxAttempts ||
            TimeoutSeconds < 1 ||
            MaxElements < 1 ||
            MaxRecognizedCharacters < 1)
        {
            return false;
        }

        if (!Enabled)
            return string.Equals(Provider, OcrProviderNames.Disabled, StringComparison.Ordinal);

        if (string.Equals(Provider, OcrProviderNames.GoogleDocumentAi, StringComparison.Ordinal))
            return Google.IsValid();

        if (string.Equals(Provider, OcrProviderNames.Tesseract, StringComparison.Ordinal))
            return Tesseract.IsValid();

        return !string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Provider, OcrProviderNames.Fake, StringComparison.Ordinal);
    }
}
