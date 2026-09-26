using SuperScanner.Domain.Documents;

namespace SuperScanner.Domain.Tests.Documents;

public sealed class PageSignatureTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static PageSignature Create() => PageSignature.Create(Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), "signatures/test.png", 2, new SignatureBox(.1, .2, .3, .1), Now);

    [Fact]
    public void Box_cannot_bypass_validation_through_public_setters()
    {
        foreach (var property in typeof(SignatureBox).GetProperties())
            Assert.False(property.SetMethod?.IsPublic == true);
    }

    [Theory]
    [InlineData(-.1, .2, .3, .1)]
    [InlineData(.9, .2, .3, .1)]
    [InlineData(.1, .2, 0, .1)]
    [InlineData(.1, .95, .3, .1)]
    public void Create_rejects_out_of_page_box(double x, double y, double width, double height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SignatureBox(x, y, width, height));

    [Fact]
    public void MoveResize_rejects_stale_revision()
    {
        var signature = Create();
        signature.MoveResize(new SignatureBox(.2, .3, .3, .1), 0, Now);
        Assert.Equal(1, signature.Revision);
        Assert.Throws<InvalidOperationException>(() => signature.MoveResize(new SignatureBox(.3, .3, .3, .1), 0, Now));
    }

    [Fact]
    public void Delete_hides_overlay_without_losing_asset_key()
    {
        var signature = Create();
        signature.Delete(0, Now);
        Assert.Equal(Now, signature.DeletedAt);
        Assert.Equal("signatures/test.png", signature.AssetKey);
        Assert.Throws<InvalidOperationException>(() => signature.MoveResize(new SignatureBox(.2, .3, .3, .1), 1, Now));
    }
}
