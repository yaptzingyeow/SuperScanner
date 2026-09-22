using SuperScanner.Application.Ocr;
using SuperScanner.Domain.Ocr;

namespace SuperScanner.Infrastructure.Ocr;

public enum FakeOcrScenario
{
    Normal,
    Empty,
    Timeout,
    Invalid
}

public sealed class FakeOcrProvider(FakeOcrScenario scenario = FakeOcrScenario.Normal) : IOcrProvider
{
    private static readonly OcrPoint[] Polygon =
    [
        new(.1, .1), new(.9, .1), new(.9, .2), new(.1, .2)
    ];

    public async Task<NormalizedOcrDocument> RecognizeAsync(OcrInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Content is null || !input.Content.CanRead ||
            !string.Equals(input.Language, "en", StringComparison.Ordinal) ||
            input.MediaType is not ("image/jpeg" or "image/png"))
        {
            throw new OcrProviderException("ocr_unsupported_media", false);
        }

        var probe = new byte[1];
        if (await input.Content.ReadAsync(probe, ct) == 0)
            throw new OcrProviderException("ocr_invalid_response", false);

        if (scenario == FakeOcrScenario.Timeout)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }

        if (scenario == FakeOcrScenario.Empty)
            return new(string.Empty, "Fake", "fixture-v1", []);

        if (scenario == FakeOcrScenario.Invalid)
        {
            return new("Invalid", "Fake", "fixture-v1",
            [
                new("word", "missing", OcrElementKind.Word, "Invalid", .9,
                    OcrTextType.Printed, 0, Polygon)
            ]);
        }

        return new("Sample Name", "Fake", "fixture-v1",
        [
            new("block-1", null, OcrElementKind.Block, "Sample Name", .94,
                OcrTextType.Printed, 0, Polygon),
            new("line-1", "block-1", OcrElementKind.Line, "Sample Name", .95,
                OcrTextType.Printed, 0, Polygon),
            new("word-1", "line-1", OcrElementKind.Word, "Sample", .96,
                OcrTextType.Printed, 0, Polygon),
            new("word-2", "line-1", OcrElementKind.Word, "Name", .93,
                OcrTextType.Handwritten, 1, Polygon)
        ]);
    }
}
