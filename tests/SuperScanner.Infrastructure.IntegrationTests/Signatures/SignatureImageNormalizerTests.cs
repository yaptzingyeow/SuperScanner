using ImageMagick;
using SuperScanner.Infrastructure.Signatures;

namespace SuperScanner.Infrastructure.IntegrationTests.Signatures;

public sealed class SignatureImageNormalizerTests
{
    [Fact]
    public async Task Grayscale_alpha_with_visible_ink_is_accepted_and_metadata_removed()
    {
        using var image = new MagickImage(MagickColors.Black, 20, 10);
        image.ColorType = ColorType.GrayscaleAlpha;
        image.SetAttribute("comment", "private source metadata");
        var result = await new SignatureImageNormalizer().NormalizeAsync(new MemoryStream(image.ToByteArray(MagickFormat.Png)), default);
        using var decoded = new MagickImage(result.Bytes);
        Assert.Null(decoded.GetAttribute("comment"));
        Assert.Equal(20, result.Width);
    }

    [Fact]
    public async Task Rejects_excessive_decoded_pixels()
    {
        using var image = new MagickImage(MagickColors.Black, 4000, 3001);
        await Assert.ThrowsAsync<InvalidDataException>(() => new SignatureImageNormalizer()
            .NormalizeAsync(new MemoryStream(image.ToByteArray(MagickFormat.Png)), default));
    }
    [Fact]
    public async Task Valid_png_is_reencoded_with_dimensions()
    {
        using var image = new MagickImage(MagickColors.Black, 20, 10);
        image.Format = MagickFormat.Png;
        using var input = new MemoryStream(image.ToByteArray());
        var result = await new SignatureImageNormalizer().NormalizeAsync(input, default);
        Assert.Equal(20, result.Width);
        Assert.Equal(10, result.Height);
        Assert.Equal(2, result.AspectRatio);
        using var decoded = new MagickImage(result.Bytes);
        Assert.Equal(MagickFormat.Png, decoded.Format);
    }

    [Fact]
    public async Task Rejects_jpeg_even_if_decodable()
    {
        using var image = new MagickImage(MagickColors.Black, 20, 10);
        image.Format = MagickFormat.Jpeg;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new SignatureImageNormalizer().NormalizeAsync(new MemoryStream(image.ToByteArray()), default));
    }

    [Fact]
    public async Task Rejects_entirely_transparent_image()
    {
        using var image = new MagickImage(MagickColors.Transparent, 20, 10);
        image.Format = MagickFormat.Png;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new SignatureImageNormalizer().NormalizeAsync(new MemoryStream(image.ToByteArray()), default));
    }

    [Fact]
    public async Task Rejects_oversized_input() => await Assert.ThrowsAsync<InvalidDataException>(() =>
        new SignatureImageNormalizer().NormalizeAsync(new MemoryStream(new byte[5 * 1024 * 1024 + 1]), default));

    [Fact]
    public async Task Rejects_invalid_image() => await Assert.ThrowsAsync<InvalidDataException>(() =>
        new SignatureImageNormalizer().NormalizeAsync(new MemoryStream([1, 2, 3]), default));
}
