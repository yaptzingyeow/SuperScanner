using System.Text.RegularExpressions;

namespace SuperScanner.Infrastructure.Ocr;

public static class OcrProviderNames
{
    public const string Disabled = "Disabled";
    public const string Fake = "Fake";
    public const string GoogleDocumentAi = "GoogleDocumentAi";
}

public sealed partial class GoogleDocumentAiOptions
{
    public string ProjectId { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string ProcessorId { get; init; } = string.Empty;
    public string? Endpoint { get; init; }
    public long MaxInputBytes { get; init; } = 25 * 1024 * 1024;
    public bool EnableStyleInfo { get; init; }

    public string EffectiveEndpoint => string.IsNullOrEmpty(Endpoint)
        ? $"{Location}-documentai.googleapis.com"
        : Endpoint;

    public string ProcessorName =>
        $"projects/{ProjectId}/locations/{Location}/processors/{ProcessorId}";

    public bool IsValid()
    {
        if (!ProjectIdPattern().IsMatch(ProjectId) ||
            !string.Equals(Location, "asia-southeast1", StringComparison.Ordinal) ||
            !ProcessorIdPattern().IsMatch(ProcessorId) ||
            MaxInputBytes is < 1 or > 26_214_400)
        {
            return false;
        }

        return string.IsNullOrEmpty(Endpoint) ||
            string.Equals(Endpoint, $"{Location}-documentai.googleapis.com", StringComparison.Ordinal);
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{4,28}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectIdPattern();

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ProcessorIdPattern();
}
