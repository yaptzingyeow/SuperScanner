using SuperScanner.Domain.Documents;
using SuperScanner.Domain.TextEditing;

namespace SuperScanner.Domain.Tests.TextEditing;

public sealed class PageRevisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Activating_a_derived_revision_changes_only_the_processed_asset()
    {
        var page = ReadyPage("previews/p1.jpg");
        var baseRevision = PageRevision.CreateBase(
            Guid.NewGuid(), page.Id, "previews/p1.jpg", "sha-base", Now);
        var derived = PageRevision.CreateDerived(
            Guid.NewGuid(), page.Id, baseRevision.Id, Guid.NewGuid(),
            "page-revisions/p1/r2.jpg", "sha-derived", Now.AddSeconds(1));

        page.ActivateRevision(derived);

        Assert.Equal(derived.Id, page.ActiveRevisionId);
        Assert.Equal("page-revisions/p1/r2.jpg", page.GetProcessedObjectKey());
        Assert.Equal("previews/p1.jpg", page.PreviewObjectKey);
    }

    [Fact]
    public void Activating_a_revision_from_another_page_is_rejected()
    {
        var page = ReadyPage("previews/p1.jpg");
        var otherPage = ReadyPage("previews/p2.jpg");
        var revision = PageRevision.CreateBase(
            Guid.NewGuid(), otherPage.Id, "previews/p2.jpg", "sha-other", Now);

        var error = Assert.Throws<ArgumentException>(() => page.ActivateRevision(revision));

        Assert.Equal("revision", error.ParamName);
        Assert.Null(page.ActiveRevisionId);
    }

    [Fact]
    public void Ready_page_without_a_revision_resolves_its_legacy_preview()
    {
        var page = ReadyPage("previews/legacy.jpg");

        Assert.Null(page.ActiveRevisionId);
        Assert.Equal("previews/legacy.jpg", page.GetProcessedObjectKey());
        Assert.Equal("previews/legacy.jpg", page.GetExportObjectKey());
    }

    [Fact]
    public void Revision_properties_have_no_public_setters()
    {
        var mutableProperties = typeof(PageRevision).GetProperties()
            .Where(property => property.SetMethod?.IsPublic == true)
            .Select(property => property.Name)
            .ToArray();

        Assert.Empty(mutableProperties);
    }

    [Fact]
    public void Derived_revision_requires_a_parent_and_producing_edit()
    {
        Assert.Throws<ArgumentException>(() => PageRevision.CreateDerived(
            Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Guid.NewGuid(),
            "page-revisions/p1/r2.jpg", "sha-derived", Now));
        Assert.Throws<ArgumentException>(() => PageRevision.CreateDerived(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty,
            "page-revisions/p1/r2.jpg", "sha-derived", Now));
    }

    private static Page ReadyPage(string previewKey)
    {
        var document = Document.Create(Guid.NewGuid(), "owner", "Document", Now);
        var page = document.AddPage(Guid.NewGuid(), 10, Now);
        page.MarkImportReady("page-sources/source.jpg", "image/jpeg");
        page.SetPreview(previewKey, "thumbnails/p1.jpg");
        page.MarkReady();
        return page;
    }
}
