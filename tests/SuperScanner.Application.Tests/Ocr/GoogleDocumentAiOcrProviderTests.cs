using Google.Cloud.DocumentAI.V1;
using Google.Protobuf;
using SuperScanner.Application.Ocr;
using SuperScanner.Infrastructure.Ocr;

namespace SuperScanner.Application.Tests.Ocr;

public sealed class GoogleDocumentAiOcrProviderTests
{
    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    public async Task RecognizeAsync_MapsSupportedImage(string mediaType)
    {
        var client = new RecordingClient(ValidResponse());
        var provider = Provider(client, maxInputBytes: 3);
        await using var content = new MemoryStream([1, 2, 3]);

        var result = await provider.RecognizeAsync(new(content, mediaType, "en"), default);

        Assert.Equal("A", result.FullText);
        Assert.Equal("GoogleDocumentAi", result.ProviderName);
        Assert.Single(result.Elements, element => element.Kind == Domain.Ocr.OcrElementKind.Word);
        Assert.Equal(1, client.CallCount);
        Assert.Equal(ByteString.CopyFrom([1, 2, 3]), client.Content);
        Assert.Equal(mediaType, client.MediaType);
    }

    [Theory]
    [InlineData("application/pdf", "en")]
    [InlineData("image/png", "ms")]
    public async Task RecognizeAsync_RejectsUnsupportedMediaOrLanguageBeforeClient(
        string mediaType,
        string language)
    {
        var client = new RecordingClient(ValidResponse());
        var provider = Provider(client);
        await using var content = new MemoryStream([1]);

        var error = await Assert.ThrowsAsync<OcrProviderException>(() =>
            provider.RecognizeAsync(new(content, mediaType, language), default));

        Assert.Equal("ocr_unsupported_media", error.SafeCode);
        Assert.False(error.Retryable);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task RecognizeAsync_RejectsEmptyStreamBeforeClient()
    {
        var client = new RecordingClient(ValidResponse());
        var provider = Provider(client);
        await using var content = new MemoryStream();

        var error = await Assert.ThrowsAsync<OcrProviderException>(() =>
            provider.RecognizeAsync(new(content, "image/jpeg", "en"), default));

        Assert.Equal("ocr_invalid_response", error.SafeCode);
        Assert.False(error.Retryable);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task RecognizeAsync_ReadsNonSeekableStream()
    {
        var client = new RecordingClient(ValidResponse());
        var provider = Provider(client);
        await using var content = new NonSeekableStream([4, 5]);

        await provider.RecognizeAsync(new(content, "image/jpeg", "en"), default);

        Assert.Equal(ByteString.CopyFrom([4, 5]), client.Content);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public async Task RecognizeAsync_EnforcesInputLimitBeforeClient(int byteCount, bool accepted)
    {
        var client = new RecordingClient(ValidResponse());
        var provider = Provider(client, maxInputBytes: 3);
        await using var content = new MemoryStream(Enumerable.Repeat((byte)1, byteCount).ToArray());

        if (accepted)
        {
            await provider.RecognizeAsync(new(content, "image/png", "en"), default);
            Assert.Equal(1, client.CallCount);
        }
        else
        {
            var error = await Assert.ThrowsAsync<OcrProviderException>(() =>
                provider.RecognizeAsync(new(content, "image/png", "en"), default));
            Assert.Equal("ocr_unsupported_media", error.SafeCode);
            Assert.False(error.Retryable);
            Assert.Equal(0, client.CallCount);
        }
    }

    [Fact]
    public async Task RecognizeAsync_ObservesCancellationBeforeCopy()
    {
        var client = new RecordingClient(ValidResponse());
        var provider = Provider(client);
        await using var content = new MemoryStream([1]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.RecognizeAsync(new(content, "image/jpeg", "en"), cancellation.Token));

        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task RecognizeAsync_ObservesCancellationDuringCopy()
    {
        var client = new RecordingClient(ValidResponse());
        var provider = Provider(client);
        using var cancellation = new CancellationTokenSource();
        await using var content = new CancelingStream(cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.RecognizeAsync(new(content, "image/jpeg", "en"), cancellation.Token));

        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task RecognizeAsync_RejectsResponseWithoutDocument()
    {
        var client = new RecordingClient(new ProcessResponse());
        var provider = Provider(client);
        await using var content = new MemoryStream([1]);

        var error = await Assert.ThrowsAsync<OcrProviderException>(() =>
            provider.RecognizeAsync(new(content, "image/jpeg", "en"), default));

        Assert.Equal("ocr_invalid_response", error.SafeCode);
        Assert.False(error.Retryable);
    }

    private static GoogleDocumentAiOcrProvider Provider(
        IDocumentAiClient client,
        long maxInputBytes = 10) => new(client, new GoogleDocumentAiOptions
        {
            ProjectId = "superscanner-dev",
            Location = "asia-southeast1",
            ProcessorId = "fc0b14e64c62e7aa",
            MaxInputBytes = maxInputBytes
        });

    private static ProcessResponse ValidResponse()
    {
        var anchor = new Document.Types.TextAnchor();
        anchor.TextSegments.Add(new Document.Types.TextAnchor.Types.TextSegment { EndIndex = 1 });
        var polygon = new BoundingPoly();
        polygon.NormalizedVertices.Add([
            new NormalizedVertex { X = .1f, Y = .1f },
            new NormalizedVertex { X = .9f, Y = .1f },
            new NormalizedVertex { X = .9f, Y = .2f },
            new NormalizedVertex { X = .1f, Y = .2f }
        ]);
        Document.Types.Page.Types.Layout Layout() => new()
        {
            TextAnchor = anchor.Clone(),
            BoundingPoly = polygon.Clone(),
            Confidence = .9f
        };

        var page = new Document.Types.Page
        {
            PageNumber = 1,
            Dimension = new Document.Types.Page.Types.Dimension { Width = 100, Height = 100 }
        };
        page.Blocks.Add(new Document.Types.Page.Types.Block { Layout = Layout() });
        page.Paragraphs.Add(new Document.Types.Page.Types.Paragraph { Layout = Layout() });
        page.Tokens.Add(new Document.Types.Page.Types.Token { Layout = Layout() });
        var document = new Document { Text = "A" };
        document.Pages.Add(page);
        return new ProcessResponse { Document = document };
    }

    private sealed class RecordingClient(ProcessResponse response) : IDocumentAiClient
    {
        public int CallCount { get; private set; }
        public ByteString? Content { get; private set; }
        public string? MediaType { get; private set; }

        public Task<ProcessResponse> ProcessAsync(
            ByteString content,
            string mediaType,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Content = content;
            MediaType = mediaType;
            return Task.FromResult(response);
        }
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private sealed class CancelingStream(CancellationTokenSource cancellation) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return ValueTask.FromCanceled<int>(cancellationToken);
        }
    }
}
