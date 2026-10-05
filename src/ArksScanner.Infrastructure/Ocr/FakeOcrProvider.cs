using ArksScanner.Application.Ocr;
using ArksScanner.Domain.Ocr;

namespace ArksScanner.Infrastructure.Ocr;

public enum FakeOcrScenario
{
    Normal,
    Empty,
    Timeout,
    Invalid,
    PrintedTextReplacement
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
            !OcrLanguage.IsValid(input.Language) ||
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

        if (scenario == FakeOcrScenario.PrintedTextReplacement)
        {
            OcrPoint[] printedLine = [
                new(.1, .105), new(.46, .105), new(.46, .15), new(.1, .15)
            ];
            return new("Yap Tzing Yeow", "Fake", "printed-edit-fixture-v1",
            [
                new("block-1", null, OcrElementKind.Block, "Yap Tzing Yeow", .99,
                    OcrTextType.Printed, 0, printedLine),
                new("line-1", "block-1", OcrElementKind.Line, "Yap Tzing Yeow", .99,
                    OcrTextType.Printed, 0, printedLine),
                new("word-1", "line-1", OcrElementKind.Word, "Yap", .99,
                    OcrTextType.Printed, 0, [new(.1, .105), new(.18, .105), new(.18, .15), new(.1, .15)]),
                new("word-2", "line-1", OcrElementKind.Word, "Tzing", .99,
                    OcrTextType.Printed, 1, [new(.19, .105), new(.31, .105), new(.31, .15), new(.19, .15)]),
                new("word-3", "line-1", OcrElementKind.Word, "Yeow", .99,
                    OcrTextType.Printed, 2, [new(.32, .105), new(.46, .105), new(.46, .15), new(.32, .15)])
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
