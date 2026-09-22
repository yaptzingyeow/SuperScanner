using SuperScanner.Application.Ocr;
using SuperScanner.Infrastructure.Ocr;

namespace SuperScanner.Application.Tests.Ocr;

public sealed class FakeOcrProviderTests
{
    [Fact]
    public async Task DisabledProvider_ReturnsPermanentSafeFailure()
    {
        var provider = new DisabledOcrProvider();
        await using var content = new MemoryStream([1]);

        var error = await Assert.ThrowsAsync<OcrProviderException>(() =>
            provider.RecognizeAsync(new(content, "image/jpeg", "en"), default));

        Assert.Equal("ocr_disabled", error.SafeCode);
        Assert.False(error.Retryable);
    }

    [Fact]
    public async Task Provider_ReturnsSameHierarchyForAnySupportedImage()
    {
        var provider = new FakeOcrProvider();
        await using var first = new MemoryStream([0xff, 0xd8, 0xff, 0xd9]);
        await using var second = new MemoryStream([0x89, 0x50, 0x4e, 0x47]);

        var firstResult = await provider.RecognizeAsync(new(first, "image/jpeg", "en"), default);
        var secondResult = await provider.RecognizeAsync(new(second, "image/png", "en"), default);

        Assert.Equal(firstResult.FullText, secondResult.FullText);
        Assert.Equal(firstResult.Elements.Count, secondResult.Elements.Count);
        for (var index = 0; index < firstResult.Elements.Count; index++)
        {
            var expected = firstResult.Elements[index];
            var actual = secondResult.Elements[index];
            Assert.Equal(expected with { Polygon = actual.Polygon }, actual);
            Assert.Equal(expected.Polygon, actual.Polygon);
        }
    }

    [Theory]
    [InlineData("application/pdf", "en")]
    [InlineData("image/jpeg", "ms")]
    public async Task Provider_RejectsUnsupportedMediaOrLanguage(string mediaType, string language)
    {
        var provider = new FakeOcrProvider();
        await using var content = new MemoryStream([1]);

        var error = await Assert.ThrowsAsync<OcrProviderException>(() =>
            provider.RecognizeAsync(new(content, mediaType, language), default));

        Assert.Equal("ocr_unsupported_media", error.SafeCode);
        Assert.False(error.Retryable);
    }

    [Fact]
    public async Task EmptyScenario_ReturnsValidEmptyRecognition()
    {
        var provider = new FakeOcrProvider(FakeOcrScenario.Empty);
        await using var content = new MemoryStream([1]);

        var result = await provider.RecognizeAsync(new(content, "image/jpeg", "en"), default);

        Assert.Empty(result.FullText);
        Assert.Empty(result.Elements);
        Assert.Same(result, OcrResultValidator.Validate(result, new(10, 100)));
    }

    [Fact]
    public async Task TimeoutScenario_ObservesCancellation()
    {
        var provider = new FakeOcrProvider(FakeOcrScenario.Timeout);
        await using var content = new MemoryStream([1]);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.RecognizeAsync(new(content, "image/jpeg", "en"), cancellation.Token));
    }

    [Theory]
    [InlineData(false, "Disabled", "Development", true)]
    [InlineData(true, "Fake", "Development", true)]
    [InlineData(true, "Fake", "Production", false)]
    [InlineData(true, "Disabled", "Development", false)]
    [InlineData(false, "Fake", "Development", false)]
    [InlineData(true, "GoogleDocumentAi", "Development", false)]
    [InlineData(true, "Fake", "Development", true, 6)]
    [InlineData(true, "Fake", "Development", false, 7)]
    public void Options_ValidateProviderAndEnvironment(
        bool enabled,
        string provider,
        string environment,
        bool expected,
        int maxAttempts = 3)
    {
        var options = new OcrOptions
        {
            Enabled = enabled,
            Provider = provider,
            MaxAttempts = maxAttempts
        };

        Assert.Equal(expected, options.IsValid(environment));
    }
}
