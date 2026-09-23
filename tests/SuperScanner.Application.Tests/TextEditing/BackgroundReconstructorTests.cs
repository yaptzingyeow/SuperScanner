using SuperScanner.Domain.Ocr;
using SuperScanner.Domain.TextEditing;
using SuperScanner.Infrastructure.TextEditing;

namespace SuperScanner.Application.Tests.TextEditing;

public sealed class BackgroundReconstructorTests
{
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
