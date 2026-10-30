using System.Text.Json;
using ArksScanner.Application.Abstractions;
using ArksScanner.Domain.Documents;

namespace ArksScanner.Application.Documents;

public sealed class DocumentNotFoundException() : Exception("Document not found.");

/// <summary>Deletes a document the owner no longer wants (soft removal, like retention, so support can restore it).</summary>
public sealed class DeleteDocument(IDocumentRepository documents, IClock clock, IAuditWriter audit)
{
    /// <param name="savedToDevice">The owner kept the document on their phone: skip the bin and purge it soon.</param>
    public async Task HandleAsync(string ownerUid, Guid documentId, CancellationToken ct, bool savedToDevice = false)
    {
        await using var transaction = await documents.BeginTransactionAsync(ct);
        var document = await documents.FindOwnedForUpdateAsync(ownerUid, documentId, ct)
            ?? throw new DocumentNotFoundException();
        var now = clock.UtcNow;
        document.Remove(savedToDevice ? Document.SavedToDeviceReason : Document.UserDeletedReason, now);
        await audit.AppendAsync(new AuditWriteRequest(ownerUid, "document.deleted", "document", document.Id,
            savedToDevice ? """{"savedToDevice":true}""" : "{}", now), ct);
        await documents.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}

public sealed class RenameDocument(IDocumentRepository documents, IClock clock, IAuditWriter audit)
{
    public async Task<DocumentSummary> HandleAsync(string ownerUid, Guid documentId, string title, CancellationToken ct)
    {
        await using var transaction = await documents.BeginTransactionAsync(ct);
        var document = await documents.FindOwnedForUpdateAsync(ownerUid, documentId, ct)
            ?? throw new DocumentNotFoundException();
        var now = clock.UtcNow;
        document.Rename(title, now);
        await audit.AppendAsync(new AuditWriteRequest(ownerUid, "document.renamed", "document", document.Id,
            JsonSerializer.Serialize(new { titleLength = document.Title.Length }), now), ct);
        await documents.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new DocumentSummary(document.Id, document.Title, document.Status.ToString(),
            document.ActivePages.Count, document.UpdatedAt);
    }
}
