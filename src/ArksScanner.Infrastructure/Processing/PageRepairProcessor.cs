using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ArksScanner.Application.Abstractions;
using ArksScanner.Domain.Ocr;
using ArksScanner.Infrastructure.Persistence;

namespace ArksScanner.Infrastructure.Processing;

public sealed class PageRepairProcessor(AppDbContext db, IObjectStore store, IConfiguration configuration)
{
    public async Task RunAsync(Guid operationId, CancellationToken ct)
    {
        var operation = await db.PageRepairOperations.SingleOrDefaultAsync(x => x.Id == operationId, ct);
        if (operation is null || operation.State != "Queued") return;
        var directory = Directory.CreateTempSubdirectory("arksscanner-repair-");
        try
        {
            var input = Path.Combine(directory.FullName, "source.img");
            var output = Path.Combine(directory.FullName, "preview.png");
            await using (var source = await store.OpenReadAsync(operation.SourceObjectKey, ct))
            await using (var target = File.Create(input))
            {
                var buffer = new byte[81920];
                long total = 0; int count;
                while ((count = await source.ReadAsync(buffer, ct)) > 0)
                {
                    total += count;
                    if (total > 25 * 1024 * 1024) throw new InvalidDataException("Repair source too large.");
                    await target.WriteAsync(buffer.AsMemory(0, count), ct);
                }
            }
            var ocr = await db.PageOcrResults.AsNoTracking().Include(x => x.Elements)
                .Where(x => x.PageId == operation.PageId &&
                    x.SourceObjectKey == operation.SourceObjectKey && x.State == OcrResultState.Ready)
                .OrderByDescending(x => x.CompletedAt).FirstOrDefaultAsync(ct);
            var protectedBoxes = ocr?.Elements.Where(x => x.Kind == OcrElementKind.Word)
                .Select(x => new[] { x.Polygon.Min(p => p.X), x.Polygon.Min(p => p.Y),
                    x.Polygon.Max(p => p.X), x.Polygon.Max(p => p.Y) }).ToList() ?? [];
            var signatures = await db.PageSignatures.AsNoTracking()
                .Where(x => x.PageId == operation.PageId && x.DeletedAt == null)
                .Select(x => x.Box).ToArrayAsync(ct);
            var marks = await db.PageMarks.AsNoTracking()
                .Where(x => x.PageId == operation.PageId && x.DeletedAt == null)
                .Select(x => x.Box).ToArrayAsync(ct);
            foreach (var box in signatures.Concat(marks))
                protectedBoxes.Add([box.X, box.Y, box.X + box.Width, box.Y + box.Height]);
            var payloadPath = Path.Combine(directory.FullName, "request.json");
            if (operation.Kind == "Suggest")
            {
                await File.WriteAllTextAsync(payloadPath, JsonSerializer.Serialize(protectedBoxes), ct);
                var suggestions = await RunPythonAsync(["detect", input, "@" + payloadPath], ct);
                using var parsed = JsonDocument.Parse(suggestions);
                var candidates = parsed.RootElement.GetProperty("candidates");
                if (candidates.GetArrayLength() > 20 || suggestions.Length > 2048)
                    throw new InvalidDataException("Invalid hole suggestions.");
                operation.CompleteSuggestions(candidates.GetRawText());
                await db.SaveChangesAsync(ct);
                return;
            }
            var request = JsonSerializer.Serialize(new {
                rectangles = JsonSerializer.Deserialize<double[][]>(operation.RectanglesJson),
                strokes = JsonSerializer.Deserialize<JsonElement>(operation.StrokesJson),
                @protected = protectedBoxes, normalized = true
            });
            await File.WriteAllTextAsync(payloadPath, request, ct);
            await RunPythonAsync(["preview", input, output, "@" + payloadPath], ct);
            if (!File.Exists(output) || new FileInfo(output).Length > 20 * 1024 * 1024)
                throw new InvalidDataException("Repair preview failed.");
            var key = $"repairs/{operation.PageId:N}/{operation.Id:N}.png";
            await using (var stream = File.OpenRead(output))
                await store.WriteIfAbsentAsync(key, "image/png", stream, ct);
            operation.CompletePreview(key);
            await db.SaveChangesAsync(ct);
        }
        finally { directory.Delete(true); }
    }

    private async Task<string> RunPythonAsync(string[] arguments, CancellationToken ct)
    {
        var start = new ProcessStartInfo(configuration["Crop:PythonPath"] ?? "python3") {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add(configuration["Repair:ScriptPath"] ??
                Path.Combine(AppContext.BaseDirectory, "processing", "repair_image.py"));
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Repair runtime unavailable.");
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(true);
                await process.WaitForExitAsync(CancellationToken.None);
                if (ct.IsCancellationRequested) throw;
                throw new TimeoutException("Repair preview timed out.");
            }
            var result = await stdout;
            var errors = await stderr;
            if (process.ExitCode != 0 || result.Length > 4096)
            {
                // Keep the script's own reason (last line, trimmed) for the worker log.
                var reason = errors.Trim().Split('\n').LastOrDefault()?.Trim() ?? string.Empty;
                throw new InvalidDataException(
                    $"Repair preview failed (exit {process.ExitCode}): {reason[..Math.Min(reason.Length, 300)]}");
            }
            return result;
    }

    public async Task FailAsync(Guid operationId, CancellationToken ct)
    {
        var operation = await db.PageRepairOperations.SingleOrDefaultAsync(x => x.Id == operationId, ct);
        operation?.Fail();
        await db.SaveChangesAsync(ct);
    }
}
