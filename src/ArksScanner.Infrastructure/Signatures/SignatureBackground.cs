using ImageMagick;

namespace ArksScanner.Infrastructure.Signatures;

public sealed class SignatureBackgroundException(string code) : Exception(code)
{
    /// <summary>signature_invalid_image, signature_too_large or signature_no_ink.</summary>
    public string Code { get; } = code;
}

/// <summary>
/// Turns a photo or scan of a signature into a transparent PNG. The paper brightness is estimated
/// per area of the picture, so grey phone photos and shadows disappear while ink that is darker
/// than its own surroundings stays. The paper's colour cast is taken out of the ink.
/// </summary>
public static class SignatureBackground
{
    public const long MaximumSourcePixels = 12_000_000;
    private const int MaximumSide = 2000;

    public static byte[] Remove(byte[] source, double strength, bool keepOriginal, bool trim = true)
    {
        MagickImage image;
        try
        {
            var info = new MagickImageInfo(source);
            if (info.Format is not (MagickFormat.Png or MagickFormat.Jpeg or MagickFormat.Jpg or MagickFormat.Pjpeg))
                throw new SignatureBackgroundException("signature_invalid_image");
            if ((long)info.Width * info.Height > MaximumSourcePixels)
                throw new SignatureBackgroundException("signature_too_large");
            image = new MagickImage(source);
        }
        catch (MagickException) { throw new SignatureBackgroundException("signature_invalid_image"); }

        using (image)
        {
            image.AutoOrient();
            if (Math.Max(image.Width, image.Height) > MaximumSide)
                image.Resize(new MagickGeometry(MaximumSide, MaximumSide));
            image.ColorSpace = ColorSpace.sRGB;
            image.Alpha(AlphaOption.Set);
            var width = (int)image.Width;
            var height = (int)image.Height;
            byte[] rgba;
            using (var pixels = image.GetPixels()) rgba = pixels.ToByteArray(PixelMapping.RGBA)!;

            if (!keepOriginal) Clean(rgba, width, height, Math.Clamp(strength, 0, 1));
            image.ImportPixels(rgba, new PixelImportSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.RGBA));
            if (!keepOriginal && trim && !TrimToInk(image, rgba, width, height))
                throw new SignatureBackgroundException("signature_no_ink");
            image.Format = MagickFormat.Png;
            return image.ToByteArray();
        }
    }

    private static void Clean(byte[] rgba, int width, int height, double strength)
    {
        var background = PaperBrightness(rgba, width, height);
        // Darkness relative to the local paper needed before a pixel starts to count as ink.
        var start = 0.06 + strength * 0.18;
        var full = start + 0.20;
        var visible = false;
        for (int y = 0, p = 0; y < height; y++)
        for (var x = 0; x < width; x++, p++)
        {
            var i = p * 4;
            if (rgba[i + 3] == 0) continue;
            var paper = Math.Max(background[p], 1);
            var lightness = Luma(rgba, i);
            var darkness = (paper - lightness) / paper;
            var opacity = Math.Clamp((darkness - start) / (full - start), 0, 1);
            rgba[i + 3] = (byte)Math.Round(rgba[i + 3] * opacity);
            if (rgba[i + 3] > 0) visible = true;
            // White-balance against the paper so ink on yellow or grey paper keeps its true colour.
            var scale = 255.0 / paper;
            for (var c = 0; c < 3; c++)
                rgba[i + c] = (byte)Math.Clamp(Math.Round(rgba[i + c] * Math.Min(scale, 2.5)), 0, 255);
            if (opacity > 0)
            {
                // Pull ink colours away from the paper: what remains is drawn on transparency.
                var target = Math.Min(1, opacity * 1.0);
                for (var c = 0; c < 3; c++)
                    rgba[i + c] = (byte)Math.Clamp(Math.Round(rgba[i + c] * (1 - 0.35 * target)), 0, 255);
            }
        }
        if (!visible) throw new SignatureBackgroundException("signature_no_ink");
    }

    /// <summary>Paper brightness for every pixel: a bright percentile per block, smoothly interpolated.</summary>
    private static double[] PaperBrightness(byte[] rgba, int width, int height)
    {
        var block = Math.Max(16, Math.Min(width, height) / 12);
        var columns = (width + block - 1) / block;
        var rows = (height + block - 1) / block;
        var cells = new double[columns * rows];
        var histogram = new int[256];
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            Array.Clear(histogram);
            var count = 0;
            for (var y = row * block; y < Math.Min(height, (row + 1) * block); y++)
            for (var x = column * block; x < Math.Min(width, (column + 1) * block); x++)
            {
                var i = (y * width + x) * 4;
                if (rgba[i + 3] == 0) continue;
                histogram[(int)Luma(rgba, i)]++;
                count++;
            }
            cells[row * columns + column] = count == 0 ? 255 : Percentile(histogram, count, 0.90);
        }

        var result = new double[width * height];
        for (var y = 0; y < height; y++)
        {
            var gy = Math.Clamp((y + 0.5) / block - 0.5, 0, rows - 1);
            int y0 = (int)gy, y1 = Math.Min(rows - 1, y0 + 1);
            var fy = gy - y0;
            for (var x = 0; x < width; x++)
            {
                var gx = Math.Clamp((x + 0.5) / block - 0.5, 0, columns - 1);
                int x0 = (int)gx, x1 = Math.Min(columns - 1, x0 + 1);
                var fx = gx - x0;
                var top = cells[y0 * columns + x0] * (1 - fx) + cells[y0 * columns + x1] * fx;
                var bottom = cells[y1 * columns + x0] * (1 - fx) + cells[y1 * columns + x1] * fx;
                result[y * width + x] = top * (1 - fy) + bottom * fy;
            }
        }
        return result;
    }

    private static double Percentile(int[] histogram, int count, double fraction)
    {
        var target = count * fraction;
        var seen = 0;
        for (var value = 0; value < 256; value++)
        {
            seen += histogram[value];
            if (seen >= target) return value;
        }
        return 255;
    }

    private static double Luma(byte[] rgba, int i) => 0.299 * rgba[i] + 0.587 * rgba[i + 1] + 0.114 * rgba[i + 2];

    private static bool TrimToInk(MagickImage image, byte[] rgba, int width, int height)
    {
        int left = width, top = height, right = -1, bottom = -1;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (rgba[(y * width + x) * 4 + 3] <= 16) continue;
            left = Math.Min(left, x); right = Math.Max(right, x);
            top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        if (right < 0) return false;
        var margin = Math.Max(4, (int)(Math.Max(right - left, bottom - top) * 0.04));
        left = Math.Max(0, left - margin); top = Math.Max(0, top - margin);
        right = Math.Min(width - 1, right + margin); bottom = Math.Min(height - 1, bottom + margin);
        image.Crop(new MagickGeometry(left, top, (uint)(right - left + 1), (uint)(bottom - top + 1)));
        image.ResetPage();
        return true;
    }
}
