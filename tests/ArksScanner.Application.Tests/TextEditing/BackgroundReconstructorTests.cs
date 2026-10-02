using ArksScanner.Domain.Ocr;
using ArksScanner.Domain.TextEditing;
using ArksScanner.Infrastructure.TextEditing;

namespace ArksScanner.Application.Tests.TextEditing;

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
    public void Neighbouring_box_that_touches_the_selection_keeps_its_ink_and_the_edit_proceeds()
    {
        // OCR often gives a comma a box that overlaps the end of the word before it.
        var rgb = Paper(100, 60, (_, _) => 245);
        Ink(rgb, 100, 36, 25, 5, 8, 10);
        Ink(rgb, 100, 71, 30, 2, 4, 10);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, 100, 60, [Poly(.30, .35, .40, .25)],
            [Poly(.68, .35, .06, .25)],
            new NormalizedBox(.28, .32, .40, .31), 2));

        Assert.Null(result.FailureCode);
        Assert.Equal((byte)245, result.Pixels![(28 * 100 + 38) * 3]);
        Assert.Equal((byte)10, result.Pixels[(31 * 100 + 72) * 3]);
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
    public void Tight_box_does_not_leave_original_ink_just_outside_it()
    {
        // The stroke runs one pixel past the OCR box; it is part of the same
        // glyph and is removed rather than left as a fragment.
        var rgb = Paper(100, 60, (_, _) => 245);
        Ink(rgb, 100, 65, 25, 6, 8, 10);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, 100, 60, [Poly(.30, .35, .40, .25)], [],
            new NormalizedBox(.30, .35, .40, .25), 2));

        Assert.Null(result.FailureCode);
        for (var x = 65; x < 71; x++)
            Assert.Equal((byte)245, result.Pixels![(28 * 100 + x) * 3]);
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
    public void High_frequency_background_texture_is_continued_through_the_word()
    {
        // Repair copies nearby background, so a regular texture is reproduced
        // instead of being flattened into a smooth patch.
        var rgb = Paper(100, 60, (x, _) => (byte)(x % 2 == 0 ? 210 : 245));
        Ink(rgb, 100, 35, 25, 6, 8, 10);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, 100, 60, [Poly(.30, .35, .40, .25)], [],
            new NormalizedBox(.28, .32, .46, .31), 2));
        Assert.Null(result.FailureCode);
        for (var x = 35; x < 41; x++)
            Assert.Equal(x % 2 == 0 ? (byte)210 : (byte)245, result.Pixels![(28 * 100 + x) * 3]);
    }

    [Fact]
    public void Mottled_card_texture_behind_a_word_is_kept_not_flattened()
    {
        const int width = 240, height = 160;
        var rgb = Paper(width, height, Mottled);
        Ink(rgb, width, 90, 60, 16, 40, 15);
        Ink(rgb, width, 120, 60, 16, 40, 15);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, width, height, [Poly(85 / 240.0, 55 / 160.0, 60 / 240.0, 50 / 160.0)], [],
            new NormalizedBox(83 / 240.0, 53 / 160.0, 64 / 240.0, 54 / 160.0), 2));

        Assert.Null(result.FailureCode);
        var repaired = Enumerable.Range(0, width * height).Where(i => result.RepairMask![i]).ToArray();
        Assert.NotEmpty(repaired);
        var values = repaired.Select(i => (double)result.Pixels![i * 3]).ToArray();
        var reference = Enumerable.Range(0, width * height)
            .Where(i => i % width is < 70 or > 170 && i / width is > 55 and < 105)
            .Select(i => (double)rgb[i * 3]).ToArray();
        Assert.InRange(values.Average(), reference.Average() - 8, reference.Average() + 8);
        Assert.True(values.Min() > 120);
        // Filling each row with one colour leaves no variation along the row;
        // copied background keeps the mottling in both directions.
        var insideRepair = repaired.Where(i => result.RepairMask![i + 1])
            .Select(i => (double)Math.Abs(result.Pixels![(i + 1) * 3] - result.Pixels[i * 3]));
        var background = Enumerable.Range(60, 40).SelectMany(y => Enumerable.Range(20, 40)
            .Select(x => (double)Math.Abs(rgb[(y * width + x + 1) * 3] - rgb[(y * width + x) * 3])));
        Assert.True(insideRepair.Average() > .6 * background.Average());
    }

    [Fact]
    public void Darker_print_behind_black_letters_is_not_treated_as_ink()
    {
        // Light paper at the sides, a darker mottled print behind the word and
        // near-black letters on top, like a title printed over a pattern.
        const int width = 240, height = 160;
        var rgb = Paper(width, height, (x, y) =>
            x is >= 84 and < 146 && y is >= 54 and < 106 ? (byte)(Mottled(x, y) - 45) : (byte)232);
        Ink(rgb, width, 92, 60, 10, 40, 20);
        Ink(rgb, width, 112, 60, 10, 40, 20);
        Ink(rgb, width, 132, 60, 10, 40, 20);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, width, height, [Poly(85 / 240.0, 55 / 160.0, 60 / 240.0, 50 / 160.0)], [],
            new NormalizedBox(83 / 240.0, 53 / 160.0, 64 / 240.0, 54 / 160.0), 2));

        Assert.Null(result.FailureCode);
        var repaired = result.RepairMask!.Count(value => value);
        const int ink = 3 * 10 * 40;
        Assert.InRange(repaired, ink, ink * 2);
        Assert.Equal(rgb[(80 * width + 105) * 3], result.Pixels![(80 * width + 105) * 3]);
    }

    [Fact]
    public void Dark_artwork_near_the_word_is_never_copied_into_the_repair()
    {
        const int width = 240, height = 160;
        // A black card border just left of a mottled panel, as on a printed card.
        var rgb = Paper(width, height, (x, y) => x < 70 ? (byte)25 : Mottled(x, y));
        Ink(rgb, width, 90, 60, 14, 40, 15);
        Ink(rgb, width, 120, 60, 14, 40, 15);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, width, height, [Poly(85 / 240.0, 55 / 160.0, 55 / 240.0, 50 / 160.0)], [],
            new NormalizedBox(83 / 240.0, 53 / 160.0, 59 / 240.0, 54 / 160.0), 2));

        Assert.Null(result.FailureCode);
        var repaired = Enumerable.Range(0, width * height).Where(i => result.RepairMask![i]);
        Assert.All(repaired, i => Assert.True(result.Pixels![i * 3] > 150));
    }

    [Fact]
    public void Bright_sharpening_halo_around_letters_is_repaired_with_the_ink()
    {
        const int width = 240, height = 160;
        var rgb = Paper(width, height, Mottled);
        // Sharpened scans put a light rim around dark strokes.
        Ink(rgb, width, 87, 57, 20, 46, 252);
        Ink(rgb, width, 117, 57, 20, 46, 252);
        Ink(rgb, width, 90, 60, 14, 40, 15);
        Ink(rgb, width, 120, 60, 14, 40, 15);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, width, height, [Poly(85 / 240.0, 55 / 160.0, 55 / 240.0, 50 / 160.0)], [],
            new NormalizedBox(83 / 240.0, 53 / 160.0, 59 / 240.0, 54 / 160.0), 2));

        Assert.Null(result.FailureCode);
        for (var y = 58; y < 102; y++)
        foreach (var x in new[] { 88, 106, 118, 136 })
            Assert.True(result.Pixels![(y * width + x) * 3] < 240, $"halo kept at {x},{y}");
    }

    [Fact]
    public void Glyph_tops_above_a_tight_ocr_box_are_removed_with_the_word()
    {
        // Decorative capitals rise above the OCR polygon; the whole connected
        // glyph must go, not just the part inside the box.
        const int width = 240, height = 160;
        var rgb = Paper(width, height, Mottled);
        Ink(rgb, width, 90, 50, 14, 50, 15);   // strokes start 10 px above the box
        Ink(rgb, width, 120, 50, 14, 50, 15);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, width, height, [Poly(85 / 240.0, 60 / 160.0, 55 / 240.0, 40 / 160.0)], [],
            new NormalizedBox(83 / 240.0, 58 / 160.0, 59 / 240.0, 44 / 160.0), 2));

        Assert.Null(result.FailureCode);
        for (var y = 50; y < 100; y++)
        foreach (var x in new[] { 92, 97, 124, 130 })
            Assert.True(result.Pixels![(y * width + x) * 3] > 150, $"ink kept at {x},{y}");
    }

    [Fact]
    public void Large_dark_area_touching_the_word_is_not_swept_into_the_repair()
    {
        const int width = 240, height = 160;
        var rgb = Paper(width, height, Mottled);
        Ink(rgb, width, 90, 60, 14, 40, 15);
        Ink(rgb, width, 0, 0, width, 62, 20);   // dark band joined to the top of the word
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, width, height, [Poly(85 / 240.0, 62 / 160.0, 55 / 240.0, 38 / 160.0)], [],
            new NormalizedBox(83 / 240.0, 60 / 160.0, 59 / 240.0, 42 / 160.0), 2));

        for (var x = 0; x < width; x++)
            Assert.Equal((byte)20, (result.Pixels ?? rgb)[(30 * width + x) * 3]);
    }

    [Fact]
    public void Repair_follows_a_vertical_gradient_on_textured_card()
    {
        const int width = 240, height = 160;
        var rgb = Paper(width, height, (x, y) => (byte)Math.Clamp(Mottled(x, y) - 60 + y * 3 / 4, 0, 255));
        Ink(rgb, width, 100, 50, 30, 50, 15);
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, width, height, [Poly(95 / 240.0, 45 / 160.0, 40 / 240.0, 60 / 160.0)], [],
            new NormalizedBox(93 / 240.0, 43 / 160.0, 44 / 240.0, 64 / 160.0), 2));

        Assert.Null(result.FailureCode);
        double RowMean(byte[] pixels, int y, int from, int to) =>
            Enumerable.Range(from, to - from).Average(x => (double)pixels[(y * width + x) * 3]);
        foreach (var y in new[] { 55, 75, 95 })
            Assert.InRange(RowMean(result.Pixels!, y, 100, 130) - RowMean(rgb, y, 40, 90), -12, 12);
    }

    [Fact]
    public void Repair_never_copies_a_protected_neighbour_word_into_the_gap()
    {
        const int width = 240, height = 160;
        var rgb = Paper(width, height, Mottled);
        Ink(rgb, width, 100, 60, 30, 30, 15);
        Ink(rgb, width, 160, 60, 30, 30, 15);   // recognised neighbour, must stay
        var result = BackgroundReconstructor.Reconstruct(new BackgroundReconstructionRequest(
            rgb, width, height, [Poly(95 / 240.0, 55 / 160.0, 40 / 240.0, 40 / 160.0)],
            [Poly(158 / 240.0, 58 / 160.0, 34 / 240.0, 34 / 160.0)],
            new NormalizedBox(93 / 240.0, 53 / 160.0, 44 / 240.0, 44 / 160.0), 2));

        Assert.Null(result.FailureCode);
        for (var y = 60; y < 90; y++)
        for (var x = 100; x < 130; x++)
            Assert.True(result.Pixels![(y * width + x) * 3] > 120);
        Assert.Equal((byte)15, result.Pixels![(70 * width + 170) * 3]);
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

    // Deterministic blotchy card texture: value noise on a 6 px lattice.
    private static byte Mottled(int x, int y)
    {
        static double Lattice(int i, int j)
        {
            var h = unchecked((uint)(i * 73856093) ^ (uint)(j * 19349663));
            h = unchecked(h * 2654435761u);
            return (h >> 8) / (double)(1 << 24);
        }
        double fx = x / 6.0, fy = y / 6.0;
        int ix = (int)fx, iy = (int)fy;
        double tx = fx - ix, ty = fy - iy;
        var top = Lattice(ix, iy) * (1 - tx) + Lattice(ix + 1, iy) * tx;
        var bottom = Lattice(ix, iy + 1) * (1 - tx) + Lattice(ix + 1, iy + 1) * tx;
        return (byte)(185 + 40 * (top * (1 - ty) + bottom * ty));
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
