using ImageMagick;
using ArksScanner.Infrastructure.Signatures;

namespace ArksScanner.Application.Tests.Signatures;

public sealed class SignatureBackgroundTests
{
    /// <summary>A JPEG-like photo: paper brightness per column (slightly warm) with a dark ink stroke.</summary>
    private static byte[] Photo(int width, int height, Func<int, int> paper, int inkFrom, int inkTo, int inkY, int ink = 30)
    {
        var pixels = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 3;
            var inStroke = x >= inkFrom && x < inkTo && Math.Abs(y - inkY) <= 2;
            var v = inStroke ? ink : paper(x);
            pixels[i] = (byte)v; pixels[i + 1] = (byte)v; pixels[i + 2] = (byte)Math.Clamp(inStroke ? v + 40 : v - 12, 0, 255);
        }
        using var image = new MagickImage(MagickColors.White, (uint)width, (uint)height);
        image.ImportPixels(pixels, new PixelImportSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.RGB));
        return image.ToByteArray(MagickFormat.Png);
    }

    private static (MagickImage Image, IPixelCollection<byte> Pixels) Decode(byte[] png)
    {
        var image = new MagickImage(png);
        return (image, image.GetPixels());
    }

    private static byte Alpha(IPixelCollection<byte> pixels, int x, int y) => pixels.GetPixel(x, y).ToColor()!.A;

    [Fact]
    public void White_paper_becomes_transparent_and_ink_stays_opaque()
    {
        var (image, pixels) = Decode(SignatureBackground.Remove(Photo(96, 64, _ => 255, 10, 80, 30), .5, false, trim: false));
        using (image)
        {
            Assert.True(image.HasAlpha);
            Assert.Equal(0, Alpha(pixels, 5, 5));
            Assert.Equal(255, Alpha(pixels, 40, 30));
        }
    }

    [Fact]
    public void Grey_phone_photo_paper_becomes_fully_transparent()
    {
        var (image, pixels) = Decode(SignatureBackground.Remove(Photo(96, 64, _ => 185, 10, 80, 30), .5, false, trim: false));
        using (image)
        {
            Assert.Equal(0, Alpha(pixels, 5, 5));
            Assert.Equal(0, Alpha(pixels, 90, 60));
            Assert.Equal(255, Alpha(pixels, 40, 30));
        }
    }

    [Fact]
    public void Paper_under_a_shadow_is_removed_and_ink_in_the_shadow_kept()
    {
        const int width = 160;
        var (image, pixels) = Decode(SignatureBackground.Remove(
            Photo(width, 64, x => (int)Math.Round(235 - x * 95.0 / width), 110, 150, 30), .5, false, trim: false));
        using (image)
        {
            foreach (var x in new[] { 2, 50, 100, 155 }) Assert.Equal(0, Alpha(pixels, x, 8));
            Assert.Equal(255, Alpha(pixels, 130, 30));
        }
    }

    [Fact]
    public void Paper_colour_cast_is_cleaned_out_of_the_ink()
    {
        var (image, pixels) = Decode(SignatureBackground.Remove(Photo(96, 64, _ => 185, 10, 80, 30, ink: 60), .5, false, trim: false));
        using (image) Assert.True(pixels.GetPixel(40, 30).ToColor()!.R < 90);
    }

    [Fact]
    public void Higher_strength_removes_faint_marks_first()
    {
        var faint = Photo(96, 64, _ => 230, 10, 80, 30, ink: 150);
        var (strong, strongPixels) = Decode(SignatureBackground.Remove(faint, .9, false, trim: false));
        var (gentle, gentlePixels) = Decode(SignatureBackground.Remove(faint, .1, false, trim: false));
        using (strong) using (gentle)
            Assert.True(Alpha(strongPixels, 40, 30) < Alpha(gentlePixels, 40, 30));
    }

    [Fact]
    public void Keep_original_returns_the_picture_unchanged_and_opaque()
    {
        var (image, pixels) = Decode(SignatureBackground.Remove(Photo(96, 64, _ => 185, 10, 80, 30), .5, true, trim: false));
        using (image) Assert.Equal(255, Alpha(pixels, 5, 5));
    }

    [Fact]
    public void Result_is_trimmed_to_the_ink_with_a_small_margin()
    {
        using var image = new MagickImage(SignatureBackground.Remove(Photo(400, 300, _ => 200, 100, 300, 150), .5, false));
        Assert.InRange((int)image.Width, 200, 240);
        Assert.InRange((int)image.Height, 5, 40);
    }

    [Fact]
    public void A_blank_page_has_no_signature()
    {
        Assert.Throws<SignatureBackgroundException>(() =>
            SignatureBackground.Remove(Photo(96, 64, _ => 200, 0, 0, 30), .5, false));
    }

    [Fact]
    public void Files_that_are_not_images_are_rejected()
    {
        Assert.Throws<SignatureBackgroundException>(() =>
            SignatureBackground.Remove("<svg/>"u8.ToArray(), .5, false));
    }
}
