using ImageMagick;
using ImageMagick.Drawing;
using ArksScanner.Domain.Ocr;
using ArksScanner.Domain.TextEditing;
using ArksScanner.Infrastructure.TextEditing;
using TextAlignment = ArksScanner.Domain.TextEditing.TextAlignment;

namespace ArksScanner.Application.Tests.TextEditing;

public sealed class TextEditRendererTests
{
    [Fact]
    public async Task Delete_removes_selected_ink_without_drawing_new_letters()
    {
        var request = Request() with { ReplacementText = "" };
        var result = await CreateRenderer().RenderAsync(request, default);
        Assert.Null(result.FailureCode);
        using var output = new MagickImage(result.Output!);
        var pixels = output.GetPixels().ToByteArray(PixelMapping.RGB)!;
        Assert.Equal((byte)255, pixels[(88 * 320 + 120) * 3]);
    }

    [Fact]
    public async Task Add_draws_letters_over_existing_content_without_a_background_patch()
    {
        var request = Request() with
        {
            SelectedPolygons = [],
            ProtectedPolygons = [new OcrPoint[] {
                new(.30, .37), new(.48, .37), new(.48, .54), new(.30, .54) }],
            ApprovedBox = new NormalizedBox(.28, .33, .48, .26)
        };
        var result = await CreateRenderer().RenderAsync(request, default);
        Assert.Null(result.FailureCode);
        using var output = new MagickImage(result.Output!);
        var pixels = output.GetPixels().ToByteArray(PixelMapping.RGB)!;
        Assert.Equal((byte)20, pixels[(88 * 320 + 120) * 3]);
        Assert.Contains(true, result.ChangedPixelMask!);
    }

