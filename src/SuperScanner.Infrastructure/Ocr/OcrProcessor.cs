using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SuperScanner.Application.Abstractions;
using SuperScanner.Application.Ocr;
using SuperScanner.Domain.Ocr;
using SuperScanner.Infrastructure.Persistence;

namespace SuperScanner.Infrastructure.Ocr;

public sealed class OcrProcessor(
    AppDbContext db,
    IObjectStore store,
    IOcrProvider provider,
    IClock clock,
    IOptions<OcrOptions> options,
    OcrMetrics metrics)
{
    public async Task RunAsync(Guid resultId, int attemptNumber, CancellationToken ct)
    {
        var result = await db.PageOcrResults.Include(candidate => candidate.Elements)
            .SingleOrDefaultAsync(candidate => candidate.Id == resultId, ct)
            ?? throw new OcrProviderException("ocr_invalid_job", false);
        if (result.State is OcrResultState.Ready or OcrResultState.Failed) return;

        result.BeginAttempt(attemptNumber, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        metrics.Started(Math.Max(0, (clock.UtcNow - result.QueuedAt).TotalMilliseconds),
            Math.Max(0, attemptNumber - 1));

        await using var content = await store.OpenReadAsync(result.SourceObjectKey, ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        var stopwatch = Stopwatch.StartNew();
        NormalizedOcrDocument providerResult;
        try
        {
            providerResult = await provider.RecognizeAsync(
                new(content, MediaTypeFor(result.SourceObjectKey), options.Value.Language), timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new OcrProviderException("ocr_timeout", true);
        }

        var normalized = OcrResultValidator.Validate(providerResult,
            new(options.Value.MaxElements, options.Value.MaxRecognizedCharacters));
        var elementIds = normalized.Elements.ToDictionary(
            element => element.ClientId, _ => Guid.NewGuid(), StringComparer.Ordinal);
        var elements = normalized.Elements.Select(element => OcrElement.Create(
            elementIds[element.ClientId],
            result.Id,
            element.ParentClientId is null ? null : elementIds[element.ParentClientId],
            element.Kind,
            element.Text,
            element.Confidence,
            element.TextType,
            element.ReadingOrder,
            element.Polygon)).ToArray();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        result.Complete(normalized.ProviderName, normalized.ModelVersion,
            normalized.FullText, elements, clock.UtcNow);
        db.OcrElements.AddRange(elements);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var currentKey = await db.Pages.Where(page => page.Id == result.PageId)
            .Select(page => page.PreviewObjectKey).SingleOrDefaultAsync(ct);
        metrics.Completed(normalized.ProviderName, stopwatch.Elapsed.TotalMilliseconds,
            elements.Length, result.AggregateConfidence,
            !string.Equals(currentKey, result.SourceObjectKey, StringComparison.Ordinal));
    }

    public async Task FailAsync(
        Guid resultId,
        string safeCode,
        bool retryable,
        CancellationToken ct)
    {
        var result = await db.PageOcrResults.SingleOrDefaultAsync(candidate => candidate.Id == resultId, ct);
        if (result is null || result.State is OcrResultState.Ready or OcrResultState.Failed) return;
        result.Fail(safeCode, retryable, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        metrics.Failed(safeCode, retryable);
    }

    private static string MediaTypeFor(string objectKey) => Path.GetExtension(objectKey).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        _ => "image/jpeg"
    };
}
