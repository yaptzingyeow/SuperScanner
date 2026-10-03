using ArksScanner.Application.Abstractions;
using ArksScanner.Application.Plans;
using ArksScanner.Domain.Documents;

namespace ArksScanner.Application.Documents;

public sealed record DocumentSummary(
    Guid Id,
    string Title,
    string Status,
    int PageCount,
    DateTimeOffset UpdatedAt);

public sealed class CreateDocument(IDocumentRepository documents, IClock clock, IAuditWriter audit,
    PlanService? plans = null)
{
    public async Task<DocumentSummary> HandleAsync(
        string ownerFirebaseUid,
        string title,
        CancellationToken cancellationToken)
    {
        if (plans is not null) await plans.EnsureCanCreateDocumentAsync(ownerFirebaseUid, cancellationToken);
        var document = Document.Create(Guid.NewGuid(), ownerFirebaseUid, title, clock.UtcNow);
        await documents.AddAsync(document, cancellationToken);
        await audit.AppendAsync(
            new AuditWriteRequest(
                ownerFirebaseUid,
                "document.created",
                "document",
                document.Id,
                "{}",
                clock.UtcNow),
            cancellationToken);

        return new DocumentSummary(
            document.Id,
            document.Title,
            document.Status.ToString(),
            document.Pages.Count,
            document.UpdatedAt);
    }
}
