using SuperScanner.Domain.Documents;

namespace SuperScanner.Domain.Tests.Documents;

public sealed class DocumentTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-14T00:00:00Z");

    [Fact]
    public void Create_TrimsTitleAndOwnsDocument()
    {
        var now = DateTimeOffset.Parse("2026-09-02T00:00:00Z");

        var document = Document.Create(Guid.NewGuid(), "firebase-user-1", "  Application form  ", now);

        Assert.Equal("firebase-user-1", document.OwnerFirebaseUid);
        Assert.Equal("Application form", document.Title);
        Assert.Equal(DocumentStatus.Draft, document.Status);
        Assert.Equal(now, document.CreatedAt);
        Assert.Equal(now, document.UpdatedAt);
    }

    [Fact]
    public void AddPage_RejectsConfiguredLimit()
    {
        var now = DateTimeOffset.Parse("2026-09-02T00:00:00Z");
        var document = Document.Create(Guid.NewGuid(), "firebase-user-1", "Form", now);
        document.AddPage(Guid.NewGuid(), 1, now.AddSeconds(1));

        var error = Assert.Throws<InvalidOperationException>(() =>
            document.AddPage(Guid.NewGuid(), 1, now.AddSeconds(2)));

        Assert.Equal("Document page limit of 1 reached.", error.Message);
        Assert.Single(document.Pages);
    }

    [Fact]
    public void AddPage_IncrementsMembershipRevisions()
    {
        var document = CreateDocument();

        document.AddPage(Guid.NewGuid(), 10, Now);
        document.AddPage(Guid.NewGuid(), 10, Now.AddMinutes(1));

        Assert.Equal(2, document.Revision);
        Assert.Equal(2, document.PageOrderRevision);
        Assert.Equal(Now.AddMinutes(1), document.UpdatedAt);
    }

    [Fact]
    public void AppendImportedPages_AssignsContiguousPositionsAndReusesSourceTuples()
    {
        var document = CreateDocument();
        var uploadId = Guid.NewGuid();

        var appended = document.AppendImportedPages(uploadId, [1, 2, 3], 10, Now);
        var retried = document.AppendImportedPages(uploadId, [1, 2, 3], 10, Now.AddMinutes(1));

        Assert.Equal([1, 2, 3], appended.Select(page => page.Position));
        Assert.Equal(appended.Select(page => page.Id), retried.Select(page => page.Id));
        Assert.Equal([1, 2, 3], document.ActivePages.Select(page => page.Position));
        Assert.Equal(1, document.Revision);
        Assert.Equal(1, document.PageOrderRevision);
    }

    [Fact]
    public void AppendImportedPages_RejectsPageLimitBeforeChangingMembership()
    {
        var document = CreateDocument();

        var error = Assert.Throws<InvalidOperationException>(() =>
            document.AppendImportedPages(Guid.NewGuid(), [1, 2], 1, Now));

        Assert.Equal("Document page limit of 1 reached.", error.Message);
        Assert.Empty(document.ActivePages);
        Assert.Equal(0, document.Revision);
        Assert.Equal(0, document.PageOrderRevision);
    }

    [Fact]
    public void ReorderPages_UsesExpectedRevisionAndPersistsContiguousPositions()
    {
        var document = CreateDocumentWithThreePages();
        var ids = document.Pages.OrderBy(x => x.Position).Select(x => x.Id).ToArray();

        document.ReorderPages([ids[2], ids[0], ids[1]], document.PageOrderRevision, Now);

        Assert.Equal([ids[2], ids[0], ids[1]], document.ActivePages.Select(x => x.Id));
        Assert.Equal([1, 2, 3], document.ActivePages.Select(x => x.Position));
        Assert.Equal(4, document.PageOrderRevision);
        Assert.Equal(4, document.Revision);
    }

    [Fact]
    public void ReorderPages_RejectsStaleAndInvalidOrdersWithoutMutation()
    {
        var document = CreateDocumentWithThreePages();
        var ids = document.ActivePages.Select(page => page.Id).ToArray();
        var initialRevision = document.PageOrderRevision;

        document.ReorderPages(ids, initialRevision, Now);

        var stale = Assert.Throws<InvalidOperationException>(() =>
            document.ReorderPages([ids[2], ids[1], ids[0]], initialRevision, Now));
        var duplicate = Assert.Throws<ArgumentException>(() =>
            document.ReorderPages([ids[0], ids[0], ids[1]], document.PageOrderRevision, Now));
        var incomplete = Assert.Throws<ArgumentException>(() =>
            document.ReorderPages([ids[0], ids[1]], document.PageOrderRevision, Now));
        var foreign = Assert.Throws<ArgumentException>(() =>
            document.ReorderPages([ids[0], ids[1], Guid.NewGuid()], document.PageOrderRevision, Now));

        Assert.Equal("Page order revision is stale.", stale.Message);
        Assert.Equal([ids[0], ids[1], ids[2]], document.ActivePages.Select(page => page.Id));
        Assert.Equal(4, document.PageOrderRevision);
        Assert.Equal(4, document.Revision);
        Assert.NotNull(duplicate);
        Assert.NotNull(incomplete);
        Assert.NotNull(foreign);
    }

    [Fact]
    public void RemovePage_SoftRemovesAndCompactsActivePositions()
    {
        var document = CreateDocumentWithThreePages();
        var pages = document.ActivePages.ToArray();

        document.RemovePage(pages[1].Id, "firebase-user-1", Now);

        Assert.Equal([pages[0].Id, pages[2].Id], document.ActivePages.Select(page => page.Id));
        Assert.Equal([1, 2], document.ActivePages.Select(page => page.Position));
        Assert.Equal("firebase-user-1", pages[1].RemovedByFirebaseUid);
        Assert.Equal(Now, pages[1].RemovedAt);
        Assert.Equal(4, document.Revision);
        Assert.Equal(4, document.PageOrderRevision);
    }

    [Fact]
    public void ReorderPages_RejectsRemovedPageWithoutMutation()
    {
        var document = CreateDocumentWithThreePages();
        var pages = document.ActivePages.ToArray();
        document.RemovePage(pages[1].Id, "firebase-user-1", Now);
        var activePageIds = document.ActivePages.Select(page => page.Id).ToArray();

        var error = Assert.Throws<ArgumentException>(() =>
            document.ReorderPages([pages[0].Id, pages[1].Id, pages[2].Id], document.PageOrderRevision, Now));

        Assert.NotNull(error);
        Assert.Equal(activePageIds, document.ActivePages.Select(page => page.Id));
        Assert.Equal(4, document.Revision);
        Assert.Equal(4, document.PageOrderRevision);
    }

    [Fact]
    public void MarkContentChanged_IncrementsOnlyDocumentRevision()
    {
        var document = CreateDocumentWithThreePages();

        document.MarkContentChanged(Now);

        Assert.Equal(4, document.Revision);
        Assert.Equal(3, document.PageOrderRevision);
        Assert.Equal(Now, document.UpdatedAt);
    }

    private static Document CreateDocument() =>
        Document.Create(Guid.NewGuid(), "firebase-user-1", "Form", Now);

    private static Document CreateDocumentWithThreePages()
    {
        var document = CreateDocument();
        document.AddPage(Guid.NewGuid(), 10, Now);
        document.AddPage(Guid.NewGuid(), 10, Now);
        document.AddPage(Guid.NewGuid(), 10, Now);
        return document;
    }
}
