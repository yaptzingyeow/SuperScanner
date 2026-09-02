using SuperScanner.Domain.Documents;

namespace SuperScanner.Domain.Tests.Documents;

public sealed class DocumentTests
{
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
}
