using ArksScanner.Domain.Documents;
using ArksScanner.Domain.TextEditing;

namespace ArksScanner.Domain.Tests.Documents;

public sealed class PageRepairOperationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Only_unused_preview_can_expire()
    {
        var unused = PageRepairOperation.Create(Guid.NewGuid(), Guid.NewGuid(), null,
            "previews/source.jpg", "[[0.01,0.2,0.04,0.25]]", Now);
        unused.CompletePreview("repairs/unused.png");
        unused.Expire();
        Assert.Equal("Expired", unused.State);

        var applied = PageRepairOperation.Create(Guid.NewGuid(), Guid.NewGuid(), null,
            "previews/source.jpg", "[[0.01,0.2,0.04,0.25]]", Now);
        applied.CompletePreview("repairs/applied.png");
        applied.Apply(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => applied.Expire());
    }

    [Fact]
    public void Suggestions_are_not_an_applied_page_change()
    {
        var operation = PageRepairOperation.CreateSuggestion(Guid.NewGuid(), Guid.NewGuid(),
            null, "previews/source.jpg", Now);
        operation.CompleteSuggestions("[[0.01,0.2,0.04,0.25]]");

        Assert.Equal("Ready", operation.State);
        Assert.Equal("Suggest", operation.Kind);
        Assert.Null(operation.PreviewObjectKey);
        Assert.Null(operation.AppliedRevisionId);
        Assert.Throws<InvalidOperationException>(() => operation.Apply(Guid.NewGuid()));
    }

    [Fact]
    public void Repair_requires_a_preview_before_application()
    {
        var operation = PageRepairOperation.Create(Guid.NewGuid(), Guid.NewGuid(), null,
            "previews/source.jpg", "[[0.01,0.2,0.04,0.25]]", Now);
        Assert.Throws<InvalidOperationException>(() => operation.Apply(Guid.NewGuid()));

        operation.CompletePreview("repairs/preview.png");
        var revisionId = Guid.NewGuid();
        operation.Apply(revisionId);

        Assert.Equal("Applied", operation.State);
        Assert.Equal(revisionId, operation.AppliedRevisionId);
        Assert.Throws<InvalidOperationException>(() => operation.Apply(Guid.NewGuid()));
    }

    [Fact]
    public void Repair_revision_is_a_png_child_of_the_original()
    {
        var parent = Guid.NewGuid();
        var revision = PageRevision.CreateRepair(Guid.NewGuid(), Guid.NewGuid(), parent,
            "repairs/preview.png", new string('a', 64), Now);
        Assert.Equal(parent, revision.ParentRevisionId);
        Assert.Equal("image/png", revision.MediaType);
        Assert.Null(revision.ProducingTextEditId);
    }
}
