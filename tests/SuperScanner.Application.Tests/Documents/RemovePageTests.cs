using Microsoft.EntityFrameworkCore;
using SuperScanner.Application.Documents;
using SuperScanner.Domain.Documents;

namespace SuperScanner.Application.Tests.Documents;

public sealed class RemovePageTests
{
    [Fact]
    public async Task Remove_SoftDeletesCompactsAndAuditsInOneSave()
    {
        await using var fixture = await PageMutationFixture.CreateAsync();
        await fixture.Remove.HandleAsync("user-a", fixture.DocumentId, fixture.PageIds[1], default);
        var document = await fixture.ReloadAsync();
        Assert.Equal(new[] { fixture.PageIds[0], fixture.PageIds[2] }, document.ActivePages.Select(p => p.Id));
        Assert.Equal(new[] { 1, 2 }, document.ActivePages.Select(p => p.Position));
        var removed = document.Pages.Single(p => p.Id == fixture.PageIds[1]);
        Assert.Equal(fixture.Clock.UtcNow, removed.RemovedAt);
        Assert.Equal("user-a", removed.RemovedByFirebaseUid);
        Assert.Equal(2, document.PageOrderRevision);
        Assert.Equal(2, document.Revision);
        var audit = await fixture.Db.AuditEvents.SingleAsync();
        Assert.Equal("document.page_removed", audit.Action);
        Assert.Equal(fixture.DocumentId, audit.TargetId);
        Assert.Contains(fixture.PageIds[1].ToString(), audit.RegionJson);
        Assert.Equal(1, fixture.SaveCounter.Calls);
    }

    [Fact]
    public async Task RemovingFinalPage_PersistsEmptyDraft()
    {
        await using var fixture = await PageMutationFixture.CreateAsync(1);
        await fixture.Remove.HandleAsync("user-a", fixture.DocumentId, fixture.PageIds[0], default);
        var document = await fixture.ReloadAsync();
        Assert.Empty(document.ActivePages);
        Assert.Equal(DocumentStatus.Draft, document.Status);
        Assert.Equal(2, document.Revision);
    }

    [Theory]
    [InlineData("unowned")]
    [InlineData("missing-document")]
    [InlineData("foreign")]
    [InlineData("removed")]
    [InlineData("missing-page")]
    public async Task InaccessibleResource_IsHiddenAndUnchanged(string kind)
    {
        await using var fixture = await PageMutationFixture.CreateAsync();
        var pageId = kind switch
        {
            "foreign" => fixture.ForeignPageId,
            "removed" => fixture.RemovedPageId,
            "missing-page" => Guid.NewGuid(),
            _ => fixture.PageIds[0]
        };
        await Assert.ThrowsAsync<PageManagementNotFoundException>(() => fixture.Remove.HandleAsync(
            kind == "unowned" ? "user-b" : "user-a",
            kind == "missing-document" ? Guid.NewGuid() : fixture.DocumentId, pageId, default));
        await fixture.AssertUnchangedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_RollsBackRemovalCompactionRevisionsAndAudit(bool failDuringSave)
    {
        await using var fixture = await PageMutationFixture.CreateAsync();
        fixture.SaveCounter.Fail = failDuringSave;
        var handler = failDuringSave ? fixture.Remove : new RemovePage(fixture.Repository,
            fixture.Clock, new FailingAuditWriter(fixture.Audit));
        await Assert.ThrowsAsync<IOException>(() => handler.HandleAsync("user-a", fixture.DocumentId,
            fixture.PageIds[0], default));
        await fixture.AssertUnchangedAsync();
    }
}
