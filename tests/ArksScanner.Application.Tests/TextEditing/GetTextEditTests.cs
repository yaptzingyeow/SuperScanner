using ArksScanner.Application.TextEditing;
using ArksScanner.Domain.TextEditing;

namespace ArksScanner.Application.Tests.TextEditing;

public sealed class GetTextEditTests
{
    [Fact]
    public async Task Unowned_page_does_not_reveal_edit()
    {
        var repository = new FakeReadRepository { Owned = false };
        var query = new GetTextEdit(repository);
        await Assert.ThrowsAsync<TextSelectionNotFoundException>(() =>
            query.HandleAsync("outsider", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), default));
        Assert.Equal(0, repository.ReadCount);
    }

    [Fact]
    public async Task Owned_edit_returns_status_and_draft_fields()
    {
        var edit = Edit();
        var repository = new FakeReadRepository { Edit = edit };
        var result = await new GetTextEdit(repository).HandleAsync("owner", edit.DocumentId,
            edit.PageId, edit.Id, default);
        Assert.Equal("Tan BB", result.ReplacementText);
        Assert.Equal("Queued", result.State);
    }

    [Fact]
    public async Task History_is_owner_scoped_and_sequence_ordered()
    {
        var edit = Edit();
        var repository = new FakeReadRepository { Edit = edit };
        var history = await new GetPageEditHistory(repository).HandleAsync("owner",
            edit.DocumentId, edit.PageId, default);
        Assert.Single(history.Entries);
        Assert.Equal(edit.Id, history.Entries[0].Id);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public async Task History_exposes_undo_and_redo_for_active_branch()
    {
        var edit = Edit();
        var baseId = edit.SourceRevisionId;
        var childId = Guid.NewGuid();
        edit.Start(DateTimeOffset.UtcNow);
        edit.Complete(childId, DateTimeOffset.UtcNow);
        var repository = new FakeReadRepository
        {
            Edit = edit,
            RevisionState = new PageEditRevisionState(childId,
                [new(baseId, null), new(childId, baseId)])
        };
        var query = new GetPageEditHistory(repository);
        var atChild = await query.HandleAsync("owner", edit.DocumentId, edit.PageId, default);
        Assert.True(atChild.CanUndo);
        Assert.False(atChild.CanRedo);
        repository.RevisionState = repository.RevisionState with { ActiveRevisionId = baseId };
        var atBase = await query.HandleAsync("owner", edit.DocumentId, edit.PageId, default);
        Assert.False(atBase.CanUndo);
        Assert.True(atBase.CanRedo);
    }

    private static TextEditOperation Edit() => TextEditOperation.Queue(Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), "owner", Guid.NewGuid(), Guid.NewGuid(),
        [Guid.NewGuid()], "Name", "Tan BB", new NormalizedBox(.1, .2, .3, .1),
        new TextEditStyle("noto-sans", "archive-main-regular", .04, 400,
            "#000000", 0, .25, 0, TextAlignment.Left), 1, null, "key-1",
        new string('a', 64), "renderer-v1", "layout-v1", DateTimeOffset.UtcNow);

    private sealed class FakeReadRepository : ITextEditReadRepository
    {
        public bool Owned { get; set; } = true;
        public TextEditOperation? Edit { get; set; }
        public PageEditRevisionState RevisionState { get; set; } = new(null, []);
        public int ReadCount { get; private set; }
        public Task<bool> IsOwnedPageAsync(string ownerUid, Guid documentId,
            Guid pageId, CancellationToken ct) => Task.FromResult(Owned);
        public Task<TextEditOperation?> FindAsync(Guid pageId, Guid editId, CancellationToken ct)
        {
            ReadCount++;
            return Task.FromResult(Edit?.Id == editId && Edit.PageId == pageId ? Edit : null);
        }
        public Task<IReadOnlyList<TextEditOperation>> ListAsync(Guid pageId, CancellationToken ct)
        {
            ReadCount++;
            return Task.FromResult<IReadOnlyList<TextEditOperation>>(Edit?.PageId == pageId ? [Edit] : []);
        }
        public Task<PageEditRevisionState> GetRevisionStateAsync(Guid pageId,
            CancellationToken ct) => Task.FromResult(RevisionState);
    }
}
