using System.Diagnostics;
using Google.Cloud.DocumentAI.V1;
using Google.Protobuf;
using Grpc.Core;
using SuperScanner.Application.Ocr;

namespace SuperScanner.Infrastructure.Ocr;

public sealed class GoogleDocumentAiOcrProvider(
    IDocumentAiClient client,
    GoogleDocumentAiOptions options,
    OcrMetrics? metrics = null) : IOcrProvider
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
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await client.ProcessAsync(content, input.MediaType, cancellationToken);
            if (response?.Document is null)
                throw new OcrProviderException("ocr_invalid_response", retryable: false);

            var result = DocumentAiResultMapper.Map(response.Document);
            metrics?.ProviderRequest("google_document_ai", "ready", stopwatch.Elapsed.TotalMilliseconds);
            return result;
        }
        catch (OperationCanceledException)
        {
            metrics?.ProviderRequest("google_document_ai", "cancelled", stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
        catch (RpcException exception)
        {
            var translated = Translate(exception.StatusCode);
            metrics?.ProviderRequest("google_document_ai", translated.SafeCode,
                stopwatch.Elapsed.TotalMilliseconds);
            throw translated;
        }
        catch (OcrProviderException exception)
        {
            metrics?.ProviderRequest("google_document_ai", exception.SafeCode,
                stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
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

    private static OcrProviderException Translate(StatusCode statusCode) => statusCode switch
    {
        StatusCode.DeadlineExceeded => new("ocr_timeout", retryable: true),
        StatusCode.ResourceExhausted => new("ocr_rate_limited", retryable: true),
        StatusCode.Unavailable or StatusCode.Internal =>
            new("ocr_provider_unavailable", retryable: true),
        StatusCode.Unauthenticated or StatusCode.PermissionDenied =>
            new("ocr_auth_failed", retryable: false),
        StatusCode.InvalidArgument => new("ocr_unsupported_media", retryable: false),
        _ => new("ocr_failed", retryable: false)
    };
}
