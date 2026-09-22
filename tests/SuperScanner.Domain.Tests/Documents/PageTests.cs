using SuperScanner.Domain.Documents;

namespace SuperScanner.Domain.Tests.Documents;

public sealed class PageTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BeginCrop_RemovesReadyPageFromExportEligibility(bool detect)
    {
        var now = DateTimeOffset.UtcNow;
        var document = Document.Create(Guid.NewGuid(), "owner", "Scan", now);
        var page = document.AddPage(Guid.NewGuid(), 50, now);
        page.SetPreview("preview", "thumbnail");
        page.InitializeCrop();
        page.MarkReady();

        page.BeginCrop(detect, null);

        Assert.Equal(PageState.Processing, page.State);
        Assert.Throws<InvalidOperationException>(() => page.GetExportObjectKey());
    }

    [Fact]
    public void AcceptOriginalRejectsReplacingExistingAsset()
    {
        var document = Document.Create(
            Guid.NewGuid(),
            "firebase-user-1",
            "Form",
            DateTimeOffset.Parse("2026-09-02T00:00:00Z"));
        var page = document.AddPage(Guid.NewGuid(), 1, DateTimeOffset.Parse("2026-09-02T00:01:00Z"));

        page.AcceptOriginal("originals/document/page/first-hash");

        var error = Assert.Throws<InvalidOperationException>(() =>
            page.AcceptOriginal("originals/document/page/replacement-hash"));
        Assert.Equal("Original asset is immutable.", error.Message);
        Assert.Equal("originals/document/page/first-hash", page.OriginalObjectKey);
    }

    [Fact]
    public void Lifecycle_TracksImportAndExportReadiness()
    {
        var now = DateTimeOffset.Parse("2026-09-14T00:00:00Z");
        var document = Document.Create(Guid.NewGuid(), "firebase-user-1", "Form", now);
        var page = document.AppendImportedPages(Guid.NewGuid(), [1], 10, now).Single();

        page.MarkImportReady("page-sources/document/page/source.png", "image/png");
        page.MarkProcessing();
        page.SetPreview("previews/document/page/revision-1.png", "thumbnails/document/page/revision-1.png");
        page.MarkReady();

        Assert.Equal(PageState.Ready, page.State);
        Assert.Equal("image/png", page.OriginalMediaType);
        Assert.Equal("previews/document/page/revision-1.png", page.GetExportObjectKey());
    }

    [Fact]
    public void SoftRemove_RecordsRemovalMetadata()
    {
        var now = DateTimeOffset.Parse("2026-09-14T00:00:00Z");
        var document = Document.Create(Guid.NewGuid(), "firebase-user-1", "Form", now);
        var page = document.AppendImportedPages(Guid.NewGuid(), [1], 10, now).Single();

        page.MarkFailed("render_failed");
        page.SoftRemove("firebase-user-1", now.AddMinutes(1));

        Assert.Equal(PageState.Failed, page.State);
        Assert.Equal("render_failed", page.FailureCode);
        Assert.Equal("firebase-user-1", page.RemovedByFirebaseUid);
        Assert.Equal(now.AddMinutes(1), page.RemovedAt);
    }
}
