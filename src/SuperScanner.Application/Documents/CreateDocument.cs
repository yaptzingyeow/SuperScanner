using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;

namespace SuperScanner.Application.Documents;

public sealed record DocumentSummary(
    Guid Id,
    string Title,
    string Status,
    int PageCount,
    DateTimeOffset UpdatedAt);

public sealed class CreateDocument(IDocumentRepository documents, IClock clock, IAuditWriter audit)
{
    public async Task<DocumentSummary> HandleAsync(
        string ownerFirebaseUid,
        string title,
        CancellationToken cancellationToken)
    {
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
