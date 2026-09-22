using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace SuperScanner.Infrastructure.Processing;

public sealed class PopplerPdfImportTool(IOptions<DocumentImportOptions> options) : IPdfImportTool
{
    private const int RenderDpi = 300;

    public async Task<PdfInspection> InspectAsync(string sourcePath, CancellationToken ct)
    {
        var result = await RunAsync("pdfinfo", [sourcePath], options.Value.InspectTimeoutSeconds, ct);
        var output = result.StandardOutput;
        var encrypted = output.Contains("Encrypted:      yes", StringComparison.OrdinalIgnoreCase) ||
            result.StandardError.Contains("password", StringComparison.OrdinalIgnoreCase);
        if (encrypted) return new PdfInspection(0, true, 0);
        if (result.ExitCode != 0) throw new PdfImportException("pdf_invalid");

        var pages = ReadInt(output, "Pages:");
        if (pages < 1) throw new PdfImportException("pdf_invalid");
        if (pages > options.Value.MaxPagesPerImport) return new PdfInspection(pages, false, 0);

        long pixels = 0;
        using var inspectionBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        inspectionBudget.CancelAfter(TimeSpan.FromSeconds(options.Value.InspectTimeoutSeconds));
        try
        {
            for (var pageIndex = 1; pageIndex <= pages; pageIndex++)
            {
                var page = await RunAsync("pdfinfo", ["-f", pageIndex.ToString(CultureInfo.InvariantCulture),
                    "-l", pageIndex.ToString(CultureInfo.InvariantCulture), sourcePath], options.Value.InspectTimeoutSeconds,
                    inspectionBudget.Token);
                if (page.ExitCode != 0) throw new PdfImportException("pdf_invalid");
                var dimensions = ReadDimensions(page.StandardOutput) ?? throw new PdfImportException("pdf_invalid");
                if (dimensions.Width <= 0 || dimensions.Height <= 0) throw new PdfImportException("pdf_invalid");
                pixels = checked(pixels + (long)Math.Ceiling(dimensions.Width / 72d * RenderDpi) *
                    (long)Math.Ceiling(dimensions.Height / 72d * RenderDpi));
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new PdfImportException("pdf_timeout");
        }
        return new PdfInspection(pages, false, pixels);
    }

    public async Task RenderPageAsync(string sourcePath, int pageIndex, string outputPngPath, CancellationToken ct)
    {
        if (pageIndex < 1) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        var outputPrefix = Path.Combine(Path.GetDirectoryName(outputPngPath)!, Path.GetFileNameWithoutExtension(outputPngPath));
        var result = await RunAsync("pdftoppm",
            ["-r", RenderDpi.ToString(CultureInfo.InvariantCulture), "-f", pageIndex.ToString(CultureInfo.InvariantCulture), "-l", pageIndex.ToString(CultureInfo.InvariantCulture),
                "-singlefile", "-png", sourcePath, outputPrefix], options.Value.PageRenderTimeoutSeconds, ct);
        if (result.ExitCode != 0 || !File.Exists(outputPngPath)) throw new PdfImportException("pdf_render_failed");
    }

    private static async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, int timeoutSeconds, CancellationToken ct)
    {
        var start = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start) ?? throw new PdfImportException("pdf_tool_unavailable");
            var output = process.StandardOutput.ReadToEndAsync(ct);
            var error = process.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited) process.Kill(true);
                }
                catch (InvalidOperationException)
                {
                    // The process exited between the state check and the kill request.
                }
                await process.WaitForExitAsync(CancellationToken.None);
                if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                throw new PdfImportException("pdf_timeout");
            }

            return new ProcessResult(process.ExitCode, await output, await error);
        }
        catch (PdfImportException) { throw; }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            throw new PdfImportException("pdf_tool_unavailable", exception);
        }
    }

    private static int ReadInt(string output, string name)
    {
        var value = output.Split('\n').FirstOrDefault(line => line.TrimStart().StartsWith(name, StringComparison.OrdinalIgnoreCase));
        return value is null || !int.TryParse(value[(value.IndexOf(':') + 1)..].Trim(), out var parsed) ? 0 : parsed;
    }

    private static (double Width, double Height)? ReadDimensions(string output)
    {
        var value = output.Split('\n').FirstOrDefault(line => line.Contains("size:", StringComparison.OrdinalIgnoreCase));
        if (value is null) return null;
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var x = Array.IndexOf(words, "x");
        return x < 1 || x + 1 >= words.Length ||
            !double.TryParse(words[x - 1], CultureInfo.InvariantCulture, out var width) ||
            !double.TryParse(words[x + 1], CultureInfo.InvariantCulture, out var height)
            ? null : (width, height);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
