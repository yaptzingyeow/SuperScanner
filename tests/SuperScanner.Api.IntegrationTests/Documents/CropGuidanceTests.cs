using SuperScanner.Api.Endpoints;

namespace SuperScanner.Api.IntegrationTests.Documents;

public sealed class CropGuidanceTests
{
    [Theory]
    [InlineData("Ai", .84, "accurate")]
    [InlineData("Ai", .66, "verify")]
    [InlineData("OpenCvFallback", .82, "verify")]
    [InlineData("FullImage", 0, "manual")]
    public void MapsBoundaryEvidenceToSafeUserGuidance(
        string source, double confidence, string expected)
    {
        Assert.Equal(expected, CropEndpoints.GetGuidance(source, confidence));
    }
}
