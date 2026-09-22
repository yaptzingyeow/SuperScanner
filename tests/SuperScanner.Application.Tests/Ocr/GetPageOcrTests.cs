using SuperScanner.Application.Abstractions;
using SuperScanner.Application.Ocr;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Ocr;

namespace SuperScanner.Application.Tests.Ocr;

public sealed class GetPageOcrTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 1, 0, 0, TimeSpan.Zero);
    private readonly Guid documentId = Guid.NewGuid();
    private readonly Guid pageId = Guid.NewGuid();

    [Fact]
    public async Task ReadyPageWithoutCurrentResult_ReturnsNotRequested()
    {
        var repository = ReadyRepository("previews/current.jpg");
        var query = new GetPageOcr(repository);

        var dto = await query.HandleAsync("owner", documentId, pageId, default);

        Assert.Equal("NotRequested", dto.State);
        Assert.Null(dto.ResultId);
        Assert.Empty(dto.Elements);
    }

    [Fact]
    public async Task StaleResult_IsNotReturnedForNewPreview()
    {
        var repository = ReadyRepository("previews/current.jpg");
        repository.Results.Add(PageOcrResult.Queue(Guid.NewGuid(), pageId, "previews/old.jpg",
            OcrSourceFingerprint.Create("previews/old.jpg"), "en", Now));
        var query = new GetPageOcr(repository);

        var dto = await query.HandleAsync("owner", documentId, pageId, default);

        Assert.Equal("NotRequested", dto.State);
    }

    [Fact]
    public async Task MissingOrUnownedPage_IsHiddenAsNotFound()
    {
        var query = new GetPageOcr(new MemoryOcrRepository(null));

        await Assert.ThrowsAsync<OcrResourceNotFoundException>(() =>
            query.HandleAsync("owner", documentId, pageId, default));
    }

    [Fact]
    public async Task ReadyResult_MapsNestedOrderedHierarchy()
    {
        var repository = ReadyRepository("previews/current.jpg");
        var result = PageOcrResult.Queue(Guid.NewGuid(), pageId, repository.Source!.SourceObjectKey!,
            OcrSourceFingerprint.Create(repository.Source.SourceObjectKey!), "en", Now);
        var block = Element(result.Id, null, OcrElementKind.Block, "Name", 0);
        var laterLine = Element(result.Id, block.Id, OcrElementKind.Line, "Later", 1);
        var firstLine = Element(result.Id, block.Id, OcrElementKind.Line, "Name", 0);
        var word = Element(result.Id, firstLine.Id, OcrElementKind.Word, "Name", 0);
        result.BeginAttempt(1, Now.AddSeconds(1));
        result.Complete("Fake", "v1", "Name Later", [block, laterLine, firstLine, word], Now.AddSeconds(2));
        repository.Results.Add(result);

        var dto = await new GetPageOcr(repository)
            .HandleAsync("owner", documentId, pageId, default);

        Assert.Equal("Name Later", dto.FullText);
        var root = Assert.Single(dto.Elements);
        Assert.Equal("Block", root.Kind);
        Assert.Equal(["Name", "Later"], root.Children.Select(child => child.Text));
        Assert.Equal("Name", Assert.Single(root.Children[0].Children).Text);
    }

    [Fact]
    public async Task PendingResult_DoesNotExposeTextOrElements()
    {
        var repository = ReadyRepository("previews/current.jpg");
        repository.Results.Add(PageOcrResult.Queue(Guid.NewGuid(), pageId,
            repository.Source!.SourceObjectKey!,
            OcrSourceFingerprint.Create(repository.Source.SourceObjectKey!), "en", Now));

        var dto = await new GetPageOcr(repository)
            .HandleAsync("owner", documentId, pageId, default);

        Assert.Equal("Queued", dto.State);
        Assert.Null(dto.FullText);
        Assert.Empty(dto.Elements);
    }

    private MemoryOcrRepository ReadyRepository(string key) => new(new OcrPageSource(
        pageId, PageState.Ready, key, "image/jpeg"));

    private static OcrElement Element(
        Guid resultId, Guid? parentId, OcrElementKind kind, string text, int order) =>
        OcrElement.Create(Guid.NewGuid(), resultId, parentId, kind, text, .9,
            OcrTextType.Printed, order,
            [new(.1, .1), new(.9, .1), new(.9, .2), new(.1, .2)]);
}
