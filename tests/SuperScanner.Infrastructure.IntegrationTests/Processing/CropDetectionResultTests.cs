using SuperScanner.Domain.Documents;
using SuperScanner.Infrastructure.Processing;

namespace SuperScanner.Infrastructure.IntegrationTests.Processing;

public sealed class CropDetectionResultTests
{
    private static readonly CropPoint[] ValidPoints =
    [
        new(0.1, 0.1), new(0.9, 0.1), new(0.9, 0.9), new(0.1, 0.9)
    ];

    [Fact]
    public void AiResultRequiresModelVersion()
    {
        var result = new CropDetectionResult(ValidPoints, .8, "Ai", null, "ai_candidate");
        Assert.False(result.IsValid());
    }

    [Theory]
    [InlineData("Ai")]
    [InlineData("OpenCvFallback")]
    [InlineData("FullImage")]
    public void AcceptsKnownSources(string source)
    {
        var version = source == "Ai" ? "u2netp-candidate-1" : null;
        var confidence = source == "FullImage" ? 0 : .8;
        Assert.True(new CropDetectionResult(ValidPoints, confidence, source, version, "ok").IsValid());
    }

    [Fact]
    public void RejectsUnknownSourceAndUnsafeDiagnostics()
    {
        Assert.False(new CropDetectionResult(ValidPoints, .8, "Cloud", null, "ok").IsValid());
        Assert.False(new CropDetectionResult(ValidPoints, .8, "OpenCvFallback", null, "bad path\\secret").IsValid());
    }
}
