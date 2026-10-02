using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using SuperScanner.Application.Ocr;
using SuperScanner.Domain.Ocr;

namespace SuperScanner.Infrastructure.Ocr;

public sealed class TesseractOptions
{
    /// <summary>The tesseract executable: a full path, or a name found on PATH.</summary>
    public string ExecutablePath { get; init; } = "tesseract";
    /// <summary>Tesseract language pack(s), e.g. "eng".</summary>
    public string Languages { get; init; } = "eng";
    /// <summary>Page segmentation mode; 3 = fully automatic (columns, blocks, lines).</summary>
    public int PageSegmentationMode { get; init; } = 3;

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(ExecutablePath) &&
        !string.IsNullOrWhiteSpace(Languages) &&
        Languages.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '_') &&
        PageSegmentationMode is >= 0 and <= 13;
}

/// <summary>
/// Free, local OCR: runs the Tesseract command-line engine on the worker and maps its
/// word-level TSV output to blocks, lines and words, like the Google provider.
/// </summary>
public sealed class TesseractOcrProvider(TesseractOptions options) : IOcrProvider
{
    public async Task<NormalizedOcrDocument> RecognizeAsync(OcrInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Content is null || !input.Content.CanRead ||
            !string.Equals(input.Language, "en", StringComparison.Ordinal) ||
            input.MediaType is not ("image/jpeg" or "image/png"))
        {
            throw new OcrProviderException("ocr_unsupported_media", false);
        }

