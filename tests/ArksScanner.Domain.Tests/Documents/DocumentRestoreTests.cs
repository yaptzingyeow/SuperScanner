using ArksScanner.Domain.Documents;

namespace ArksScanner.Domain.Tests.Documents;

public sealed class DocumentRestoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Restore_brings_back_the_document_and_the_pages_deleted_with_it()
    {
        var document = Document.Create(Guid.NewGuid(), "owner", "Contract", Now);
        var kept = document.AddPage(Guid.NewGuid(), 10, Now);
        var removedEarlier = document.AddPage(Guid.NewGuid(), 10, Now);
        document.RemovePage(removedEarlier.Id, "owner", Now.AddMinutes(1));
        document.Remove(Document.UserDeletedReason, Now.AddMinutes(2));

        document.Restore(Now.AddMinutes(3));

        Assert.Null(document.RemovedAt);
        Assert.Null(document.RemovedReason);
        Assert.Equal([kept.Id], document.ActivePages.Select(page => page.Id));
        Assert.Equal(Now.AddMinutes(3), document.UpdatedAt);
    }

    [Fact]
    public void Restore_refuses_documents_removed_by_retention()
    {
        var document = Document.Create(Guid.NewGuid(), "owner", "Old", Now);
        document.Remove("retention", Now);
        Assert.Throws<InvalidOperationException>(() => document.Restore(Now.AddMinutes(1)));
    }

    [Fact]
    public void Restore_of_an_active_document_changes_nothing()
    {
        var document = Document.Create(Guid.NewGuid(), "owner", "Live", Now);
        var revision = document.Revision;
        document.Restore(Now.AddMinutes(1));
        Assert.Equal(revision, document.Revision);
    }
}
