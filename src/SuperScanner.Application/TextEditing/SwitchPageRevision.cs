using System.Text.Json;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.TextEditing;

namespace SuperScanner.Application.TextEditing;

public enum RevisionSwitchDirection { Undo, Redo }

public sealed record SwitchPageRevisionRequest(string OwnerUid, Guid DocumentId,
    Guid PageId, Guid? ExpectedRevisionId, RevisionSwitchDirection Direction);

public sealed record LockedRevisionSwitchPage(Page Page, Document Document);

public interface ITextRevisionSwitchRepository
{
    Task<ITextEditTransaction> BeginAsync(CancellationToken ct);
    Task<LockedRevisionSwitchPage?> FindOwnedRevisionForUpdateAsync(string ownerUid,
        Guid documentId, Guid pageId, CancellationToken ct);
    Task<int> CountPendingAsync(Guid pageId, CancellationToken ct);
    Task<IReadOnlyList<PageRevision>> ListRevisionsAsync(Guid pageId, CancellationToken ct);
    Task<IReadOnlyList<TextEditOperation>> ListEditsAsync(Guid pageId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> ListAppliedRepairRevisionIdsAsync(Guid pageId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);
    Task SaveAsync(CancellationToken ct);
}

public sealed class TextRevisionConflictException : Exception;
public sealed class TextRevisionBoundaryException : Exception;

public sealed class SwitchPageRevision(
    ITextRevisionSwitchRepository repository,
    IAuditWriter audit,
    IClock clock)
{
    public async Task<Guid> HandleAsync(SwitchPageRevisionRequest request,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OwnerUid);
        if (!Enum.IsDefined(request.Direction))
            throw new ArgumentOutOfRangeException(nameof(request));
        await using var transaction = await repository.BeginAsync(ct);
        var owned = await repository.FindOwnedRevisionForUpdateAsync(request.OwnerUid,
            request.DocumentId, request.PageId, ct)
            ?? throw new TextSelectionNotFoundException();
        var page = owned.Page;
        if (page.RemovedAt is not null || page.State != PageState.Ready ||
            page.ActiveRevisionId != request.ExpectedRevisionId ||
            await repository.CountPendingAsync(page.Id, ct) > 0)
            throw new TextRevisionConflictException();

        var revisions = await repository.ListRevisionsAsync(page.Id, ct);
        var edits = await repository.ListEditsAsync(page.Id, ct);
        var repairRevisionIds = (await repository.ListAppliedRepairRevisionIdsAsync(page.Id, ct)).ToHashSet();
        PageRevision? target;
        if (request.Direction == RevisionSwitchDirection.Undo)
        {
            var parentId = page.ActiveRevision?.ParentRevisionId;
            target = parentId is null ? null : revisions.SingleOrDefault(revision =>
                revision.Id == parentId);
        }
        else
        {
            var successful = edits.Where(edit => edit.State == TextEditState.Succeeded &&
                edit.ResultRevisionId is not null)
                .ToDictionary(edit => edit.ResultRevisionId!.Value);
            target = revisions.Where(revision =>
                    revision.ParentRevisionId == page.ActiveRevisionId &&
                    (successful.ContainsKey(revision.Id) || repairRevisionIds.Contains(revision.Id)))
                .OrderByDescending(revision => revision.CreatedAt)
                .FirstOrDefault();
        }
        if (target is null) throw new TextRevisionBoundaryException();

        page.ActivateRevision(target);
        owned.Document.MarkContentChanged(clock.UtcNow);
        await audit.AppendAsync(new AuditWriteRequest(request.OwnerUid,
            request.Direction == RevisionSwitchDirection.Undo
                ? "text_edit.undo" : "text_edit.redo",
            "page", page.Id,
            JsonSerializer.Serialize(new { pageId = page.Id,
                fromRevisionId = request.ExpectedRevisionId,
                toRevisionId = target.Id }), clock.UtcNow), ct);
        await repository.SaveAsync(ct);
        await transaction.CommitAsync(ct);
        return target.Id;
    }
}
