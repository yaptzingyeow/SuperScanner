using ArksScanner.Domain.TextEditing;

namespace ArksScanner.Application.TextEditing;

public interface ITextEditReadRepository
{
    Task<IReadOnlyList<Guid>> ListAppliedRepairRevisionIdsAsync(Guid pageId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);
    Task<bool> IsOwnedPageAsync(string ownerUid, Guid documentId,
        Guid pageId, CancellationToken ct);
    Task<TextEditOperation?> FindAsync(Guid pageId, Guid editId, CancellationToken ct);
    Task<IReadOnlyList<TextEditOperation>> ListAsync(Guid pageId, CancellationToken ct);
    Task<PageEditRevisionState> GetRevisionStateAsync(Guid pageId, CancellationToken ct);
}

public sealed record PageEditRevision(Guid Id, Guid? ParentRevisionId);
public sealed record PageEditRevisionState(Guid? ActiveRevisionId,
    IReadOnlyList<PageEditRevision> Revisions);
public sealed record PageEditHistoryDto(bool CanUndo, bool CanRedo,
    Guid? ActiveRevisionId, IReadOnlyList<TextEditDto> Entries);

public sealed record TextEditDto(
    Guid Id,
    Guid SourceRevisionId,
    Guid? ResultRevisionId,
    string State,
    string OriginalText,
    string ReplacementText,
    NormalizedBox ReplacementBox,
    TextEditStyle Style,
    string? FailureCode,
    DateTimeOffset QueuedAt,
    DateTimeOffset? CompletedAt)
{
    public static TextEditDto From(TextEditOperation edit) => new(
        edit.Id, edit.SourceRevisionId, edit.ResultRevisionId,
        edit.State.ToString(), edit.OriginalText, edit.ReplacementText,
        edit.ReplacementBox, edit.Style, edit.FailureCode,
        edit.QueuedAt, edit.CompletedAt);
}

public sealed class GetTextEdit(ITextEditReadRepository repository)
{
    public async Task<TextEditDto> HandleAsync(string ownerUid, Guid documentId,
        Guid pageId, Guid editId, CancellationToken ct)
    {
        if (!await repository.IsOwnedPageAsync(ownerUid, documentId, pageId, ct))
            throw new TextSelectionNotFoundException();
        var edit = await repository.FindAsync(pageId, editId, ct)
            ?? throw new TextSelectionNotFoundException();
        return TextEditDto.From(edit);
    }
}
