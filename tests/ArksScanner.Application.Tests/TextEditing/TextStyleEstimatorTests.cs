using ImageMagick;
using ImageMagick.Drawing;
using ArksScanner.Application.Abstractions;
using ArksScanner.Domain.Ocr;
using ArksScanner.Infrastructure.TextEditing;

namespace ArksScanner.Application.Tests.TextEditing;

public sealed class TextStyleEstimatorTests
{
    private const int Width = 1654;
    private const int Height = 2400;

    [Theory]
    [InlineData("LiberationSans-Regular.ttf", "liberation-sans", 400, 53)]
    [InlineData("LiberationSans-Regular.ttf", "liberation-sans", 400, 28)]
    [InlineData("LiberationSerif-Bold.ttf", "liberation-serif", 700, 44)]
    public async Task Picks_the_printed_font_and_its_real_pixel_size_from_a_padded_ocr_box(
        string file, string expectedFont, int expectedWeight, double size)
    {
        var root = RepositoryRoot();
        var (png, box) = Render("RM 1,500", Path.Combine(root, "assets", "fonts", file), size);
        var estimator = new TextStyleEstimator(new Store(png),
            BundledFontCatalogue.Load(Path.Combine(root, "assets", "fonts", "manifest.json")), root);

        var estimate = await estimator.EstimateAsync("page.png", [Word("RM 1,500", box)], default);

        Assert.Equal(expectedFont, estimate.Candidates[0].CatalogueId);
        Assert.Equal(expectedWeight, estimate.FontWeight);
        Assert.InRange(estimate.FontSizePoints, size * 0.9, size * 1.1);
        Assert.Equal("#000000", estimate.ColorHex);
    }

    /// <summary>Draws black text on white and returns the ink box padded the way OCR word boxes are.</summary>
    private static (byte[] Png, (double X0, double Y0, double X1, double Y1) Box) Render(string text, string font, double size)
    {
        using var image = new MagickImage(MagickColors.White, Width, Height);
        new Drawables().Font(font).FontPointSize(size).FillColor(MagickColors.Black).Text(300, 1000, text).Draw(image);
        var gray = image.GetPixels().ToByteArray(PixelMapping.RGB)!;
        int left = Width, top = Height, right = 0, bottom = 0;
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            if (gray[(y * Width + x) * 3] > 128) continue;
            left = Math.Min(left, x); right = Math.Max(right, x);
            top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        const int pad = 9;
        var box = ((left - pad) / (double)Width, (top - pad) / (double)Height,
            (right + pad) / (double)Width, (bottom + pad) / (double)Height);
        return (image.ToByteArray(MagickFormat.Png), box);
    }

    private static OcrElement Word(string text, (double X0, double Y0, double X1, double Y1) b) =>
        OcrElement.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), OcrElementKind.Word, text, .98,
            OcrTextType.Printed, 1, [new(b.X0, b.Y0), new(b.X1, b.Y0), new(b.X1, b.Y1), new(b.X0, b.Y1)]);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "assets", "fonts", "manifest.json")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class Store(byte[] png) : IObjectStore
    {
        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(png));
        public Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StoredObjectInfo?> HeadAsync(string objectKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task PromoteAsync(string quarantineKey, string acceptedKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ObjectCreationResult> WriteIfAbsentAsync(string objectKey, string mediaType, Stream content, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
