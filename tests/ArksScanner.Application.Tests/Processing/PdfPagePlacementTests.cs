using ArksScanner.Infrastructure.Processing;

namespace ArksScanner.Application.Tests.Processing;

public sealed class PdfPagePlacementTests
{
    [Theory]
    [InlineData(1000, 1500, 595.28, 841.89)]
    [InlineData(1500, 1000, 841.89, 595.28)]
    public void A4_FitsWithoutStretchingAndReservesMargins(int width, int height,
        double pageWidth, double pageHeight)
    {
        var p = PdfPagePlacement.Create(width, height, "A4");
        Assert.InRange(p.PageWidth, pageWidth - .1, pageWidth + .1);
        Assert.InRange(p.PageHeight, pageHeight - .1, pageHeight + .1);
        Assert.True(p.ContentX >= 14.17 && p.ContentY >= 14.17);
        Assert.True(p.ContentX + p.ContentWidth <= p.PageWidth - 14.17);
        Assert.True(p.ContentY + p.ContentHeight <= p.PageHeight - 14.17);
        Assert.InRange(p.ContentWidth / p.ContentHeight,
            (double)width / height - .0001, (double)width / height + .0001);
    }

    [Fact]
    public void Original_UsesExistingPageAndFullBleed()
    {
        var p = PdfPagePlacement.Create(96, 48, "Original");
        Assert.Equal((72d, 36d, 0d, 0d, 72d, 36d),
            (p.PageWidth, p.PageHeight, p.ContentX, p.ContentY, p.ContentWidth, p.ContentHeight));
    }
}
