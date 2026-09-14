using System.Text.Json;
using SuperScanner.Domain.Documents;

namespace SuperScanner.Domain.Tests.Documents;

public sealed class DocumentExportTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-14T00:00:00Z");

    [Fact]
    public void Create_SnapshotsOnlyReadyPagesInVisibleOrder()
    {
        var document = Document.Create(Guid.NewGuid(), "firebase-user-1", "Form", Now);
        var pages = document.AppendImportedPages(Guid.NewGuid(), [1, 2, 3], 10, Now).ToArray();
        PrepareReadyPage(pages[0], "previews/first.png");
        PrepareReadyPage(pages[2], "previews/third.png");
        document.ReorderPages([pages[2].Id, pages[1].Id, pages[0].Id], document.PageOrderRevision, Now);

        var export = DocumentExport.Create(Guid.NewGuid(), document, "firebase-user-1", Now, TimeSpan.FromDays(7));

        using var snapshot = JsonDocument.Parse(export.SnapshotJson);
        var entries = snapshot.RootElement.EnumerateArray().ToArray();
        Assert.Equal(DocumentExportState.Queued, export.State);
        Assert.Equal(document.Revision, export.DocumentRevision);
        Assert.Equal(2, export.ReadyPageCount);
        Assert.Equal(1, export.ExcludedPageCount);
        Assert.Equal(Now.AddDays(7), export.ExpiresAt);
        Assert.Equal(pages[2].Id, entries[0].GetProperty("PageId").GetGuid());
        Assert.Equal(1, entries[0].GetProperty("Position").GetInt32());
        Assert.Equal("previews/third.png", entries[0].GetProperty("ProcessedObjectKey").GetString());
        Assert.Equal(pages[0].Id, entries[1].GetProperty("PageId").GetGuid());
        Assert.Equal("previews/first.png", entries[1].GetProperty("ProcessedObjectKey").GetString());
    }

    [Fact]
    public void Create_RejectsDocumentWithoutReadyPages()
    {
        var document = Document.Create(Guid.NewGuid(), "firebase-user-1", "Form", Now);
        document.AppendImportedPages(Guid.NewGuid(), [1], 10, Now);

        var error = Assert.Throws<InvalidOperationException>(() =>
            DocumentExport.Create(Guid.NewGuid(), document, "firebase-user-1", Now, TimeSpan.FromDays(7)));

        Assert.Equal("Document has no ready pages to export.", error.Message);
    }

    [Fact]
    public void Transitions_MoveExportFromQueuedToReady()
    {
        var document = Document.Create(Guid.NewGuid(), "firebase-user-1", "Form", Now);
        var page = document.AppendImportedPages(Guid.NewGuid(), [1], 10, Now).Single();
        PrepareReadyPage(page, "previews/first.png");
        var export = DocumentExport.Create(Guid.NewGuid(), document, "firebase-user-1", Now, TimeSpan.FromDays(7));

        export.Start(Now.AddMinutes(1));
        export.Complete("exports/document/export/document.pdf", Now.AddMinutes(2));

        Assert.Equal(DocumentExportState.Ready, export.State);
        Assert.Equal("exports/document/export/document.pdf", export.OutputObjectKey);
        Assert.Equal(Now.AddMinutes(2), export.CompletedAt);
    }

    private static void PrepareReadyPage(Page page, string previewObjectKey)
    {
        page.MarkImportReady("page-sources/document/page/source.png", "image/png");
        page.MarkProcessing();
        page.SetPreview(previewObjectKey, "thumbnails/document/page/revision-1.png");
        page.MarkReady();
    }
}
