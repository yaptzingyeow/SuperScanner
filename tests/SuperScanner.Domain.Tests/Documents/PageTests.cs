using SuperScanner.Domain.Documents;

namespace SuperScanner.Domain.Tests.Documents;

public sealed class PageTests
{
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
}
