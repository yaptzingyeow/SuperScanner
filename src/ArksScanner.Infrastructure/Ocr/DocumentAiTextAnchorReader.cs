using System.Text;
using Google.Cloud.DocumentAI.V1;
using ArksScanner.Application.Ocr;

namespace ArksScanner.Infrastructure.Ocr;

public static class DocumentAiTextAnchorReader
{
    public static string Read(string text, Document.Types.TextAnchor? anchor)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (anchor is null || anchor.TextSegments.Count == 0)
            return string.Empty;

        // Document AI anchors count Unicode characters, not UTF-8 bytes or
        // .NET UTF-16 code units. Translate scalar boundaries before slicing.
        var offsets = new List<int> { 0 };
        var offset = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            offset += rune.Utf16SequenceLength;
            offsets.Add(offset);
        }
        var result = new StringBuilder();

        foreach (var segment in anchor.TextSegments)
        {
            var start = segment.StartIndex;
            var end = segment.EndIndex;
            if (start < 0 || end < start || end >= offsets.Count)
            {
                throw InvalidResponse();
            }

            var utf16Start = offsets[(int)start];
            result.Append(text, utf16Start, offsets[(int)end] - utf16Start);
        }

        return result.ToString();
    }

    private static OcrProviderException InvalidResponse() =>
        new("ocr_invalid_response", retryable: false);
}
