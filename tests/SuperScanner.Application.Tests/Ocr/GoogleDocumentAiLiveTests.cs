using System.Diagnostics;
using System.Text;
using ImageMagick;
using SuperScanner.Application.Ocr;
using SuperScanner.Domain.Ocr;
using SuperScanner.Infrastructure.Ocr;
using Xunit.Abstractions;

namespace SuperScanner.Application.Tests.Ocr;

public sealed class GoogleDocumentAiLiveTests(ITestOutputHelper output)
{
    [GoogleDocumentAiLiveFact]
    public async Task SyntheticEnglishPage_ProducesNormalizedRecognition()
    {
        var options = new GoogleDocumentAiOptions
        {
            ProjectId = Environment.GetEnvironmentVariable("GOOGLE_DOCUMENT_AI_PROJECT_ID")
                ?? "superscanner-dev",
            Location = Environment.GetEnvironmentVariable("GOOGLE_DOCUMENT_AI_LOCATION")
                ?? "asia-southeast1",
            ProcessorId = Environment.GetEnvironmentVariable("GOOGLE_DOCUMENT_AI_PROCESSOR_ID")
                ?? "fc0b14e64c62e7aa",
            Endpoint = Environment.GetEnvironmentVariable("GOOGLE_DOCUMENT_AI_ENDPOINT")
                ?? "asia-southeast1-documentai.googleapis.com",
            EnableStyleInfo = string.Equals(
                Environment.GetEnvironmentVariable("GOOGLE_DOCUMENT_AI_ENABLE_STYLE_INFO"),
                "1",
                StringComparison.Ordinal),
            MaxInputBytes = 25 * 1024 * 1024
        };
        Assert.True(options.IsValid());

        var provider = new GoogleDocumentAiOcrProvider(
            DocumentAiClient.Create(options), options, new OcrMetrics());
        await using var image = new MemoryStream(CreateSyntheticPng());
        var stopwatch = Stopwatch.StartNew();

        NormalizedOcrDocument result;
        try
        {
            result = await provider.RecognizeAsync(
                new(image, "image/png", "en"),
                CancellationToken.None);
        }
        catch (OcrProviderException exception)
        {
            output.WriteLine("OCR live acceptance failed with safe_code={0}", exception.SafeCode);
            throw;
        }

        stopwatch.Stop();
        Assert.Equal(OcrProviderNames.GoogleDocumentAi, result.ProviderName);
        Assert.False(string.IsNullOrWhiteSpace(result.FullText));
        var words = result.Elements.Where(element => element.Kind == OcrElementKind.Word).ToArray();
        Assert.NotEmpty(words);
        Assert.All(words, word =>
        {
            Assert.True(double.IsFinite(word.Confidence));
            Assert.InRange(word.Confidence, 0, 1);
            Assert.Equal(4, word.Polygon.Count);
            Assert.All(word.Polygon, point =>
            {
                Assert.True(double.IsFinite(point.X));
                Assert.True(double.IsFinite(point.Y));
                Assert.InRange(point.X, 0, 1);
                Assert.InRange(point.Y, 0, 1);
            });
        });

        output.WriteLine(
            "OCR live acceptance: pages={0}, elements={1}, words={2}, latency_ms={3:F0}, aggregate_confidence={4:F4}",
            1,
            result.Elements.Count,
            words.Length,
            stopwatch.Elapsed.TotalMilliseconds,
            words.Average(word => word.Confidence));
    }

    private static byte[] CreateSyntheticPng()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="1200" height="1600">
              <rect width="1200" height="1600" fill="white"/>
              <text x="110" y="300" font-family="Arial, sans-serif" font-size="72" fill="black">
                Synthetic English Document
              </text>
              <text x="110" y="470" font-family="Arial, sans-serif" font-size="58" fill="black">
                Printed text recognition sample
              </text>
              <text x="110" y="680" font-family="cursive" font-size="76" font-style="italic" fill="black">
                Generic handwritten note
              </text>
            </svg>
            """;
        using var image = new MagickImage(Encoding.UTF8.GetBytes(svg));
        image.Format = MagickFormat.Png;
        return image.ToByteArray();
    }
}

public sealed class GoogleDocumentAiLiveFactAttribute : FactAttribute
{
    public GoogleDocumentAiLiveFactAttribute()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("GOOGLE_DOCUMENT_AI_LIVE_TEST"),
            "1",
            StringComparison.Ordinal))
        {
            Skip = "Set GOOGLE_DOCUMENT_AI_LIVE_TEST=1 to run the billable live acceptance test.";
        }
    }
}
