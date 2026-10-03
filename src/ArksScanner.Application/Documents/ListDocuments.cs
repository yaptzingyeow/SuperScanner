using ArksScanner.Application.Abstractions;
using ArksScanner.Application.Plans;

namespace ArksScanner.Application.Documents;

public sealed class ListDocuments(IDocumentRepository documents, PlanService? plans = null)
{
    public async Task<IReadOnlyList<DocumentSummary>> HandleAsync(
        string ownerFirebaseUid,
        CancellationToken cancellationToken)
    {
        var ownedDocuments = await documents.ListByOwnerAsync(ownerFirebaseUid, cancellationToken);
        var expiry = plans is null ? (_ => null) : await plans.GetExpiryRuleAsync(ownerFirebaseUid, cancellationToken);

        return ownedDocuments
            .Select(document => new DocumentSummary(
                document.Id,
                document.Title,
                document.Status.ToString(),
                document.Pages.Count,
                document.UpdatedAt,
                expiry(document.CreatedAt)))
            .ToList();
    }
}
