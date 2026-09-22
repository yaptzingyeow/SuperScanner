using Google.Protobuf;
using SuperScanner.Application.Ocr;

namespace SuperScanner.Infrastructure.Ocr;

public sealed class GoogleDocumentAiOcrProvider(
    IDocumentAiClient client,
    GoogleDocumentAiOptions options) : IOcrProvider
{
    private const int BufferSize = 81_920;

    public async Task<NormalizedOcrDocument> RecognizeAsync(
        OcrInput input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(input);

        if (input.Content is null || !input.Content.CanRead ||
            !string.Equals(input.Language, "en", StringComparison.Ordinal) ||
            input.MediaType is not ("image/jpeg" or "image/png"))
        {
            throw new OcrProviderException("ocr_unsupported_media", retryable: false);
        }

        var content = await ReadBoundedAsync(input.Content, options.MaxInputBytes, cancellationToken);
        var response = await client.ProcessAsync(content, input.MediaType, cancellationToken);
        if (response?.Document is null)
            throw new OcrProviderException("ocr_invalid_response", retryable: false);

        return DocumentAiResultMapper.Map(response.Document);
    }

    private static async Task<ByteString> ReadBoundedAsync(
        Stream source,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        await using var destination = new MemoryStream();

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;

            if (destination.Length + read > maxBytes)
                throw new OcrProviderException("ocr_unsupported_media", retryable: false);

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (destination.Length == 0)
            throw new OcrProviderException("ocr_invalid_response", retryable: false);

        return ByteString.CopyFrom(destination.GetBuffer(), 0, checked((int)destination.Length));
    }
}