    [Fact]
    public async Task Add_rejects_any_unrelated_pixel_change_outside_its_box()
    {
        var result = await CreateRenderer(new MutatingPainter()).RenderAsync(Request() with {
            SelectedPolygons = []
        }, default);
        Assert.Equal("text_edit_containment_failed", result.FailureCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public async Task Tight_word_box_renders_without_counting_transparent_padding(double angle)
    {
        using var source = new MagickImage(MagickColors.White, 320, 200);
        new Drawables().FillColor(MagickColors.Black).Rectangle(105, 85, 126, 89).Draw(source);
        var request = Request() with
        {
            SourceBytes = source.ToByteArray(MagickFormat.Png),
            SelectedPolygons = [new OcrPoint[] {
                new(.31, .41), new(.42, .41), new(.42, .46), new(.31, .46) }],
            ApprovedBox = new NormalizedBox(.3, .4, .135, .08),
            Style = new TextEditStyle("noto-sans", "archive-main-regular", .055,
                400, "#142435", 0, .75, angle, TextAlignment.Left)
        };
        var result = await CreateRenderer().RenderAsync(request, default);
        Assert.Null(result.FailureCode);
        Assert.NotNull(result.Output);
        Assert.Contains(true, result.ChangedPixelMask!);
    }

    [Fact]
    public async Task Replacement_keeps_the_original_letter_height_in_a_tight_ocr_box()
    {
        var root = RootDirectory();
        var font = Path.Combine(root, "assets", "fonts", "LiberationSans-Regular.ttf");
        using var source = new MagickImage(MagickColors.White, 1000, 300);
        new Drawables().Font(font).FontPointSize(53).FillColor(MagickColors.Black).Text(100, 160, "RM 1,500").Draw(source);
        var before = InkBox(source, 0, 0, 1000, 300);
        static double nx(int x) => x / 1000.0;
        static double ny(int y) => y / 300.0;
        var request = new TextEditRenderRequest(source.ToByteArray(MagickFormat.Png),
            [new OcrPoint[] { new(nx(before.Left - 3), ny(before.Top - 3)), new(nx(before.Right + 3), ny(before.Top - 3)),
                new(nx(before.Right + 3), ny(before.Bottom + 3)), new(nx(before.Left - 3), ny(before.Bottom + 3)) }],
            [], new NormalizedBox(nx(before.Left - 6), ny(before.Top - 6), nx(before.Right - before.Left + 12),
                ny(before.Bottom - before.Top + 12)),
            "RM 1,800", new TextEditStyle("liberation-sans", "liberation-2-1-5-regular", 53 / 300.0, 400,
                "#000000", 0, .75, 0, TextAlignment.Left),
            TextEditRenderer.RendererVersion, TextLayoutEngine.LayoutVersion);

        var result = await CreateRenderer().RenderAsync(request, default);

        Assert.Null(result.FailureCode);
        using var output = new MagickImage(result.Output!);
        var after = InkBox(output, before.Left - 8, before.Top - 8, before.Right + 8, before.Bottom + 8);
        Assert.InRange(after.Bottom - after.Top, (before.Bottom - before.Top) * .93, (before.Bottom - before.Top) * 1.07);
        Assert.InRange(after.Top, before.Top - 3, before.Top + 3);
    }

    private static (int Left, int Top, int Right, int Bottom) InkBox(MagickImage image, int x0, int y0, int x1, int y1)
    {
        var width = (int)image.Width;
        var rgb = image.GetPixels().ToByteArray(PixelMapping.RGB)!;
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (var y = Math.Max(0, y0); y < Math.Min((int)image.Height, y1); y++)
        for (var x = Math.Max(0, x0); x < Math.Min(width, x1); x++)
        {
            if (rgb[(y * width + x) * 3] > 128) continue;
            left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        return (left, top, right, bottom);
    }

    private static string RootDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ArksScanner.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    [Fact]
    public async Task Same_source_and_edit_produce_byte_identical_png()
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
        Assert.Equal(MagickFormat.Png, image.Format);
    }

    [Fact]
    public async Task Glyph_part_above_the_ocr_and_placement_boxes_is_removed_too()
    {
        using var source = new MagickImage(MagickColors.White, 320, 200);
        // The stroke starts at y=64, above both the OCR polygon (74) and the
        // placement box (66): a tall glyph the OCR box did not fully cover.
        new Drawables().FillColor(new MagickColor("#142435")).Rectangle(108, 64, 143, 95).Draw(source);
        var request = Request() with { SourceBytes = source.ToByteArray(MagickFormat.Png), ReplacementText = "" };

        var result = await CreateRenderer().RenderAsync(request, default);

        Assert.Null(result.FailureCode);
        using var output = new MagickImage(result.Output!);
        var pixels = output.GetPixels().ToByteArray(PixelMapping.RGB)!;
        Assert.Equal((byte)255, pixels[(64 * 320 + 120) * 3]);
        Assert.Equal((byte)255, pixels[(65 * 320 + 140) * 3]);
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
    public async Task Renderer_rejects_glyphs_that_would_paint_over_a_protected_word()
    {
        var request = Request() with
        {
            ProtectedPolygons = [new OcrPoint[] {
                new(.70, .40), new(.74, .40), new(.74, .46), new(.70, .46) }]
        };
        var result = await CreateRenderer(new MutatingProtectedPainter())
            .RenderAsync(request, default);

        Assert.Equal("text_edit_placement_overlap", result.FailureCode);
        Assert.Null(result.Output);
    }

    [Fact]
    public async Task Unsafe_background_reports_which_guard_rejected_the_selection()
    {
        var request = Request();
        var result = await CreateRenderer().RenderAsync(request with
        {
            ProtectedPolygons = request.SelectedPolygons
        }, default);

        Assert.Equal("text_edit_unsafe_background", result.FailureCode);
        Assert.Equal("selected-protected-overlap", result.DiagnosticReason);
        Assert.Null(result.Output);
    }

    [Fact]
    public async Task Moving_the_placement_box_does_not_leave_the_original_ink_behind()
    {
        var request = Request() with
        {
            ApprovedBox = new NormalizedBox(.52, .33, .36, .26)
        };
        var result = await CreateRenderer().RenderAsync(request, default);

        Assert.Null(result.FailureCode);
        using var output = new MagickImage(result.Output!);
        var pixels = output.GetPixels().ToByteArray(PixelMapping.RGB)!;
        Assert.Equal((byte)255, pixels[(88 * 320 + 120) * 3]);
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
    public async Task Weight_that_disagrees_with_the_pinned_font_face_fails_without_output()
    {
        var style = new TextEditStyle("noto-sans", "archive-main-regular", .05,
            700, "#142435", 0, .5, 0, TextAlignment.Center);
        var result = await CreateRenderer().RenderAsync(Request() with { Style = style }, default);
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
    public async Task Photo_texture_outside_edit_box_remains_pixel_identical()
    {
        var rgb = new byte[320 * 200 * 3];
        Array.Fill(rgb, (byte)245);
        for (var y = 5; y < 60; y++)
        for (var x = 5; x < 80; x++)
        {
            var value = (byte)((x + y) % 2 == 0 ? 15 : 235);
            Array.Fill(rgb, value, (y * 320 + x) * 3, 3);
        }
        for (var y = 84; y <= 95; y++)
        for (var x = 108; x <= 143; x++)
            Array.Fill(rgb, (byte)20, (y * 320 + x) * 3, 3);
        using var source = new MagickImage(MagickColors.White, 320, 200);
        source.ImportPixels(rgb, new PixelImportSettings(320, 200,
            StorageType.Char, PixelMapping.RGB));
        source.Quality = 65;
        var request = Request() with { SourceBytes = source.ToByteArray(MagickFormat.Jpeg) };

        var result = await CreateRenderer().RenderAsync(request, default);

        Assert.Null(result.FailureCode);
        using var original = new MagickImage(request.SourceBytes);
        using var output = new MagickImage(result.Output!);
        var originalRgb = original.GetPixels().ToByteArray(PixelMapping.RGB)!;
        var outputRgb = output.GetPixels().ToByteArray(PixelMapping.RGB)!;
        for (var y = 5; y < 60; y++)
        for (var x = 5; x < 80; x++)
        for (var channel = 0; channel < 3; channel++)
            Assert.Equal(originalRgb[(y * 320 + x) * 3 + channel],
                outputRgb[(y * 320 + x) * 3 + channel]);
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

    [Fact]
    public async Task Replacement_fits_between_rules_and_keeps_rule_pixels_unchanged()
    {
        var request = Request();
        using var source = new MagickImage(request.SourceBytes);
        new Drawables().StrokeColor(MagickColors.Black).StrokeWidth(2)
            .Line(20, 76, 300, 76).Line(20, 104, 300, 104).Draw(source);
        var result = await CreateRenderer().RenderAsync(request with
        {
            SourceBytes = source.ToByteArray(MagickFormat.Png),
            ApprovedBox = new NormalizedBox(.28, .37, .48, .17)
        }, default);
        Assert.Null(result.FailureCode);
        using var output = new MagickImage(result.Output!);
        var before = source.GetPixels().ToByteArray(PixelMapping.RGB)!;
        var after = output.GetPixels().ToByteArray(PixelMapping.RGB)!;
        foreach (var y in new[] { 76, 104 })
        for (var x = 20; x <= 300; x++)
        for (var c = 0; c < 3; c++)
            Assert.Equal(before[(y * 320 + x) * 3 + c], after[(y * 320 + x) * 3 + c]);
    }

    private static TextEditRenderer CreateRenderer(ITextGlyphPainter? painter = null)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ArksScanner.slnx")))
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

    private sealed class MutatingProtectedPainter : ITextGlyphPainter
    {
        public void Paint(byte[] rgb, int width, int height, NormalizedBox box,
            string text, TextEditStyle style, string fontPath, TextLayoutResult fit)
        {
            rgb[(86 * width + 230) * 3] = 0;
        }
    }
}
