using ArksScanner.Domain.Documents;

namespace ArksScanner.Domain.Tests.Documents;

public sealed class ShadowFilterTests
{
    [Theory]
    [InlineData("Magic")]
    [InlineData("RemoveShadows")]
    [InlineData("CleanDocument")]
    [InlineData("CleanDocumentGentle")]
    [InlineData("CleanDocumentStrong")]
    [InlineData("ContentClean")]
    public void Shadow_filter_can_be_selected_without_changing_preserved_source(string filter)
    {
        var now = DateTimeOffset.UtcNow;
        var document = Document.Create(Guid.NewGuid(), "owner", "Scan", now);
        var page = document.AddPage(Guid.NewGuid(), 10, now);
        page.SetPreview("source.jpg", "thumbnail.jpg");
        page.EnableOptionalCrop();

        page.SetFilter(filter);

        Assert.Equal(filter, page.Filter);
        Assert.Equal("Original", page.AppliedFilter);
        Assert.Equal("source.jpg", page.CropSourceObjectKey);
        Assert.Equal("source.jpg", page.GetExportObjectKey());
        Assert.Throws<ArgumentException>(() => page.SetFilter("UnknownFilter"));
    }
}
