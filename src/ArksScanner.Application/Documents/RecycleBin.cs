using ArksScanner.Application.Abstractions;
using ArksScanner.Application.Plans;
using ArksScanner.Domain.Documents;

namespace ArksScanner.Application.Documents;

public sealed record BinEntry(Guid Id, string Title, DateTimeOffset DeletedAt, DateTimeOffset PurgeAfter);

/// <summary>Owner-deleted documents stay restorable for <see cref="Days"/> days.</summary>
public sealed class RecycleBin(IDocumentRepository documents, PlanService plans, IClock clock, IAuditWriter audit)
{
    public const int Days = 30;

    public async Task<IReadOnlyList<BinEntry>> ListAsync(string ownerUid, CancellationToken ct) =>
        (await documents.ListDeletedByOwnerAsync(ownerUid, clock.UtcNow.AddDays(-Days), ct))
            .Select(d => new BinEntry(d.Id, d.Title, d.RemovedAt!.Value, d.RemovedAt.Value.AddDays(Days)))
            .ToList();

    /// <summary>Restores a binned document; counts against the plan's document limit like a new one.</summary>
    public async Task<DocumentSummary> RestoreAsync(string ownerUid, Guid documentId, CancellationToken ct)
    {
        await plans.EnsureCanCreateDocumentAsync(ownerUid, ct);
        await using var transaction = await documents.BeginTransactionAsync(ct);
        var now = clock.UtcNow;
        var document = await documents.FindOwnedDeletedForUpdateAsync(ownerUid, documentId, now.AddDays(-Days), ct)
            ?? throw new DocumentNotFoundException();
        document.Restore(now);
        await audit.AppendAsync(new AuditWriteRequest(ownerUid, "document.restored", "document", document.Id,
            "{}", now), ct);
        await documents.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new DocumentSummary(document.Id, document.Title, document.Status.ToString(),
            document.ActivePages.Count, document.UpdatedAt);
    }
}
