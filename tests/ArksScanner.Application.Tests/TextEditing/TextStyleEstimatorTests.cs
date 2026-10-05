using System.Text.Json;
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
    [InlineData("Poppins-Regular.ttf", "poppins", 400, 46)]
    [InlineData("Carlito-Regular.ttf", "carlito", 400, 50)]
    [InlineData("Merriweather-Regular.ttf", "merriweather", 400, 40)]
    public async Task Picks_the_printed_font_and_its_real_pixel_size_from_a_padded_ocr_box(
        string file, string expectedFont, int expectedWeight, double size)
    {
        var root = RepositoryRoot();
        var (png, box) = Render("Monthly rent: RM 1,500", Path.Combine(root, "assets", "fonts", file), size);
        var estimator = new TextStyleEstimator(new Store(png),
            BundledFontCatalogue.Load(Path.Combine(root, "assets", "fonts", "manifest.json")), root);

        var estimate = await estimator.EstimateAsync("page.png", [Word("Monthly rent: RM 1,500", box)], default);

        Assert.Equal(expectedFont, estimate.Candidates[0].CatalogueId);
        Assert.Equal(expectedWeight, estimate.FontWeight);
        Assert.InRange(estimate.FontSizePoints, size * 0.9, size * 1.1);
        Assert.Equal("#000000", estimate.ColorHex);
    }

    [Theory]
    [InlineData("LiberationSans-Regular.ttf", "liberation-sans")]
    [InlineData("LiberationSerif-Regular.ttf", "liberation-serif")]
    public async Task Survives_a_scanned_page_upscaled_blurred_sharpened_and_jpeg_compressed(string file, string expectedFont)
    {
        var root = RepositoryRoot();
        var (png, box) = Render("RM 1,500", Path.Combine(root, "assets", "fonts", file), 34, pad: 1);
        using var scanned = new MagickImage(png);
        scanned.Resize(new Percentage(156));
        scanned.GaussianBlur(0, 1.2);
        scanned.UnsharpMask(0, 1.5, 1.2, 0.02);
        scanned.Quality = 82;
        var jpeg = scanned.ToByteArray(MagickFormat.Jpeg);
        var estimator = new TextStyleEstimator(new Store(jpeg),
            BundledFontCatalogue.Load(Path.Combine(root, "assets", "fonts", "manifest.json")), root);

        var estimate = await estimator.EstimateAsync("page.jpg", [Word("RM 1,500", box)], default);

        Assert.Equal(expectedFont, estimate.Candidates[0].CatalogueId);
        Assert.InRange(estimate.FontSizePoints, 34 * 1.56 * .9, 34 * 1.56 * 1.1);
    }

    [Fact]
    public async Task Recognises_arial_on_a_real_magic_scanned_page()
    {
        var root = RepositoryRoot();
        var folder = Path.Combine(AppContext.BaseDirectory, "TextEditing", "Fixtures");
        var png = await File.ReadAllBytesAsync(Path.Combine(folder, "scanned-arial-rm-1500.png"));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "scanned-arial-rm-1500.json")));
        var words = json.RootElement.EnumerateArray().Select((word, i) => OcrElement.Create(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), OcrElementKind.Word,
            // Google OCR word text keeps its trailing break ("RM ", "1,500" + newline); the estimator must cope.
            word.GetProperty("text").GetString()! + (i == 0 ? " " : Environment.NewLine), .98, OcrTextType.Printed, i + 1,
            word.GetProperty("polygon").EnumerateArray().Select(p => new OcrPoint(p.GetProperty("x").GetDouble(),
                p.GetProperty("y").GetDouble())).ToArray())).ToArray();
        var estimator = new TextStyleEstimator(new Store(png),
            BundledFontCatalogue.Load(Path.Combine(root, "assets", "fonts", "manifest.json")), root);

        var estimate = await estimator.EstimateAsync("page.png", words, default);

        // Liberation Sans is the bundled, metric-compatible match for Arial.
        Assert.Equal("liberation-sans", estimate.Candidates[0].CatalogueId);
        Assert.Equal(400, estimate.FontWeight);
        Assert.InRange(estimate.FontSizePoints, 48, 58);
    }

    [Fact]
    public async Task Only_suggests_fonts_that_can_draw_the_selected_script()
    {
        var root = RepositoryRoot();
        var (png, box) = Render("每月租金 1,500", Path.Combine(root, "assets", "fonts", "NotoSansSC-Regular.ttf"), 48);
        var catalogue = BundledFontCatalogue.Load(Path.Combine(root, "assets", "fonts", "manifest.json"));
        var estimator = new TextStyleEstimator(new Store(png), catalogue, root);

        var estimate = await estimator.EstimateAsync("page.png", [Word("每月租金 1,500", box)], default);

        Assert.NotEmpty(estimate.Candidates);
        Assert.All(estimate.Candidates, candidate =>
            Assert.Contains("Hani", catalogue.ScriptsOf(catalogue.Get(candidate.CatalogueId, candidate.Version))));
        Assert.InRange(estimate.FontSizePoints, 48 * .9, 48 * 1.1);
    }

    /// <summary>Draws black text on white and returns the ink box padded the way OCR word boxes are.</summary>
    private static (byte[] Png, (double X0, double Y0, double X1, double Y1) Box) Render(string text, string font, double size, int pad = 9)
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
