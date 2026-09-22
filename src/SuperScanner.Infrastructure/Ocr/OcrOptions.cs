using SuperScanner.Infrastructure.Processing;

namespace SuperScanner.Infrastructure.Ocr;

public sealed class OcrOptions
{
    public const string SectionName = "Ocr";

    public bool Enabled { get; init; }
    public string Provider { get; init; } = "Disabled";
    public string Language { get; init; } = "en";
    public int MaxAttempts { get; init; } = 3;
    public int TimeoutSeconds { get; init; } = 30;
    public int MaxElements { get; init; } = 10_000;
    public int MaxRecognizedCharacters { get; init; } = 1_000_000;

    public bool IsValid(string environmentName)
    {
        if (string.IsNullOrWhiteSpace(environmentName) ||
            !string.Equals(Language, "en", StringComparison.Ordinal) ||
            MaxAttempts is < 1 or > PostgresJobQueue.DefaultMaxAttempts ||
            TimeoutSeconds < 1 ||
            MaxElements < 1 ||
            MaxRecognizedCharacters < 1)
        {
            return false;
        }

        if (!Enabled) return string.Equals(Provider, "Disabled", StringComparison.Ordinal);
        return !string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Provider, "Fake", StringComparison.Ordinal);
    }
}
