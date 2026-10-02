using ArksScanner.Domain.Documents;

namespace ArksScanner.Domain.Tests.Documents;

public sealed class PageMarkTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 0, 0, 0, TimeSpan.Zero);
    private static readonly SignatureBox Box = new(.2, .3, .05, .05);

    [Theory]
    [InlineData("red", .08)]
    [InlineData("#12AB", .08)]
    [InlineData("#000000\n", .08)]
    [InlineData("#000000", double.NaN)]
    [InlineData("#000000", .01)]
    [InlineData("#000000", .21)]
    public void Style_rejects_invalid_color_or_stroke(string color, double stroke) =>
        Assert.ThrowsAny<ArgumentException>(() => new PageMarkStyle(color, stroke));

    [Fact]
    public void Create_and_update_preserve_validated_style_and_revision()
    {
        var mark = PageMark.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            PageMarkKind.Check, Box, new PageMarkStyle("#12abef", .08), Now);
        Assert.Equal("#12ABEF", mark.Style.Color);
        Assert.Equal(0, mark.Revision);

        mark.Update(PageMarkKind.Cross, new SignatureBox(.4, .5, .08, .08),
            new PageMarkStyle("#FF0000", .2), 0, Now.AddMinutes(1));
        Assert.Equal(PageMarkKind.Cross, mark.Kind);
        Assert.Equal(1, mark.Revision);
        Assert.Throws<InvalidOperationException>(() => mark.Update(PageMarkKind.Check, Box,
            new PageMarkStyle("#000000", .08), 0, Now.AddMinutes(2)));
    }

    [Fact]
    public void Create_rejects_unknown_kind_and_missing_identifiers()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PageMark.Create(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), (PageMarkKind)123, Box, new PageMarkStyle("#000000", .08), Now));
        Assert.Throws<ArgumentException>(() => PageMark.Create(Guid.Empty, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), PageMarkKind.Check, Box, new PageMarkStyle("#000000", .08), Now));
    }

    [Fact]
    public void Delete_soft_deletes_and_rejects_stale_revision()
    {
        var mark = PageMark.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            PageMarkKind.Check, Box, new PageMarkStyle("#000000", .08), Now);
        mark.Delete(0, Now.AddMinutes(1));
        Assert.NotNull(mark.DeletedAt);
        Assert.Equal(1, mark.Revision);
        Assert.Throws<InvalidOperationException>(() => mark.Delete(0, Now.AddMinutes(2)));
    }

    [Fact]
    public void Create_request_matching_uses_original_payload_after_update_and_delete()
    {
        var originalStyle = new PageMarkStyle("#000000", .08);
        var mark = PageMark.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            PageMarkKind.Check, Box, originalStyle, Now);
        mark.Update(PageMarkKind.Cross, new SignatureBox(.4, .5, .08, .08),
            new PageMarkStyle("#FF0000", .2), 0, Now.AddMinutes(1));
        mark.Delete(1, Now.AddMinutes(2));

        Assert.True(mark.MatchesOriginalCreate(PageMarkKind.Check, Box, originalStyle));
        Assert.False(mark.MatchesOriginalCreate(PageMarkKind.Cross, mark.Box, mark.Style));
    }
}
