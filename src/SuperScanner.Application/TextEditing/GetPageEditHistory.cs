namespace SuperScanner.Application.TextEditing;

public sealed class GetPageEditHistory(ITextEditReadRepository repository)
{
    public async Task<IReadOnlyList<TextEditDto>> HandleAsync(string ownerUid,
        Guid documentId, Guid pageId, CancellationToken ct)
    {
        if (!await repository.IsOwnedPageAsync(ownerUid, documentId, pageId, ct))
            throw new TextSelectionNotFoundException();
        var edits = await repository.ListAsync(pageId, ct);
        return edits.Select(TextEditDto.From).ToArray();
    }
}
