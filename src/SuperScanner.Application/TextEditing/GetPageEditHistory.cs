namespace SuperScanner.Application.TextEditing;

public sealed class GetPageEditHistory(ITextEditReadRepository repository)
{
    public async Task<PageEditHistoryDto> HandleAsync(string ownerUid,
        Guid documentId, Guid pageId, CancellationToken ct)
    {
        if (!await repository.IsOwnedPageAsync(ownerUid, documentId, pageId, ct))
            throw new TextSelectionNotFoundException();
        var edits = await repository.ListAsync(pageId, ct);
        var state = await repository.GetRevisionStateAsync(pageId, ct);
        var active = state.Revisions.SingleOrDefault(revision =>
            revision.Id == state.ActiveRevisionId);
        var busy = edits.Any(edit => edit.State is Domain.TextEditing.TextEditState.Queued or
            Domain.TextEditing.TextEditState.Processing);
        var successfulRevisionIds = edits.Where(edit =>
                edit.State == Domain.TextEditing.TextEditState.Succeeded &&
                edit.ResultRevisionId is not null)
            .Select(edit => edit.ResultRevisionId!.Value).ToHashSet();
        successfulRevisionIds.UnionWith(await repository.ListAppliedRepairRevisionIdsAsync(pageId, ct));
        return new PageEditHistoryDto(!busy && active?.ParentRevisionId is not null,
            !busy && state.Revisions.Any(revision =>
                revision.ParentRevisionId == state.ActiveRevisionId &&
                successfulRevisionIds.Contains(revision.Id)),
            state.ActiveRevisionId, edits.Select(TextEditDto.From).ToArray());
    }
}