        var imagePath = Path.Combine(Path.GetTempPath(),
            $"ocr-{Guid.NewGuid():N}{(input.MediaType == "image/png" ? ".png" : ".jpg")}");
        try
        {
            await using (var file = File.Create(imagePath))
                await input.Content.CopyToAsync(file, ct);
            if (new FileInfo(imagePath).Length == 0)
                throw new OcrProviderException("ocr_invalid_response", false);

            var tsv = await RunAsync(imagePath, ct);
            return Parse(tsv, await VersionAsync(ct));
        }
        finally
        {
            try { File.Delete(imagePath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private async Task<string> RunAsync(string imagePath, CancellationToken ct)
    {
        var start = Start();
        foreach (var argument in new[]
                 {
                     imagePath, "stdout", "-l", options.Languages,
                     "--psm", options.PageSegmentationMode.ToString(CultureInfo.InvariantCulture), "tsv",
                 })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = StartOrThrow(start);
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(ct);
            var error = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            await error;
            if (process.ExitCode != 0)
                throw new OcrProviderException("ocr_engine_failed", false);
            return await output;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
    }

    private async Task<string> VersionAsync(CancellationToken ct)
    {
        var start = Start();
        start.ArgumentList.Add("--version");
        using var process = StartOrThrow(start);
        var text = await process.StandardOutput.ReadToEndAsync(ct) + await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "tesseract";
        return first.Length > 128 ? first[..128] : first;
    }

    private ProcessStartInfo Start() => new(options.ExecutablePath)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8,
    };

    private static Process StartOrThrow(ProcessStartInfo start)
    {
        try
        {
            return Process.Start(start) ?? throw new OcrProviderException("ocr_engine_missing", false);
        }
        catch (Win32Exception)
        {
            throw new OcrProviderException("ocr_engine_missing", false);
        }
    }

    /// <summary>Maps Tesseract TSV (level, page, block, par, line, word, left, top, width, height, conf, text).</summary>
    public static NormalizedOcrDocument Parse(string tsv, string modelVersion = "tesseract")
    {
        ArgumentNullException.ThrowIfNull(tsv);
        double pageWidth = 0, pageHeight = 0;
        var blocks = new List<BlockBuilder>();
        var blockByNumber = new Dictionary<int, BlockBuilder>();

        foreach (var raw in tsv.Split('\n'))
        {
            var columns = raw.TrimEnd('\r').Split('\t');
            if (columns.Length < 12 ||
                !int.TryParse(columns[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
            {
                continue; // header or blank line
            }

            var box = new Box(Int(columns[6]), Int(columns[7]), Int(columns[8]), Int(columns[9]));
            if (level == 1)
            {
                pageWidth = box.Width;
                pageHeight = box.Height;
                continue;
            }

            var text = string.Join(' ', columns[11..]).Trim();
            if (level != 5 || text.Length == 0) continue;
            var confidence = double.TryParse(columns[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? Math.Clamp(value / 100, 0, 1)
                : 0;

            var blockNumber = Int(columns[2]);
            if (!blockByNumber.TryGetValue(blockNumber, out var block))
            {
                block = new BlockBuilder();
                blockByNumber[blockNumber] = block;
                blocks.Add(block);
            }

            block.Line((Int(columns[3]), Int(columns[4]))).Add(new Word(text, confidence, box));
        }

        if (pageWidth <= 0 || pageHeight <= 0)
            return new(string.Empty, OcrProviderNames.Tesseract, modelVersion, []);

        var elements = new List<NormalizedOcrElement>();
        var blockTexts = new List<string>();
        for (var b = 0; b < blocks.Count; b++)
        {
            var blockId = $"b{b + 1}";
            var lineTexts = new List<string>();
            var children = new List<NormalizedOcrElement>();
            var blockWords = new List<Word>();
            for (var l = 0; l < blocks[b].Lines.Count; l++)
            {
                var words = blocks[b].Lines[l];
                var lineId = $"{blockId}-l{l + 1}";
                var lineText = string.Join(' ', words.Select(word => word.Text));
                lineTexts.Add(lineText);
                children.Add(Element(lineId, blockId, OcrElementKind.Line, lineText,
                    words.Average(word => word.Confidence), l, Union(words), pageWidth, pageHeight));
                for (var w = 0; w < words.Count; w++)
                {
                    children.Add(Element($"{lineId}-w{w + 1}", lineId, OcrElementKind.Word, words[w].Text,
                        words[w].Confidence, w, words[w].Box, pageWidth, pageHeight));
                }

                blockWords.AddRange(words);
            }

            var blockText = string.Join('\n', lineTexts);
            blockTexts.Add(blockText);
            elements.Add(Element(blockId, null, OcrElementKind.Block, blockText,
                blockWords.Average(word => word.Confidence), b, Union(blockWords), pageWidth, pageHeight));
            elements.AddRange(children);
        }

        return new(string.Join("\n\n", blockTexts), OcrProviderNames.Tesseract, modelVersion, elements);
    }

    private static NormalizedOcrElement Element(string id, string? parentId, OcrElementKind kind, string text,
        double confidence, int readingOrder, Box box, double pageWidth, double pageHeight)
    {
        double X(int value) => Math.Clamp(value / pageWidth, 0, 1);
        double Y(int value) => Math.Clamp(value / pageHeight, 0, 1);
        return new(id, parentId, kind, text, confidence, OcrTextType.Printed, readingOrder,
        [
            new(X(box.Left), Y(box.Top)),
            new(X(box.Left + box.Width), Y(box.Top)),
            new(X(box.Left + box.Width), Y(box.Top + box.Height)),
            new(X(box.Left), Y(box.Top + box.Height)),
        ]);
    }

    private static Box Union(IReadOnlyCollection<Word> words)
    {
        var left = words.Min(word => word.Box.Left);
        var top = words.Min(word => word.Box.Top);
        var right = words.Max(word => word.Box.Left + word.Box.Width);
        var bottom = words.Max(word => word.Box.Top + word.Box.Height);
        return new(left, top, right - left, bottom - top);
    }

    private static int Int(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;

    private readonly record struct Box(int Left, int Top, int Width, int Height);

    private sealed record Word(string Text, double Confidence, Box Box);

    private sealed class BlockBuilder
    {
        private readonly Dictionary<(int Paragraph, int Line), List<Word>> byKey = [];
        public List<List<Word>> Lines { get; } = [];

        public List<Word> Line((int Paragraph, int Line) key)
        {
            if (byKey.TryGetValue(key, out var line)) return line;
            line = [];
            byKey[key] = line;
            Lines.Add(line);
            return line;
        }
    }
}
