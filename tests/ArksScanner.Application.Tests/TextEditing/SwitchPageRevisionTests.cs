using ArksScanner.Application.Abstractions;
using ArksScanner.Application.TextEditing;
using ArksScanner.Domain.Documents;
using ArksScanner.Domain.TextEditing;

namespace ArksScanner.Application.Tests.TextEditing;

public sealed class SwitchPageRevisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Applied_cleanup_can_be_undone_and_redone_without_a_text_edit()
    {
        var fixture = new Fixture();
        var parentId = fixture.Page.ActiveRevisionId!.Value;
        var repair = PageRevision.CreateRepair(Guid.NewGuid(), fixture.Page.Id,
            parentId, "repairs/page.png", new string('b', 64), Now.AddSeconds(1));
        fixture.Revisions.Add(repair);
        fixture.Repository.AppliedRepairs.Add(repair.Id);
        fixture.Page.ActivateRevision(repair);

        await fixture.Switch.HandleAsync(new SwitchPageRevisionRequest("owner",
            fixture.Document.Id, fixture.Page.Id, repair.Id,
            RevisionSwitchDirection.Undo), default);
        Assert.Equal(parentId, fixture.Page.ActiveRevisionId);

        await fixture.Switch.HandleAsync(new SwitchPageRevisionRequest("owner",
            fixture.Document.Id, fixture.Page.Id, parentId,
            RevisionSwitchDirection.Redo), default);
        Assert.Equal(repair.Id, fixture.Page.ActiveRevisionId);
    }

    [Fact]
    public async Task Undo_then_redo_switches_persisted_active_revision()
    {
        var fixture = new Fixture();
        fixture.AddSucceededEdit("A");
        fixture.AddSucceededEdit("B");
        var expectedB = fixture.Page.ActiveRevisionId;

        await fixture.Switch.HandleAsync(new SwitchPageRevisionRequest("owner",
            fixture.Document.Id, fixture.Page.Id, expectedB, RevisionSwitchDirection.Undo), default);
        Assert.Equal(fixture.Revisions[^2].Id, fixture.Page.ActiveRevisionId);
        await fixture.Switch.HandleAsync(new SwitchPageRevisionRequest("owner",
            fixture.Document.Id, fixture.Page.Id, fixture.Page.ActiveRevisionId,
            RevisionSwitchDirection.Redo), default);
        Assert.Equal(expectedB, fixture.Page.ActiveRevisionId);
        Assert.Equal(2, fixture.Audit.Events.Count);
    }

    [Fact]
    public async Task Jump_goes_straight_to_any_saved_version_and_back()
    {
        var fixture = new Fixture();
        fixture.AddSucceededEdit("A");
        fixture.AddSucceededEdit("B");
        var latest = fixture.Page.ActiveRevisionId;
        var original = fixture.Revisions[0].Id;

        await fixture.Switch.HandleAsync(new SwitchPageRevisionRequest("owner",
            fixture.Document.Id, fixture.Page.Id, latest, RevisionSwitchDirection.To, original), default);
        Assert.Equal(original, fixture.Page.ActiveRevisionId);
        await fixture.Switch.HandleAsync(new SwitchPageRevisionRequest("owner",
            fixture.Document.Id, fixture.Page.Id, original, RevisionSwitchDirection.To, latest), default);
        Assert.Equal(latest, fixture.Page.ActiveRevisionId);
        Assert.Equal("text_edit.jump", fixture.Audit.Events[^1].Action);
    }

    [Fact]
    public async Task Jump_to_an_unknown_or_current_version_is_rejected()
    {
        var fixture = new Fixture();
        fixture.AddSucceededEdit("A");
        var current = fixture.Page.ActiveRevisionId;

        await Assert.ThrowsAsync<TextRevisionBoundaryException>(() => fixture.Switch.HandleAsync(new SwitchPageRevisionRequest("owner",
            fixture.Document.Id, fixture.Page.Id, current, RevisionSwitchDirection.To, Guid.NewGuid()), default));
        await Assert.ThrowsAsync<TextRevisionBoundaryException>(() => fixture.Switch.HandleAsync(new SwitchPageRevisionRequest("owner",
            fixture.Document.Id, fixture.Page.Id, current, RevisionSwitchDirection.To, current), default));
    }

    [Fact]
    public async Task New_edit_after_undo_supersedes_old_redo_branch()
    {
        var fixture = new Fixture();
        fixture.AddSucceededEdit("A");
        fixture.AddSucceededEdit("B");
        await fixture.Switch.HandleAsync(new SwitchPageRevisionRequest("owner",
            fixture.Document.Id, fixture.Page.Id, fixture.Page.ActiveRevisionId,
            RevisionSwitchDirection.Undo), default);
        fixture.AddSucceededEdit("C");
        await Assert.ThrowsAsync<TextRevisionBoundaryException>(() =>
            fixture.Switch.HandleAsync(new SwitchPageRevisionRequest("owner",
                fixture.Document.Id, fixture.Page.Id, fixture.Page.ActiveRevisionId,
                RevisionSwitchDirection.Redo), default));
        Assert.Equal(4, fixture.Revisions.Count);
    }

    [Fact]
    public async Task Pending_edit_blocks_revision_switch()
    {
        var fixture = new Fixture();
        fixture.AddSucceededEdit("A");
        fixture.Repository.Pending = 1;
        await Assert.ThrowsAsync<TextRevisionConflictException>(() =>
            fixture.Switch.HandleAsync(new SwitchPageRevisionRequest("owner",
                fixture.Document.Id, fixture.Page.Id, fixture.Page.ActiveRevisionId,
                RevisionSwitchDirection.Undo), default));
    }

    [Fact]
    public async Task Stale_expected_revision_blocks_switch()
    {
        var fixture = new Fixture();
        fixture.AddSucceededEdit("A");
        await Assert.ThrowsAsync<TextRevisionConflictException>(() =>
            fixture.Switch.HandleAsync(new SwitchPageRevisionRequest("owner",
                fixture.Document.Id, fixture.Page.Id, Guid.NewGuid(),
                RevisionSwitchDirection.Undo), default));
        Assert.Empty(fixture.Audit.Events);
    }

    [Fact]
    public async Task Cross_owner_request_does_not_reveal_revision()
    {
        var fixture = new Fixture();
        fixture.AddSucceededEdit("A");
        await Assert.ThrowsAsync<TextSelectionNotFoundException>(() =>
            fixture.Switch.HandleAsync(new SwitchPageRevisionRequest("outsider",
                fixture.Document.Id, fixture.Page.Id, fixture.Page.ActiveRevisionId,
                RevisionSwitchDirection.Undo), default));
    }

    [Fact]
    public async Task Undo_and_redo_at_boundaries_are_rejected()
    {
        var fixture = new Fixture();
        await Assert.ThrowsAsync<TextRevisionBoundaryException>(() =>
            fixture.Switch.HandleAsync(new SwitchPageRevisionRequest("owner",
                fixture.Document.Id, fixture.Page.Id, fixture.Page.ActiveRevisionId,
                RevisionSwitchDirection.Undo), default));
        await Assert.ThrowsAsync<TextRevisionBoundaryException>(() =>
            fixture.Switch.HandleAsync(new SwitchPageRevisionRequest("owner",
                fixture.Document.Id, fixture.Page.Id, fixture.Page.ActiveRevisionId,
                RevisionSwitchDirection.Redo), default));
    }

    [Fact]
    public async Task Processing_or_removed_page_cannot_switch_revision()
    {
        var processing = new Fixture();
        processing.AddSucceededEdit("A");
        processing.Page.MarkProcessing();
        await Assert.ThrowsAsync<TextRevisionConflictException>(() =>
            processing.Switch.HandleAsync(new SwitchPageRevisionRequest("owner",
                processing.Document.Id, processing.Page.Id,
                processing.Page.ActiveRevisionId, RevisionSwitchDirection.Undo), default));

        var removed = new Fixture();
        removed.AddSucceededEdit("A");
        removed.Page.SoftRemove("owner", Now);
        await Assert.ThrowsAsync<TextRevisionConflictException>(() =>
            removed.Switch.HandleAsync(new SwitchPageRevisionRequest("owner",
                removed.Document.Id, removed.Page.Id,
                removed.Page.ActiveRevisionId, RevisionSwitchDirection.Undo), default));
    }

    [Fact]
    public async Task Audit_metadata_never_includes_replaced_document_text()
    {
        var fixture = new Fixture();
        fixture.AddSucceededEdit("Private Name");
        await fixture.Switch.HandleAsync(new SwitchPageRevisionRequest("owner",
            fixture.Document.Id, fixture.Page.Id, fixture.Page.ActiveRevisionId,
            RevisionSwitchDirection.Undo), default);
        Assert.DoesNotContain("Private Name", fixture.Audit.Events.Single().RegionJson);
    }

    private sealed class Fixture
    {
        public Document Document { get; } = Document.Create(Guid.NewGuid(), "owner", "Test", Now);
        public Page Page { get; }
        public List<PageRevision> Revisions { get; } = [];
        public FakeRepository Repository { get; }
        public FakeAudit Audit { get; } = new();
        public SwitchPageRevision Switch { get; }

        public Fixture()
        {
            Page = Document.AddPage(Guid.NewGuid(), 10, Now);
            Page.MarkImportReady("original.jpg", "image/jpeg");
            Page.SetPreview("base.jpg", "thumb.jpg");
            Page.MarkReady();
            Document.MarkReady(Now);
            var baseRevision = PageRevision.CreateBase(Guid.NewGuid(), Page.Id,
                "base.jpg", new string('a', 64), Now);
            Revisions.Add(baseRevision);
            Page.ActivateRevision(baseRevision);
            Repository = new FakeRepository(Document, Page, Revisions);
            Switch = new SwitchPageRevision(Repository, Audit, new Clock());
        }

        public void AddSucceededEdit(string label)
        {
            var editId = Guid.NewGuid();
            var revision = PageRevision.CreateDerived(Guid.NewGuid(), Page.Id,
                Page.ActiveRevisionId!.Value, editId, $"{label}.jpg", new string('b', 64), Now);
            var edit = TextEditOperation.Queue(editId, Document.Id, Page.Id, "owner",
                Page.ActiveRevisionId.Value, Guid.NewGuid(), [Guid.NewGuid()],
                "Name", label, new NormalizedBox(.1, .2, .3, .1),
                new TextEditStyle("noto-sans", "archive-main-regular", .04,
                    400, "#000000", 0, .5, 0, TextAlignment.Left),
                Repository.Edits.Count + 1, Page.ActiveRevision?.ProducingTextEditId,
                $"key-{label}", new string('a', 64), "renderer-v1", "layout-v1", Now);
            edit.Start(Now);
            edit.Complete(revision.Id, Now);
            Repository.Edits.Add(edit);
            Revisions.Add(revision);
            Page.ActivateRevision(revision);
        }
    }

    private sealed class FakeRepository(Document document, Page page,
        List<PageRevision> revisions) : ITextRevisionSwitchRepository
    {
        public int Pending { get; set; }
        public List<TextEditOperation> Edits { get; } = [];
        public List<Guid> AppliedRepairs { get; } = [];
        public Task<ITextEditTransaction> BeginAsync(CancellationToken ct) =>
            Task.FromResult<ITextEditTransaction>(new Transaction());
        public Task<LockedRevisionSwitchPage?> FindOwnedRevisionForUpdateAsync(string ownerUid,
            Guid documentId, Guid pageId, CancellationToken ct) =>
            Task.FromResult<LockedRevisionSwitchPage?>(ownerUid == "owner" &&
                documentId == document.Id && pageId == page.Id
                ? new LockedRevisionSwitchPage(page, document) : null);
        public Task<int> CountPendingAsync(Guid pageId, CancellationToken ct) =>
            Task.FromResult(Pending);
        public Task<IReadOnlyList<PageRevision>> ListRevisionsAsync(Guid pageId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PageRevision>>(revisions);
        public Task<IReadOnlyList<TextEditOperation>> ListEditsAsync(Guid pageId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TextEditOperation>>(Edits);
        public Task<IReadOnlyList<Guid>> ListAppliedRepairRevisionIdsAsync(Guid pageId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Guid>>(AppliedRepairs);
        public Task SaveAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Transaction : ITextEditTransaction
    {
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeAudit : IAuditWriter
    {
        public List<AuditWriteRequest> Events { get; } = [];
        public Task<Guid> AppendAsync(AuditWriteRequest request, CancellationToken ct)
        {
            Events.Add(request);
            return Task.FromResult(Guid.NewGuid());
        }
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Now; }
}
