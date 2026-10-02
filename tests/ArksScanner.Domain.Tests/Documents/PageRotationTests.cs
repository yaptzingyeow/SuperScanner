using ArksScanner.Domain.Documents;

namespace ArksScanner.Domain.Tests.Documents;

public sealed class PageRotationTests
{
    [Fact]
    public void Rotation_accepts_quarter_turns_and_rejects_others()
    {
        var now = DateTimeOffset.UtcNow;
        var document = Document.Create(Guid.NewGuid(), "owner", "Scan", now);
        var page = document.AddPage(Guid.NewGuid(), 50, now);
        Assert.Equal(0, page.Rotation);

        page.SetRotation(90);
        Assert.Equal(90, page.Rotation);
        Assert.Throws<ArgumentException>(() => page.SetRotation(45));
        Assert.Throws<ArgumentException>(() => page.SetRotation(360));
        Assert.Equal(90, page.Rotation);
    }
}
