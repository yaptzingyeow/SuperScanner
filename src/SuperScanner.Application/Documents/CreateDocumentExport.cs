using System.Text.Json;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;

namespace SuperScanner.Application.Documents;

public sealed class DocumentExportNotFoundException() : Exception("Document export not found.");
public sealed class DocumentExportNoReadyPagesException() : Exception("Document has no ready pages to export.");

public sealed class DocumentExportPolicy
{
    public DocumentExportPolicy(int retentionDays)
    {
        if (retentionDays <= 0) throw new ArgumentOutOfRangeException(nameof(retentionDays));
        Retention = TimeSpan.FromDays(retentionDays);
    }

    public TimeSpan Retention { get; }
}

public sealed class CreateDocumentExport(IDocumentRepository documents, IDocumentExportRepository exports,
    IOcrRepository ocr,
    IClock clock, IAuditWriter audit, IProcessingJobQueue queue, DocumentExportPolicy policy,
    IPageSignatureRepository signatures)
{
    public async Task<DocumentExportResult> HandleAsync(string ownerUid, Guid documentId, CancellationToken ct)
    {
        await using var transaction = await documents.BeginTransactionAsync(ct);
        var document = await documents.FindOwnedForUpdateAsync(ownerUid, documentId, ct)
            ?? throw new DocumentExportNotFoundException();
        if (!document.ActivePages.Any(page => page.State == PageState.Ready))
            throw new DocumentExportNoReadyPagesException();

        var now = clock.UtcNow;
        var readyPages = document.ActivePages.Where(page => page.State == PageState.Ready).ToArray();
        var results = await ocr.FindReadyByPageIdsAsync(readyPages.Select(page => page.Id).ToArray(), ct);
        var matchingOcr = DocumentExportOcrEligibility.Match(readyPages, results);
        var overlays = await signatures.GetActiveForDocumentAsync(document.Id, ct);
        var signatureSnapshots = overlays.OrderBy(s => s.CreatedAt).ThenBy(s => s.Id)
            .GroupBy(s => s.PageId).ToDictionary(g => g.Key,
                g => (IReadOnlyList<SignatureOverlaySnapshot>)g.Select(s =>
                    new SignatureOverlaySnapshot(s.Id, s.AssetKey, s.Box, s.ImageAspectRatio)).ToArray());
        var export = DocumentExport.Create(Guid.NewGuid(), document, ownerUid, now, policy.Retention, matchingOcr,
            signatureSnapshots);
        await exports.AddAsync(export, ct);
        await audit.AppendAsync(new AuditWriteRequest(ownerUid, "document.export_created", "document",
            document.Id, JsonSerializer.Serialize(new { exportId = export.Id }), now), ct);
        await queue.EnqueueAsync("BuildDocumentPdf", export.Id.ToString(), $"export:{export.Id}:build:v1", ct);
        await exports.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return DocumentExportResult.From(export, document.Revision, now);
    }
}
