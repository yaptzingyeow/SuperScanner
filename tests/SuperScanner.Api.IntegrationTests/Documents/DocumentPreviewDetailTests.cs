using System.Text.Json;
using SuperScanner.Api.Endpoints;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.TextEditing;

namespace SuperScanner.Api.IntegrationTests.Documents;

public sealed class DocumentPreviewDetailTests
{
    [Fact]
    public void Detail_uses_active_revision_as_preview_token()
    {
        var now = new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);
        var document = Document.Create(Guid.NewGuid(), "owner", "Form", now);
        var page = document.AddPage(Guid.NewGuid(), 10, now);
        page.MarkImportReady("source.jpg", "image/jpeg");
        page.SetPreview("original.jpg", "thumbnail.jpg");
        page.MarkReady();
        var revision = PageRevision.CreateBase(Guid.NewGuid(), page.Id,
            "page-revisions/edited.jpg", new string('a', 64), now);
        page.ActivateRevision(revision);

        var detail = JsonSerializer.SerializeToElement(
            DocumentPreviewEndpoints.CreateDetail(document, [], null));
        var ready = detail.GetProperty("pages")[0];
        Assert.Equal(revision.Id.ToString("N"), ready.GetProperty("previewRevision").GetString());
    }
}
