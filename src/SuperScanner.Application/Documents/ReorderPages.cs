using SuperScanner.Application.Abstractions;

namespace SuperScanner.Application.Documents;

public sealed record ReorderPagesRequest(long? ExpectedPageOrderRevision, IReadOnlyList<Guid>? PageIds);
public sealed record OrderedPage(Guid Id, int Position);
public sealed record PageOrderResult(long Revision, long PageOrderRevision, IReadOnlyList<OrderedPage> Pages);

public sealed class PageManagementNotFoundException() : Exception("Document or page not found.");
public sealed class PageOrderConflictException(long pageOrderRevision) : Exception("Page order revision is stale.")
{
    public long PageOrderRevision { get; } = pageOrderRevision;
}

public sealed class ReorderPages(IDocumentRepository documents, IClock clock, IAuditWriter audit)
{
    public async Task<PageOrderResult> HandleAsync(string ownerUid, Guid documentId,
        ReorderPagesRequest request, CancellationToken ct)
    {
        await using var transaction = await documents.BeginTransactionAsync(ct);
        var document = await documents.FindOwnedForUpdateAsync(ownerUid, documentId, ct)
            ?? throw new PageManagementNotFoundException();
        if (request.ExpectedPageOrderRevision is null or < 0)
            throw new ArgumentException("A non-negative expected page order revision is required.", "expectedPageOrderRevision");
        if (request.ExpectedPageOrderRevision != document.PageOrderRevision)
            throw new PageOrderConflictException(document.PageOrderRevision);
        if (request.PageIds is null)
            throw new ArgumentException("Page order must contain every active page exactly once.", "pageIds");

        var now = clock.UtcNow;
        document.ReorderPages(request.PageIds, request.ExpectedPageOrderRevision.Value, now);
        await audit.AppendAsync(new AuditWriteRequest(ownerUid, "document.pages_reordered", "document",
            document.Id, "{}", now), ct);
        await documents.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new PageOrderResult(document.Revision, document.PageOrderRevision,
            document.ActivePages.Select(p => new OrderedPage(p.Id, p.Position)).ToArray());
    }
}
