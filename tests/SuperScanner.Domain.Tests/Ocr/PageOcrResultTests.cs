using SuperScanner.Domain.Ocr;

namespace SuperScanner.Domain.Tests.Ocr;

public sealed class PageOcrResultTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-22T00:00:00Z");
    private static readonly string Fingerprint = new('a', 64);
    private static readonly OcrPoint[] Polygon =
    [
        new(.1, .1),
        new(.9, .1),
        new(.9, .2),
        new(.1, .2)
    ];

    [Fact]
    public void Complete_AllowsEmptyRecognitionAndLeavesConfidenceUnset()
    {
        var result = CreateQueued();
        result.BeginAttempt(1, Now.AddSeconds(1));

        result.Complete("Fake", "fixture-v1", string.Empty, [], Now.AddSeconds(2));

        Assert.Equal(OcrResultState.Ready, result.State);
        Assert.Empty(result.Elements);
        Assert.Equal(string.Empty, result.FullText);
        Assert.Equal(0, result.ElementCount);
        Assert.Null(result.AggregateConfidence);
    }

    [Fact]
    public void Complete_PreservesHierarchyOrderAndComputesConfidence()
    {
        var result = CreateQueued();
        var block = OcrElement.Create(Guid.NewGuid(), result.Id, null, OcrElementKind.Block,
            "Name", .8, OcrTextType.Printed, 0, Polygon);
        var line = OcrElement.Create(Guid.NewGuid(), result.Id, block.Id, OcrElementKind.Line,
            "Name", .9, OcrTextType.Printed, 0, Polygon);
        var word = OcrElement.Create(Guid.NewGuid(), result.Id, line.Id, OcrElementKind.Word,
            "Name", 1, OcrTextType.Handwritten, 0, Polygon);
        result.BeginAttempt(1, Now.AddSeconds(1));

        result.Complete("Fake", "fixture-v1", "Name", [block, line, word], Now.AddSeconds(2));

        Assert.Equal([block.Id, line.Id, word.Id], result.Elements.Select(x => x.Id));
        Assert.NotNull(result.AggregateConfidence);
        Assert.Equal(.9, result.AggregateConfidence.Value, 10);
        Assert.Equal(3, result.ElementCount);
        Assert.Equal(OcrTextType.Handwritten, result.Elements.Last().TextType);
    }

    [Fact]
    public void BeginAttempt_AllowsAReclaimedProcessingLeaseToAdvanceAttempt()
    {
        var result = CreateQueued();
        result.BeginAttempt(1, Now.AddSeconds(1));

        result.BeginAttempt(2, Now.AddSeconds(5));

        Assert.Equal(OcrResultState.Processing, result.State);
        Assert.Equal(2, result.AttemptCount);
        Assert.Equal(Now.AddSeconds(1), result.StartedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fail_FromPendingState_CanRetryOnlyWhenMarkedRetryable(bool afterStart)
    {
        var result = CreateQueued();
        if (afterStart) result.BeginAttempt(1, Now.AddSeconds(1));

        result.Fail("ocr_provider_unavailable", true, Now.AddSeconds(2));
        result.Retry(Now.AddSeconds(3));

        Assert.Equal(OcrResultState.Queued, result.State);
        Assert.Equal(0, result.AttemptCount);
        Assert.Null(result.FailureCode);
        Assert.Null(result.StartedAt);
        Assert.Null(result.CompletedAt);
    }

    [Fact]
    public void Retry_RejectsPermanentFailure()
    {
        var result = CreateQueued();
        result.Fail("ocr_invalid_response", false, Now.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(() => result.Retry(Now.AddSeconds(2)));
    }

    [Fact]
    public void CompletedResult_RejectsFurtherTransitions()
    {
        var result = CreateQueued();
        result.BeginAttempt(1, Now.AddSeconds(1));
        result.Complete("Fake", "fixture-v1", string.Empty, [], Now.AddSeconds(2));

        Assert.Throws<InvalidOperationException>(() => result.BeginAttempt(2, Now.AddSeconds(3)));
        Assert.Throws<InvalidOperationException>(() => result.Fail("ocr_failed", true, Now.AddSeconds(3)));
        Assert.Throws<InvalidOperationException>(() => result.Complete(
            "Fake", "fixture-v1", string.Empty, [], Now.AddSeconds(3)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void Queue_RejectsInvalidSourceFingerprint(string fingerprint) =>
        Assert.Throws<ArgumentException>(() => PageOcrResult.Queue(Guid.NewGuid(), Guid.NewGuid(),
            "previews/d/p/crop-1.jpg", fingerprint, "en", Now));

    [Fact]
    public void Queue_NormalizesUppercaseFingerprint()
    {
        var result = PageOcrResult.Queue(Guid.NewGuid(), Guid.NewGuid(),
            "previews/d/p/crop-1.jpg", new string('A', 64), "en", Now);

        Assert.Equal(Fingerprint, result.SourceFingerprint);
    }

    [Theory]
    [InlineData(-0.01, 0.5)]
    [InlineData(1.01, 0.5)]
    [InlineData(double.NaN, 0.5)]
    [InlineData(0.5, double.PositiveInfinity)]
    public void Element_RejectsCoordinatesOutsideNormalizedImage(double x, double y) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => OcrElement.Create(
            Guid.NewGuid(), Guid.NewGuid(), null, OcrElementKind.Word, "Name", .95,
            OcrTextType.Printed, 0,
            [new(x, y), new(.9, .1), new(.9, .2), new(.1, .2)]));

    [Fact]
    public void Element_RejectsPolygonWithoutFourPoints() =>
        Assert.Throws<ArgumentException>(() => OcrElement.Create(
            Guid.NewGuid(), Guid.NewGuid(), null, OcrElementKind.Word, "Name", .95,
            OcrTextType.Printed, 0, [new(.1, .1), new(.9, .1), new(.9, .2)]));

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void Element_RejectsInvalidConfidence(double confidence) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => OcrElement.Create(
            Guid.NewGuid(), Guid.NewGuid(), null, OcrElementKind.Word, "Name", confidence,
            OcrTextType.Printed, 0, Polygon));

    [Fact]
    public void Element_RejectsNegativeReadingOrder() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => OcrElement.Create(
            Guid.NewGuid(), Guid.NewGuid(), null, OcrElementKind.Word, "Name", .95,
            OcrTextType.Printed, -1, Polygon));

    [Fact]
    public void Complete_RejectsElementOwnedByAnotherResult()
    {
        var result = CreateQueued();
        var foreign = OcrElement.Create(Guid.NewGuid(), Guid.NewGuid(), null,
            OcrElementKind.Block, "Name", .9, OcrTextType.Printed, 0, Polygon);
        result.BeginAttempt(1, Now.AddSeconds(1));

        Assert.Throws<ArgumentException>(() => result.Complete(
            "Fake", "fixture-v1", "Name", [foreign], Now.AddSeconds(2)));
    }

    [Fact]
    public void Complete_RejectsMissingParent()
    {
        var result = CreateQueued();
        var orphan = OcrElement.Create(Guid.NewGuid(), result.Id, Guid.NewGuid(),
            OcrElementKind.Word, "Name", .9, OcrTextType.Printed, 0, Polygon);
        result.BeginAttempt(1, Now.AddSeconds(1));

        Assert.Throws<ArgumentException>(() => result.Complete(
            "Fake", "fixture-v1", "Name", [orphan], Now.AddSeconds(2)));
    }

    [Fact]
    public void Complete_RejectsCyclicHierarchy()
    {
        var result = CreateQueued();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = OcrElement.Create(firstId, result.Id, secondId, OcrElementKind.Block,
            "A", .9, OcrTextType.Printed, 0, Polygon);
        var second = OcrElement.Create(secondId, result.Id, firstId, OcrElementKind.Line,
            "A", .9, OcrTextType.Printed, 0, Polygon);
        result.BeginAttempt(1, Now.AddSeconds(1));

        Assert.Throws<ArgumentException>(() => result.Complete(
            "Fake", "fixture-v1", "A", [first, second], Now.AddSeconds(2)));
    }

    private static PageOcrResult CreateQueued() => PageOcrResult.Queue(
        Guid.NewGuid(), Guid.NewGuid(), "previews/d/p/crop-2.jpg", Fingerprint, "en", Now);
}
