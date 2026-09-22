using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;

namespace SuperScanner.Application.Documents;

public sealed record DocumentExportResult(Guid Id, string State, long DocumentRevision,
    int ReadyPageCount, int ExcludedPageCount, string StatusUrl, string? DownloadUrl,
    bool IsOutdated, string? FailureCode, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt,
    DateTimeOffset ExpiresAt)
{
    internal static DocumentExportResult From(DocumentExport export, long documentRevision, DateTimeOffset now)
    {
        var statusUrl = $"/api/documents/{export.DocumentId}/exports/{export.Id}";
        var failureCode = export.FailureCode switch
        {
            null => null,
            "export_asset_missing" or "export_decode_failed" or "export_size_limit" or "export_build_failed" => export.FailureCode,
            _ => "export_build_failed"
        };
        return new(export.Id, export.State.ToString(), export.DocumentRevision,
            export.ReadyPageCount, export.ExcludedPageCount, statusUrl,
            export.State == DocumentExportState.Ready && export.ExpiresAt > now ? $"{statusUrl}/download" : null,
            export.DocumentRevision != documentRevision, failureCode,
            export.CreatedAt, export.CompletedAt, export.ExpiresAt);
    }
}

public sealed class GetDocumentExport(IDocumentExportRepository exports, IClock clock)
{
    public async Task<DocumentExportResult> HandleAsync(string ownerUid, Guid documentId, Guid exportId, CancellationToken ct)
    {
        var owned = await exports.FindOwnedAsync(ownerUid, documentId, exportId, ct)
            ?? throw new DocumentExportNotFoundException();
        return DocumentExportResult.From(owned.Export, owned.CurrentDocumentRevision, clock.UtcNow);
    }
}
