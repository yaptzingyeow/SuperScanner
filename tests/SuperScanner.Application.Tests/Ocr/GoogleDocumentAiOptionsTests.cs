using SuperScanner.Infrastructure.Ocr;

namespace SuperScanner.Application.Tests.Ocr;

public sealed class GoogleDocumentAiOptionsTests
{
    [Theory]
    [InlineData("superscanner-dev", "asia-southeast1", "fc0b14e64c62e7aa", "asia-southeast1-documentai.googleapis.com", 26_214_400L, true)]
    [InlineData("", "asia-southeast1", "fc0b14e64c62e7aa", "asia-southeast1-documentai.googleapis.com", 26_214_400L, false)]
    [InlineData("superscanner-dev", "", "fc0b14e64c62e7aa", "asia-southeast1-documentai.googleapis.com", 26_214_400L, false)]
    [InlineData("superscanner-dev", "us", "fc0b14e64c62e7aa", "us-documentai.googleapis.com", 26_214_400L, false)]
    [InlineData("superscanner-dev", "asia-southeast1", "", "asia-southeast1-documentai.googleapis.com", 26_214_400L, false)]
    [InlineData("superscanner-dev", "asia-southeast1", "bad/id", "asia-southeast1-documentai.googleapis.com", 26_214_400L, false)]
    [InlineData("superscanner-dev", "asia-southeast1", "fc0b14e64c62e7aa", "us-documentai.googleapis.com", 26_214_400L, false)]
    [InlineData("superscanner-dev", "asia-southeast1", "fc0b14e64c62e7aa", null, 1L, true)]
    [InlineData("superscanner-dev", "asia-southeast1", "fc0b14e64c62e7aa", null, 0L, false)]
    [InlineData("superscanner-dev", "asia-southeast1", "fc0b14e64c62e7aa", null, 26_214_401L, false)]
    public void GoogleOptions_ValidateApprovedResourceAndInputLimit(
        string projectId,
        string location,
        string processorId,
        string? endpoint,
        long maxInputBytes,
        bool expected)
    {
        var options = new GoogleDocumentAiOptions
        {
            ProjectId = projectId,
            Location = location,
            ProcessorId = processorId,
            Endpoint = endpoint,
            MaxInputBytes = maxInputBytes
        };

        Assert.Equal(expected, options.IsValid());
    }

    [Fact]
    public void GoogleOptions_DeriveRegionalEndpointAndProcessorName()
    {
        var options = ValidGoogleOptions(endpoint: null);

        Assert.Equal("asia-southeast1-documentai.googleapis.com", options.EffectiveEndpoint);
        Assert.Equal(
            "projects/superscanner-dev/locations/asia-southeast1/processors/fc0b14e64c62e7aa",
            options.ProcessorName);
    }

    [Fact]
    public void GoogleOptions_EmptyEndpointDerivesRegionalEndpoint()
    {
        var options = ValidGoogleOptions(endpoint: string.Empty);

        Assert.True(options.IsValid());
        Assert.Equal("asia-southeast1-documentai.googleapis.com", options.EffectiveEndpoint);
    }

    [Theory]
    [InlineData(false, "Disabled", "Development", "en", true)]
    [InlineData(true, "GoogleDocumentAi", "Development", "en", true)]
    [InlineData(true, "GoogleDocumentAi", "Production", "en", true)]
    [InlineData(true, "GoogleDocumentAi", "Development", "ms", false)]
    [InlineData(true, "Fake", "Development", "en", true)]
    [InlineData(true, "Fake", "Production", "en", false)]
    [InlineData(true, "Disabled", "Development", "en", false)]
    [InlineData(false, "Fake", "Development", "en", false)]
    [InlineData(false, "GoogleDocumentAi", "Development", "en", false)]
    public void OcrOptions_ValidateProviderEnvironmentAndLanguage(
        bool enabled,
        string provider,
        string environment,
        string language,
        bool expected)
    {
        var options = new OcrOptions
        {
            Enabled = enabled,
            Provider = provider,
            Language = language,
            Google = ValidGoogleOptions()
        };

        Assert.Equal(expected, options.IsValid(environment));
    }

    [Fact]
    public void OcrOptions_RejectGoogleProviderWhenGoogleResourceIsInvalid()
    {
        var options = new OcrOptions
        {
            Enabled = true,
            Provider = "GoogleDocumentAi",
            Google = ValidGoogleOptions(processorId: string.Empty)
        };

        Assert.False(options.IsValid("Development"));
    }

    private static GoogleDocumentAiOptions ValidGoogleOptions(
        string processorId = "fc0b14e64c62e7aa",
        string? endpoint = "asia-southeast1-documentai.googleapis.com") => new()
        {
            ProjectId = "superscanner-dev",
            Location = "asia-southeast1",
            ProcessorId = processorId,
            Endpoint = endpoint,
            MaxInputBytes = 25 * 1024 * 1024
        };
}
