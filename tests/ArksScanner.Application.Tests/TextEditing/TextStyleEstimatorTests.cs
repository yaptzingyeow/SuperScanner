using ImageMagick;
using ImageMagick.Drawing;
using ArksScanner.Application.Abstractions;
using ArksScanner.Domain.Ocr;
using ArksScanner.Infrastructure.TextEditing;

namespace ArksScanner.Application.Tests.TextEditing;

public sealed class TextStyleEstimatorTests
{
    [Fact]
    public async Task Dark_blue_print_on_white_page_produces_bounded_style()
    {
        using var image = new MagickImage(MagickColors.White, 200, 100);
        new Drawables().FillColor(new MagickColor("#18386a"))
            .Rectangle(20, 20, 84, 29).Draw(image);
        var estimator = CreateEstimator(image.ToByteArray(MagickFormat.Png));
        var estimate = await estimator.EstimateAsync("private/source.png", [Word()], CancellationToken.None);

        Assert.StartsWith("#", estimate.ColorHex);
        Assert.InRange(estimate.FontSizePoints, 3, 72);
        Assert.InRange(estimate.BaselineAngleDegrees, -15, 15);
        Assert.NotEmpty(estimate.Candidates);
        Assert.InRange(estimate.Confidence, 0, 1);
    }

    [Fact]
    public async Task Low_contrast_page_returns_uncertain_fallback()
    {
        using var image = new MagickImage(MagickColors.White, 200, 100);
        new Drawables().FillColor(new MagickColor("#f7f7f7"))
            .Rectangle(20, 20, 84, 29).Draw(image);
        var estimator = CreateEstimator(image.ToByteArray(MagickFormat.Png));
        var estimate = await estimator.EstimateAsync("private/source.png", [Word()], CancellationToken.None);

        Assert.True(estimate.Confidence < 0.5);
        Assert.NotEmpty(estimate.Candidates);
    }

    [Fact]
    public async Task Expanded_catalogue_recommends_distinct_selectable_families()
    {
        using var image = new MagickImage(MagickColors.White, 200, 100);
        var estimate = await CreateEstimator(image.ToByteArray(MagickFormat.Png))
            .EstimateAsync("private/source.png", [Word()], CancellationToken.None);

        Assert.True(estimate.Candidates.Count >= 20);
        Assert.Equal(3, estimate.Candidates.Take(3)
            .Select(candidate => candidate.CatalogueId).Distinct().Count());
    }

    private static TextStyleEstimator CreateEstimator(byte[] bytes)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ArksScanner.slnx")))
            directory = directory.Parent;
        var root = directory?.FullName ?? throw new DirectoryNotFoundException();
        var catalogue = BundledFontCatalogue.Load(Path.Combine(root, "assets/fonts/manifest.json"));
        return new TextStyleEstimator(new InMemoryStore(bytes), catalogue, root);
    }

    private static OcrElement Word() => OcrElement.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        OcrElementKind.Word, "Example", 0.95, OcrTextType.Printed, 0,
        [new(0.1, 0.2), new(0.42, 0.2), new(0.42, 0.3), new(0.1, 0.3)]);

    private sealed class InMemoryStore(byte[] bytes) : IObjectStore
    {
        public Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<StoredObjectInfo?> HeadAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(bytes));
        public Task PromoteAsync(string from, string to, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task<ObjectCreationResult> WriteIfAbsentAsync(string key, string type, Stream content, CancellationToken ct) => throw new NotSupportedException();
    }
}
