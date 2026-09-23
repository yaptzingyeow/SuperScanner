using ImageMagick;
using ImageMagick.Drawing;
using SuperScanner.Domain.Ocr;
using SuperScanner.Domain.TextEditing;
using SuperScanner.Infrastructure.TextEditing;
using TextAlignment = SuperScanner.Domain.TextEditing.TextAlignment;

namespace SuperScanner.Application.Tests.TextEditing;

public sealed class TextEditRendererTests
{
    [Fact]
    public async Task Same_source_and_edit_produce_byte_identical_jpeg()
    {
        var renderer = CreateRenderer();
        var request = Request();
        var first = await renderer.RenderAsync(request, default);
        var second = await renderer.RenderAsync(request, default);

        Assert.Null(first.FailureCode);
        Assert.Equal(first.Output, second.Output);
        Assert.Equal(first.Sha256Hex, second.Sha256Hex);
        Assert.NotNull(first.ChangedPixelMask);
        Assert.Contains(true, first.ChangedPixelMask!);
        using var image = new MagickImage(first.Output!);
        Assert.Equal(MagickFormat.Jpeg, image.Format);
    }

    [Fact]
    public async Task Renderer_rejects_any_change_outside_the_approved_mask()
    {
        var renderer = CreateRenderer(new MutatingPainter());
        var result = await renderer.RenderAsync(Request(), default);
        Assert.Equal("text_edit_containment_failed", result.FailureCode);
        Assert.Null(result.Output);
    }

    [Fact]
    public async Task Rotated_text_stays_inside_approved_box()
    {
        var renderer = CreateRenderer();
        var style = new TextEditStyle("noto-sans", "archive-main-regular", .05,
            400, "#142435", 0, .5, 8, TextAlignment.Center);
        var result = await renderer.RenderAsync(Request() with { Style = style }, default);
        Assert.Null(result.FailureCode);
        Assert.NotNull(result.Output);
    }

    [Fact]
    public async Task Unsupported_font_fails_without_output()
    {
        var renderer = CreateRenderer();
        var style = new TextEditStyle("missing", "v1", .05,
            400, "#142435", 0, .5, 0, TextAlignment.Center);
        var result = await renderer.RenderAsync(Request() with { Style = style }, default);
        Assert.Equal("text_edit_font_unavailable", result.FailureCode);
        Assert.Null(result.Output);
    }

    [Fact]
    public async Task Unselected_dark_content_outside_box_survives_jpeg_encoding()
    {
        var renderer = CreateRenderer();
        var request = Request();
        using var source = new MagickImage(request.SourceBytes);
        new Drawables().FillColor(MagickColors.Black)
            .Rectangle(20, 20, 75, 30).Draw(source);
        var result = await renderer.RenderAsync(request with
        {
            SourceBytes = source.ToByteArray(MagickFormat.Jpeg)
        }, default);
        Assert.Null(result.FailureCode);
        Assert.NotNull(result.Output);
    }

    [Fact]
    public async Task Canonical_output_changes_with_exact_replacement_characters()
    {
        var renderer = CreateRenderer();
        var request = Request();
        var first = await renderer.RenderAsync(request, default);
        var second = await renderer.RenderAsync(request with { ReplacementText = "Tan BC" }, default);
        Assert.Null(first.FailureCode);
        Assert.Null(second.FailureCode);
        Assert.NotEqual(first.Sha256Hex, second.Sha256Hex);
    }

    private static TextEditRenderer CreateRenderer(ITextGlyphPainter? painter = null)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SuperScanner.slnx")))
            directory = directory.Parent;
        var root = directory?.FullName ?? throw new DirectoryNotFoundException();
        return new TextEditRenderer(
            BundledFontCatalogue.Load(Path.Combine(root, "assets/fonts/manifest.json")),
            root, new TextEditingOptions(), painter ?? new MagickGlyphPainter());
    }

    private static TextEditRenderRequest Request()
    {
        using var source = new MagickImage(MagickColors.White, 320, 200);
        new Drawables().FillColor(new MagickColor("#142435"))
            .Rectangle(108, 84, 143, 95).Draw(source);
        var selected = new OcrPoint[]
        {
            new(.30, .37), new(.48, .37), new(.48, .54), new(.30, .54)
        };
        return new TextEditRenderRequest(source.ToByteArray(MagickFormat.Png),
            [selected], [], new NormalizedBox(.28, .33, .48, .26),
            "Tan BB", new TextEditStyle("noto-sans", "archive-main-regular",
                .05, 400, "#142435", 0, .5, 0, TextAlignment.Center),
            TextEditRenderer.RendererVersion, TextLayoutEngine.LayoutVersion);
    }

    private sealed class MutatingPainter : ITextGlyphPainter
    {
        public void Paint(byte[] rgb, int width, int height, NormalizedBox box,
            string text, TextEditStyle style, string fontPath, TextLayoutResult fit)
        {
            rgb[0]--;
        }
    }
}
