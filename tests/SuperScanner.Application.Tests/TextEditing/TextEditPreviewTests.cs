using SuperScanner.Application.Abstractions;
using SuperScanner.Application.TextEditing;
using SuperScanner.Domain.Ocr;
using SuperScanner.Domain.TextEditing;
using SuperScanner.Infrastructure.TextEditing;
using TextAlignment = SuperScanner.Domain.TextEditing.TextAlignment;

namespace SuperScanner.Application.Tests.TextEditing;

public sealed class TextEditPreviewTests
{
    [Fact]
    public async Task Add_preview_works_without_an_ocr_result_or_selected_word()
    {
        var source = Selection() with { OcrResultId = Guid.Empty, Elements = [] };
        var renderer = new PreviewRenderer();
        var service = new TextEditPreview(new SelectionRepository(source),
            new ReadOnlyStore([1, 2, 3]), renderer, Limits());
        var result = await service.RenderAsync(Request() with {
            OcrResultId = Guid.Empty, WordIds = [], ReplacementText = "New label"
        }, default);
        Assert.Null(result.FailureCode);
        Assert.Empty(renderer.LastRequest!.SelectedPolygons);
    }

    private readonly Guid documentId = Guid.NewGuid();
    private readonly Guid pageId = Guid.NewGuid();
    private readonly Guid ocrId = Guid.NewGuid();
    private readonly Guid wordId = Guid.NewGuid();

    [Fact]
    public async Task Preview_returns_rendered_bytes_without_writing_an_object()
    {
        var store = new ReadOnlyStore([1, 2, 3]);
        var service = new TextEditPreview(new SelectionRepository(Selection()), store,
            new PreviewRenderer(), Limits());

        var result = await service.RenderAsync(Request(), default);

        Assert.Equal(new byte[] { 8, 9 }, result.Output);
        Assert.Equal(1, store.ReadCount);
    }

    [Fact]
    public async Task Stale_revision_is_rejected_before_reading_the_image()
    {
        var store = new ReadOnlyStore([1, 2, 3]);
        var service = new TextEditPreview(new SelectionRepository(Selection()), store,
            new PreviewRenderer(), Limits());

        await Assert.ThrowsAsync<StaleTextSelectionException>(() => service.RenderAsync(
            Request() with { ExpectedRevisionId = Guid.NewGuid() }, default));

        Assert.Equal(0, store.ReadCount);
    }

    [Fact]
    public async Task Unowned_page_does_not_reveal_a_preview_or_read_the_image()
    {
        var store = new ReadOnlyStore([1, 2, 3]);
        var service = new TextEditPreview(new SelectionRepository(Selection()), store,
            new PreviewRenderer(), Limits());

        await Assert.ThrowsAsync<TextSelectionNotFoundException>(() => service.RenderAsync(
            Request() with { OwnerUid = "someone-else" }, default));

        Assert.Equal(0, store.ReadCount);
    }

    private CreateTextEditRequest Request() => new("owner", documentId, pageId, ocrId,
        null, [wordId], "Tan BB", new NormalizedBox(.1, .2, .3, .1),
        new TextEditStyle("noto-sans", "archive-main-regular", .04, 400,
            "#000000", 0, .25, 0, TextAlignment.Left), "preview");

    private OwnedTextSelection Selection()
    {
        var word = OcrElement.Create(wordId, ocrId, Guid.NewGuid(), OcrElementKind.Word,
            "Name", .9, OcrTextType.Printed, 0,
            [new(.1, .2), new(.4, .2), new(.4, .3), new(.1, .3)]);
        return new OwnedTextSelection(null, "source.png", ocrId,
            OcrResultState.Ready, "source.png", [word]);
    }

    private static TextEditLimits Limits() => new(true, 20, 100, .5, 2);

    private sealed class SelectionRepository(OwnedTextSelection selection) : ITextSelectionRepository
    {
        public Task<OwnedTextSelection?> FindOwnedAsync(string ownerUid, Guid documentId,
            Guid pageId, Guid ocrResultId, CancellationToken ct) =>
            Task.FromResult<OwnedTextSelection?>(ownerUid == "owner" ? selection : null);
    }

    private sealed class ReadOnlyStore(byte[] bytes) : IObjectStore
    {
        public int ReadCount { get; private set; }
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
        {
            ReadCount++;
            return Task.FromResult<Stream>(new MemoryStream(bytes));
        }
        public Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<StoredObjectInfo?> HeadAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task PromoteAsync(string from, string to, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task<ObjectCreationResult> WriteIfAbsentAsync(string key, string type, Stream content, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class PreviewRenderer : ITextEditRenderer
    {
        public TextEditRenderRequest? LastRequest { get; private set; }
        public Task<TextEditRenderResult> RenderAsync(TextEditRenderRequest request,
            CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new TextEditRenderResult([8, 9], "hash", null, null));
        }
    }
}
