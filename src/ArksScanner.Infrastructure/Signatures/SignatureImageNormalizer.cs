using ImageMagick;

namespace ArksScanner.Infrastructure.Signatures;

public sealed record NormalizedSignatureImage(byte[] Bytes, int Width, int Height)
{
    public double AspectRatio => (double)Width / Height;
}

public sealed class SignatureImageNormalizer
{
    private const int MaximumBytes = 5 * 1024 * 1024;
    private const long MaximumPixels = 12_000_000;
    private static readonly byte[] PngHeader = [137, 80, 78, 71, 13, 10, 26, 10];

    public async Task<NormalizedSignatureImage> NormalizeAsync(Stream input, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(chunk, ct)) != 0)
        {
            if (buffer.Length + count > MaximumBytes) throw new InvalidDataException("Signature image is too large.");
            await buffer.WriteAsync(chunk.AsMemory(0, count), ct);
        }
        var bytes = buffer.ToArray();
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(PngHeader))
            throw new InvalidDataException("A valid PNG signature is required.");
        try
        {
            var info = new MagickImageInfo(bytes);
            if (info.Width == 0 || info.Height == 0 || (long)info.Width * info.Height > MaximumPixels)
                throw new InvalidDataException("Signature dimensions exceed the limit.");
            ct.ThrowIfCancellationRequested();
            using var image = new MagickImage(bytes);
            if (image.Format != MagickFormat.Png) throw new InvalidDataException("Only PNG signatures are accepted.");
            using (var pixels = image.GetPixels())
            {
                var hasVisiblePixel = false;
                for (var y = 0; y < image.Height && !hasVisiblePixel; y++)
                {
                    ct.ThrowIfCancellationRequested();
                    for (var x = 0; x < image.Width; x++)
                    {
                        var pixel = pixels.GetPixel(x, y);
                        if (!image.HasAlpha || pixel.GetChannel(3) > 0) { hasVisiblePixel = true; break; }
                    }
                }
                if (!hasVisiblePixel) throw new InvalidDataException("Signature contains no visible ink.");
            }
            image.Strip();
            image.Format = MagickFormat.Png;
            var output = image.ToByteArray();
            if (output.Length > MaximumBytes) throw new InvalidDataException("Normalized signature is too large.");
            return new(output, checked((int)image.Width), checked((int)image.Height));
        }
        catch (MagickException exception) { throw new InvalidDataException("Signature could not be decoded.", exception); }
    }
}
