using System.Text;
using Google.Cloud.DocumentAI.V1;
using SuperScanner.Application.Ocr;

namespace SuperScanner.Infrastructure.Ocr;

public static class DocumentAiTextAnchorReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string Read(string text, Document.Types.TextAnchor? anchor)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (anchor is null || anchor.TextSegments.Count == 0)
            return string.Empty;

        var bytes = Encoding.UTF8.GetBytes(text);
        var result = new StringBuilder();

        foreach (var segment in anchor.TextSegments)
        {
            var start = segment.StartIndex;
            var end = segment.EndIndex;
            if (start < 0 || end < start || end > bytes.LongLength ||
                start > int.MaxValue || end - start > int.MaxValue)
            {
                throw InvalidResponse();
            }

            try
            {
                result.Append(StrictUtf8.GetString(bytes, (int)start, (int)(end - start)));
            }
            catch (DecoderFallbackException)
            {
                throw InvalidResponse();
            }
        }

        return result.ToString();
    }

    private static OcrProviderException InvalidResponse() =>
        new("ocr_invalid_response", retryable: false);
}
