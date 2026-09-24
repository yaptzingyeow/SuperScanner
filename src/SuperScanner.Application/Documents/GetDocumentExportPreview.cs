using SuperScanner.Application.Abstractions;
using SuperScanner.Application.Ocr;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Ocr;

namespace SuperScanner.Application.Documents;

public sealed record DocumentExportPreviewResult(
    int ReadyPageCount,
    int ExcludedPageCount,
    int SearchablePageCount,
    string Searchability);

public sealed class GetDocumentExportPreview(IDocumentRepository documents, IOcrRepository ocr)
{
    public async Task<DocumentExportPreviewResult> HandleAsync(
        string ownerUid,
        Guid documentId,
        CancellationToken ct)
    {
        await using var transaction = await documents.BeginTransactionAsync(ct);
        var document = await documents.FindOwnedForUpdateAsync(ownerUid, documentId, ct)
            ?? throw new DocumentExportNotFoundException();
        var readyPages = document.ActivePages.Where(page => page.State == PageState.Ready).ToArray();
        var results = readyPages.Length == 0
            ? []
            : await ocr.FindReadyByPageIdsAsync(readyPages.Select(page => page.Id).ToArray(), ct);
        var searchablePageCount = DocumentExportOcrEligibility.Match(readyPages, results).Count;
        var searchability = searchablePageCount == 0
            ? "ImageOnly"
            : searchablePageCount == readyPages.Length
                ? "Searchable"
                : "PartiallySearchable";

        return new DocumentExportPreviewResult(
            readyPages.Length,
            document.ActivePages.Count - readyPages.Length,
            searchablePageCount,
            searchability);
    }
}

internal static class DocumentExportOcrEligibility
{
    public static IReadOnlyDictionary<Guid, DocumentExportOcrSnapshot> Match(
        IReadOnlyCollection<Page> readyPages,
        IReadOnlyCollection<PageOcrResult> results) =>
        results
            .Join(readyPages,
                result => result.PageId,
                page => page.Id,
                (result, page) => new { Result = result, SourceKey = page.GetExportObjectKey() })
            .Where(candidate =>
                candidate.Result.State == OcrResultState.Ready &&
                string.Equals(candidate.Result.Language, "en", StringComparison.Ordinal) &&
                string.Equals(candidate.Result.SourceObjectKey, candidate.SourceKey, StringComparison.Ordinal) &&
                string.Equals(candidate.Result.SourceFingerprint, OcrSourceFingerprint.Create(candidate.SourceKey),
                    StringComparison.Ordinal))
            .ToDictionary(candidate => candidate.Result.PageId, candidate => new DocumentExportOcrSnapshot(
                candidate.Result.Id, candidate.Result.SourceObjectKey, candidate.Result.SourceFingerprint));
}
