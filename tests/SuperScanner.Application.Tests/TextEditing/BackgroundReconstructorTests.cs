using SuperScanner.Domain.Ocr;
using SuperScanner.Domain.TextEditing;
using SuperScanner.Infrastructure.TextEditing;

namespace SuperScanner.Application.Tests.TextEditing;

public sealed class BackgroundReconstructorTests
{
    [Fact]
    public void Name_between_form_rules_is_erased_without_changing_either_rule()
    {
        var rgb = Paper(100, 60, (_, _) => 245);
        Ink(rgb, 100, 0, 21, 100, 2, 20);
        Ink(rgb, 100, 0, 34, 100, 2, 20);
        Ink(rgb, 100, 35, 25, 6, 8, 10);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, 100, 60, [Poly(.30, .35, .40, .25)], [],
            new NormalizedBox(.28, .32, .46, .31), 2));

        Assert.Null(result.FailureCode);
        Assert.Equal((byte)245, result.Pixels![(28 * 100 + 38) * 3]);
        foreach (var y in new[] { 21, 22, 34, 35 })
        for (var x = 0; x < 100; x++)
        {
            Assert.Equal((byte)20, result.Pixels[(y * 100 + x) * 3]);
            Assert.False(result.RepairMask![y * 100 + x]);
        }
    }

    [Fact]
    public void Blocked_side_samples_use_nearby_paper_and_preserve_neighbour_words()
    {
        var rgb = Paper(100, 60, (_, _) => 245);
        Ink(rgb, 100, 22, 25, 7, 5, 10);
        Ink(rgb, 100, 35, 25, 6, 8, 10);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, 100, 60, [Poly(.30, .35, .40, .25)], [Poly(.21, .40, .08, .12)],
            new NormalizedBox(.28, .32, .46, .31), 2));
        Assert.Null(result.FailureCode);
        Assert.Equal((byte)245, result.Pixels![(28 * 100 + 38) * 3]);
        Assert.Equal((byte)10, result.Pixels[(28 * 100 + 25) * 3]);
    }

    [Fact]
    public void Plain_paper_removes_only_selected_foreground()
    {
        var rgb = Paper(100, 60, (_, _) => 245);
        Ink(rgb, 100, 35, 25, 6, 8, 10);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, 100, 60, [Poly(.30, .35, .40, .25)], [],
            new NormalizedBox(.28, .32, .46, .31), 2));

        Assert.Null(result.FailureCode);
        Assert.NotNull(result.Pixels);
        Assert.True(result.RepairMask![30 * 100 + 40]);
        Assert.Equal((byte)245, result.Pixels![(30 * 100 + 40) * 3]);
        Assert.Equal((byte)245, result.Pixels[(30 * 100 + 20) * 3]);
    }

    [Fact]
    public void Gradient_paper_uses_local_row_background()
    {
        var rgb = Paper(100, 60, (_, y) => (byte)(210 + y / 2));
        Ink(rgb, 100, 35, 25, 6, 8, 10);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, 100, 60, [Poly(.30, .35, .40, .25)], [],
            new NormalizedBox(.28, .32, .46, .31), 1));

        Assert.Null(result.FailureCode);
        Assert.InRange(result.Pixels![(30 * 100 + 40) * 3], (byte)223, (byte)227);
    }

    [Fact]
    public void Crossing_form_line_is_unsafe()
    {
        var rgb = Paper(100, 60, (_, _) => 245);
        Ink(rgb, 100, 0, 30, 100, 2, 20);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, 100, 60, [Poly(.30, .35, .40, .25)], [],
            new NormalizedBox(.28, .32, .46, .31), 2));
        Assert.Equal("text_edit_unsafe_background", result.FailureCode);
        Assert.Null(result.Pixels);
    }

    [Fact]
    public void Protected_neighbour_inside_dilation_is_unsafe()
    {
        var rgb = Paper(100, 60, (_, _) => 245);
        Ink(rgb, 100, 35, 25, 6, 8, 10);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, 100, 60, [Poly(.30, .35, .40, .25)],
            [Poly(.39, .39, .12, .15)], new NormalizedBox(.28, .32, .46, .31), 2));
        Assert.Equal("text_edit_unsafe_background", result.FailureCode);
    }

    [Fact]
    public void Placement_box_may_overlap_adjacent_word_without_erasing_its_ink()
    {
        var rgb = Paper(100, 60, (_, _) => 245);
        Ink(rgb, 100, 36, 25, 5, 8, 10);
        Ink(rgb, 100, 75, 25, 5, 8, 10);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, 100, 60, [Poly(.30, .35, .40, .25)],
            [Poly(.74, .35, .07, .25)],
            new NormalizedBox(.28, .32, .55, .31), 2));

        Assert.Null(result.FailureCode);
        Assert.Equal((byte)245, result.Pixels![(28 * 100 + 38) * 3]);
        Assert.Equal((byte)10, result.Pixels[(28 * 100 + 77) * 3]);
        Assert.False(result.RepairMask![28 * 100 + 77]);
    }

    [Fact]
    public void Adjacent_recognized_word_does_not_make_plain_paper_unsafe()
    {
        var rgb = Paper(100, 60, (_, _) => 245);
        Ink(rgb, 100, 25, 25, 3, 8, 10);
        Ink(rgb, 100, 36, 25, 5, 8, 10);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, 100, 60, [Poly(.30, .35, .40, .25)],
            [Poly(.24, .35, .04, .25)],
            new NormalizedBox(.30, .35, .40, .25), 2));

        Assert.Null(result.FailureCode);
        Assert.Equal((byte)245, result.Pixels![(28 * 100 + 38) * 3]);
        Assert.Equal((byte)10, result.Pixels[(28 * 100 + 26) * 3]);
    }

    [Fact]
    public void Ink_one_pixel_beyond_ocr_polygon_is_repaired_within_padded_box()
    {
        var rgb = Paper(100, 60, (_, _) => 245);
        Ink(rgb, 100, 25, 25, 3, 8, 10);
        Ink(rgb, 100, 65, 25, 6, 8, 10);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, 100, 60, [Poly(.30, .35, .40, .25)],
            [Poly(.24, .35, .04, .25)],
            new NormalizedBox(.28, .35, .44, .25), 2));

        Assert.Null(result.FailureCode);
        Assert.Equal((byte)245, result.Pixels![(28 * 100 + 70) * 3]);
        Assert.Equal((byte)10, result.Pixels[(28 * 100 + 26) * 3]);
    }

    [Fact]
    public void Two_pixel_ocr_under_run_is_cleared_without_touching_a_protected_neighbour()
    {
        var rgb = Paper(100, 60, (_, _) => 245);
        Ink(rgb, 100, 65, 25, 8, 8, 10);
        Ink(rgb, 100, 77, 25, 3, 8, 10);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, 100, 60, [Poly(.30, .35, .40, .25)],
            [Poly(.76, .35, .05, .25)],
            new NormalizedBox(.28, .32, .46, .31), 2));

        Assert.Null(result.FailureCode);
        Assert.Equal((byte)245, result.Pixels![(28 * 100 + 72) * 3]);
        Assert.Equal((byte)10, result.Pixels[(28 * 100 + 78) * 3]);
    }

    [Fact]
    public void Dilation_above_the_first_sampled_row_uses_the_nearest_paper_row()
    {
        var rgb = Paper(100, 60, (_, _) => 245);
        Ink(rgb, 100, 35, 20, 5, 8, 10);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, 100, 60, [Poly(.30, .33, .40, .25)], [],
            new NormalizedBox(.28, .30, .44, .32), 2));

        Assert.Null(result.FailureCode);
        Assert.Equal((byte)245, result.Pixels![(20 * 100 + 37) * 3]);
    }

    [Fact]
    public void Bright_paper_highlight_inside_the_selected_polygon_is_not_old_ink()
    {
        var rgb = Paper(100, 60, (_, _) => 210);
        Ink(rgb, 100, 35, 25, 5, 8, 10);
        Ink(rgb, 100, 50, 25, 3, 8, 250);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, 100, 60, [Poly(.30, .35, .40, .25)], [],
            new NormalizedBox(.28, .32, .44, .31), 2));

        Assert.Null(result.FailureCode);
        Assert.Equal((byte)250, result.Pixels![(28 * 100 + 51) * 3]);
        Assert.Equal((byte)210, result.Pixels[(28 * 100 + 37) * 3]);
    }

    [Fact]
    public void Tight_box_cannot_leave_original_ink_outside_the_repair_area()
    {
        var rgb = Paper(100, 60, (_, _) => 245);
        Ink(rgb, 100, 65, 25, 6, 8, 10);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, 100, 60, [Poly(.30, .35, .40, .25)], [],
            new NormalizedBox(.30, .35, .40, .25), 2));

        Assert.Equal("text_edit_unsafe_background", result.FailureCode);
        Assert.Null(result.Pixels);
    }

    [Fact]
    public void Dilation_never_crosses_the_approved_box_edge()
    {
        var rgb = Paper(100, 60, (_, _) => 245);
        Ink(rgb, 100, 31, 25, 2, 5, 10);
        var box = new NormalizedBox(.30, .30, .42, .35);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, 100, 60, [Poly(.30, .35, .40, .25)], [], box, 4));
        Assert.Null(result.FailureCode);
        Assert.False(result.RepairMask![27 * 100 + 29]);
        Assert.True(result.RepairMask[27 * 100 + 31]);
    }

    [Fact]
    public void Slightly_rotated_selection_is_reconstructed()
    {
        var rgb = Paper(100, 60, (_, _) => 245);
        Ink(rgb, 100, 40, 25, 4, 7, 10);
        IReadOnlyList<OcrPoint> polygon =
            [new(.28, .30), new(.70, .34), new(.68, .63), new(.26, .59)];
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, 100, 60, [polygon], [],
            new NormalizedBox(.24, .27, .50, .40), 1));
        Assert.Null(result.FailureCode);
    }

    [Fact]
    public void High_frequency_background_texture_is_unsafe()
    {
        var rgb = Paper(100, 60, (x, _) => (byte)(x % 2 == 0 ? 210 : 245));
        Ink(rgb, 100, 35, 25, 6, 8, 10);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, 100, 60, [Poly(.30, .35, .40, .25)], [],
            new NormalizedBox(.28, .32, .46, .31), 2));
        Assert.Equal("text_edit_unsafe_background", result.FailureCode);
    }

    [Fact]
    public void Moderate_scanned_paper_grain_does_not_block_a_word_replacement()
    {
        var rgb = Paper(100, 60, (x, _) => (byte)(x % 2 == 0 ? 194 : 220));
        Ink(rgb, 100, 35, 25, 6, 8, 10);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, 100, 60, [Poly(.30, .35, .40, .25)], [],
            new NormalizedBox(.28, .32, .46, .31), 2));
        Assert.Null(result.FailureCode);
    }

    [Fact]
    public void Isolated_paper_grain_outliers_do_not_fail_the_border_check()
    {
        var rgb = Paper(100, 60, (_, _) => 210);
        Ink(rgb, 100, 35, 25, 6, 8, 10);
        Ink(rgb, 100, 28, 25, 1, 8, 190);
        Ink(rgb, 100, 77, 25, 1, 8, 230);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, 100, 60, [Poly(.30, .35, .40, .25)], [],
            new NormalizedBox(.28, .32, .46, .31), 2));
        Assert.Null(result.FailureCode);
    }

    private static byte[] Paper(int width, int height, Func<int, int, byte> shade)
    {
        var pixels = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            Array.Fill(pixels, shade(x, y), (y * width + x) * 3, 3);
        return pixels;
    }

    private static void Ink(byte[] rgb, int width, int x, int y, int w, int h, byte value)
    {
        for (var row = y; row < y + h; row++)
        for (var col = x; col < x + w; col++)
            Array.Fill(rgb, value, (row * width + col) * 3, 3);
    }

    private static IReadOnlyList<OcrPoint> Poly(double x, double y, double w, double h) =>
        [new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h)];
}
