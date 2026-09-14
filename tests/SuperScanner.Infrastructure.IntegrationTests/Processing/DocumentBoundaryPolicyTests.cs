using SuperScanner.Infrastructure.Processing;

namespace SuperScanner.Infrastructure.IntegrationTests.Processing;

public sealed class DocumentBoundaryPolicyTests
{
    [Fact]
    public void DefaultOptionsAreSafeAndValid()
    {
        var options = new DocumentBoundaryOptions();
        Assert.True(options.IsValid());
        Assert.Equal("OpenCvOnly", options.Mode);
        Assert.Equal(0, options.RolloutPercentage);
    }

    [Theory]
    [InlineData("Unknown", .78, .58, 20, 0)]
    [InlineData("AiPreferred", .50, .60, 20, 0)]
    [InlineData("AiPreferred", .78, .58, 26, 0)]
    [InlineData("AiPreferred", .78, .58, 20, 101)]
    public void RejectsUnsafeOptions(
        string mode, double high, double medium, int timeout, int rollout)
    {
        var options = new DocumentBoundaryOptions
        {
            Mode = mode,
            HighConfidence = high,
            MediumConfidence = medium,
            InferenceTimeoutSeconds = timeout,
            RolloutPercentage = rollout
        };
        Assert.False(options.IsValid());
    }

    [Fact]
    public void HealthTripsOnceAndRemainsUnhealthy()
    {
        var health = new DocumentBoundaryHealth();
        Assert.True(health.CanAttemptAi);
        health.MarkUnhealthy("ai_checksum_invalid");
        health.MarkUnhealthy("replacement");
        Assert.False(health.CanAttemptAi);
        Assert.Equal("ai_checksum_invalid", health.UnhealthyCode);
    }

    [Fact]
    public void RolloutIsDeterministicAndHonorsBounds()
    {
        var pageId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        Assert.False(DocumentBoundaryRollout.ShouldUseAi(pageId, 0));
        Assert.True(DocumentBoundaryRollout.ShouldUseAi(pageId, 100));
        Assert.Equal(
            DocumentBoundaryRollout.ShouldUseAi(pageId, 37),
            DocumentBoundaryRollout.ShouldUseAi(pageId, 37));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void RolloutRejectsInvalidPercentage(int percentage)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DocumentBoundaryRollout.ShouldUseAi(Guid.NewGuid(), percentage));
    }
}
