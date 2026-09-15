using System.Text.Json;
using SuperScanner.Application.Abstractions;

namespace SuperScanner.Application.Documents;

public sealed class RemovePage(IDocumentRepository documents, IClock clock, IAuditWriter audit)
{
    public async Task HandleAsync(string ownerUid, Guid documentId, Guid pageId, CancellationToken ct)
    {
        await using var transaction = await documents.BeginTransactionAsync(ct);
        var document = await documents.FindOwnedForUpdateAsync(ownerUid, documentId, ct)
            ?? throw new PageManagementNotFoundException();
        if (!document.ActivePages.Any(p => p.Id == pageId)) throw new PageManagementNotFoundException();

        var now = clock.UtcNow;
        document.RemovePage(pageId, ownerUid, now);
        await audit.AppendAsync(new AuditWriteRequest(ownerUid, "document.page_removed", "document",
            document.Id, JsonSerializer.Serialize(new { pageId }), now), ct);
        await documents.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
