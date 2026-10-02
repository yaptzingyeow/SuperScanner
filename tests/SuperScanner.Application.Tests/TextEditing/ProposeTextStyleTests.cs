using SuperScanner.Application.TextEditing;
using SuperScanner.Domain.Ocr;

namespace SuperScanner.Application.Tests.TextEditing;

public sealed class ProposeTextStyleTests
{
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid PageId = Guid.NewGuid();
    private static readonly Guid ResultId = Guid.NewGuid();
    private const string Owner = "owner";

    [Fact]
    public async Task Handwritten_word_is_rejected_before_image_access()
    {
        var fixture = Fixture(OcrTextType.Handwritten);
        await Assert.ThrowsAsync<UnsupportedTextSelectionException>(() =>
            fixture.Service.HandleAsync(Owner, DocumentId, PageId, ResultId,
                [fixture.Words[0].Id], CancellationToken.None));
        Assert.Equal(0, fixture.Estimator.CallCount);
    }

    [Fact]
    public async Task Selection_is_sorted_by_server_reading_order()
    {
        var fixture = Fixture(OcrTextType.Printed);
        var result = await fixture.Service.HandleAsync(Owner, DocumentId, PageId, ResultId,
            [fixture.Words[1].Id, fixture.Words[0].Id], CancellationToken.None);
        Assert.Equal("Yap Tzing", result.OriginalText);
        Assert.Equal([fixture.Words[0].Id, fixture.Words[1].Id], result.WordIds);
        Assert.Equal(1, fixture.Estimator.CallCount);
    }

    [Fact]
    public async Task Duplicate_or_noncontiguous_words_are_rejected()
    {
        var fixture = Fixture(OcrTextType.Printed);
        await Assert.ThrowsAsync<InvalidTextSelectionException>(() =>
            fixture.Service.HandleAsync(Owner, DocumentId, PageId, ResultId,
                [fixture.Words[0].Id, fixture.Words[0].Id], CancellationToken.None));
        await Assert.ThrowsAsync<InvalidTextSelectionException>(() =>
            fixture.Service.HandleAsync(Owner, DocumentId, PageId, ResultId,
                [fixture.Words[0].Id, fixture.Words[2].Id], CancellationToken.None));
        Assert.Equal(0, fixture.Estimator.CallCount);
    }

    [Fact]
    public async Task Missing_owned_page_is_not_exposed()
    {
        var fixture = Fixture(OcrTextType.Printed);
        fixture.Repository.Selection = null;
        await Assert.ThrowsAsync<TextSelectionNotFoundException>(() =>
            fixture.Service.HandleAsync(Owner, DocumentId, PageId, ResultId,
                [fixture.Words[0].Id], CancellationToken.None));
        Assert.Equal(0, fixture.Estimator.CallCount);
    }

    [Fact]
    public async Task Stale_ocr_source_is_rejected_before_image_access()
    {
        var fixture = Fixture(OcrTextType.Printed);
        fixture.Repository.Selection = fixture.Repository.Selection! with
        {
            OcrSourceObjectKey = "private/older-source.jpg"
        };
        await Assert.ThrowsAsync<StaleTextSelectionException>(() =>
            fixture.Service.HandleAsync(Owner, DocumentId, PageId, ResultId,
                [fixture.Words[0].Id], CancellationToken.None));
        Assert.Equal(0, fixture.Estimator.CallCount);
    }

    [Fact]
    public async Task Selection_across_lines_is_rejected()
    {
        var fixture = Fixture(OcrTextType.Printed);
        var secondLineWord = OcrElement.Create(Guid.NewGuid(), ResultId, Guid.NewGuid(),
            OcrElementKind.Word, "Other", 0.9, OcrTextType.Printed, 0,
            [new(0.1, 0.4), new(0.2, 0.4), new(0.2, 0.44), new(0.1, 0.44)]);
        fixture.Repository.Selection = fixture.Repository.Selection! with
        {
            Elements = [.. fixture.Words, secondLineWord]
        };
        await Assert.ThrowsAsync<InvalidTextSelectionException>(() =>
            fixture.Service.HandleAsync(Owner, DocumentId, PageId, ResultId,
                [fixture.Words[0].Id, secondLineWord.Id], CancellationToken.None));
        Assert.Equal(0, fixture.Estimator.CallCount);
    }

    [Fact]
    public async Task Overlapping_unselected_word_is_rejected()
    {
        var fixture = Fixture(OcrTextType.Printed);
        var overlapping = OcrElement.Create(Guid.NewGuid(), ResultId, Guid.NewGuid(),
            OcrElementKind.Word, "Protected", 0.9, OcrTextType.Printed, 0,
            [new(0.11, 0.21), new(0.17, 0.21), new(0.17, 0.23), new(0.11, 0.23)]);
        fixture.Repository.Selection = fixture.Repository.Selection! with
        {
            Elements = [.. fixture.Words, overlapping]
        };
        await Assert.ThrowsAsync<InvalidTextSelectionException>(() =>
            fixture.Service.HandleAsync(Owner, DocumentId, PageId, ResultId,
                [fixture.Words[0].Id], CancellationToken.None));
        Assert.Equal(0, fixture.Estimator.CallCount);
    }

