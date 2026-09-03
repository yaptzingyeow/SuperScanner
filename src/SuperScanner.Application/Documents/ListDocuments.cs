using SuperScanner.Application.Abstractions;

namespace SuperScanner.Application.Documents;

public sealed class ListDocuments(IDocumentRepository documents)
{
    public async Task<IReadOnlyList<DocumentSummary>> HandleAsync(
        string ownerFirebaseUid,
        CancellationToken cancellationToken)
    {
        var ownedDocuments = await documents.ListByOwnerAsync(ownerFirebaseUid, cancellationToken);

        return ownedDocuments
            .Select(document => new DocumentSummary(
                document.Id,
                document.Title,
                document.Status.ToString(),
                document.Pages.Count,
                document.UpdatedAt))
            .ToList();
    }
}