    [Fact]
    public async Task Neighbouring_words_whose_boxes_touch_the_selection_do_not_block_it()
    {
        var line = Guid.NewGuid();
        OcrElement Word(string text, int order, double x0, double x1) => OcrElement.Create(Guid.NewGuid(), ResultId,
            line, OcrElementKind.Word, text, .95, OcrTextType.Printed, order,
            [new(x0, .2), new(x1, .2), new(x1, .24), new(x0, .24)]);
        // "2A, Jalan, Ampang": boxes overlap by a sliver, and the comma sits inside the end of "Jalan".
        OcrElement[] words = [Word("2A", 0, .10, .16), Word("Jalan", 1, .155, .30), Word(",", 2, .29, .305),
            Word("Ampang", 3, .31, .45)];
        var fixture = Fixture(OcrTextType.Printed);
        fixture.Repository.Selection = fixture.Repository.Selection! with { Elements = words };

        var result = await fixture.Service.HandleAsync(Owner, DocumentId, PageId, ResultId,
            [words[1].Id], CancellationToken.None);

        Assert.Equal("Jalan", result.OriginalText);
    }

    [Fact]
    public async Task A_word_on_another_line_partly_covered_by_the_selection_is_rejected()
    {
        var fixture = Fixture(OcrTextType.Printed);
        // Handwriting above dips into the selected word: a third of it lies inside the selection.
        var handwriting = OcrElement.Create(Guid.NewGuid(), ResultId, Guid.NewGuid(),
            OcrElementKind.Word, "Signed", .9, OcrTextType.Printed, 0,
            [new(.1, .17), new(.18, .17), new(.18, .215), new(.1, .215)]);
        fixture.Repository.Selection = fixture.Repository.Selection! with
        {
            Elements = [.. fixture.Words, handwriting]
        };

        await Assert.ThrowsAsync<InvalidTextSelectionException>(() =>
            fixture.Service.HandleAsync(Owner, DocumentId, PageId, ResultId,
                [fixture.Words[0].Id], CancellationToken.None));
    }

    [Fact]
    public async Task Maximum_word_count_is_enforced()
    {
        var fixture = Fixture(OcrTextType.Printed);
        var service = new ProposeTextStyle(fixture.Repository, fixture.Estimator, 1);
        await Assert.ThrowsAsync<InvalidTextSelectionException>(() =>
            service.HandleAsync(Owner, DocumentId, PageId, ResultId,
                [fixture.Words[0].Id, fixture.Words[1].Id], CancellationToken.None));
        Assert.Equal(0, fixture.Estimator.CallCount);
    }

    [Fact]
    public async Task Degenerate_polygon_is_rejected()
    {
        var fixture = Fixture(OcrTextType.Printed);
        var invalid = OcrElement.Create(Guid.NewGuid(), ResultId, fixture.Words[0].ParentElementId,
            OcrElementKind.Word, "Flat", 0.9, OcrTextType.Printed, 0,
            [new(0.1, 0.2), new(0.2, 0.2), new(0.3, 0.2), new(0.4, 0.2)]);
        fixture.Repository.Selection = fixture.Repository.Selection! with { Elements = [invalid] };
        await Assert.ThrowsAsync<InvalidTextSelectionException>(() =>
            fixture.Service.HandleAsync(Owner, DocumentId, PageId, ResultId,
                [invalid.Id], CancellationToken.None));
        Assert.Equal(0, fixture.Estimator.CallCount);
    }

    private static FixtureData Fixture(OcrTextType type)
    {
        var line = Guid.NewGuid();
        var words = new[] { "Yap", "Tzing", "Yeow" }
            .Select((text, index) => OcrElement.Create(Guid.NewGuid(), ResultId, line,
                OcrElementKind.Word, text, 0.95, type, index,
                [new(0.1 + index * 0.1, 0.2), new(0.18 + index * 0.1, 0.2),
                 new(0.18 + index * 0.1, 0.24), new(0.1 + index * 0.1, 0.24)]))
            .ToArray();
        var repository = new FakeRepository
        {
            Selection = new OwnedTextSelection(null, "private/source.jpg", ResultId,
                OcrResultState.Ready, "private/source.jpg", words)
        };
        var estimator = new FakeEstimator();
        return new FixtureData(new ProposeTextStyle(repository, estimator, 50), repository, estimator, words);
    }

    private sealed record FixtureData(ProposeTextStyle Service, FakeRepository Repository,
        FakeEstimator Estimator, OcrElement[] Words);

    private sealed class FakeRepository : ITextSelectionRepository
    {
        public OwnedTextSelection? Selection { get; set; }
        public Task<OwnedTextSelection?> FindOwnedAsync(string ownerUid, Guid documentId,
            Guid pageId, Guid ocrResultId, CancellationToken ct) => Task.FromResult(Selection);
    }

    private sealed class FakeEstimator : ITextStyleEstimator
    {
        public int CallCount { get; private set; }
        public Task<TextStyleEstimate> EstimateAsync(string sourceObjectKey,
            IReadOnlyList<OcrElement> words, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(new TextStyleEstimate([], 0.5, "#000000", 12, 400, 0, 0, "left"));
        }
    }
}
