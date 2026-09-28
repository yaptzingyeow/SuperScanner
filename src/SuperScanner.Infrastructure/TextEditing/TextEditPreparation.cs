using System.Security.Cryptography;
using ImageMagick;
using ImageMagick.Drawing;
using SuperScanner.Application.Abstractions;
using SuperScanner.Application.TextEditing;
using SuperScanner.Domain.TextEditing;

namespace SuperScanner.Infrastructure.TextEditing;

public sealed class TextEditPreparation(
    IObjectStore store,
    IFontCatalogue fonts,
    string fontRoot,
    TextEditingOptions options) : ITextEditPreparation
{
    public async Task<PreparedTextEdit> PrepareAsync(string sourceKey, string replacement,
        NormalizedBox box, TextEditStyle style, CancellationToken ct)
    {
        var font = fonts.Get(style.FontId, style.FontVersion);
        if (!font.SupportsWeight(style.Weight)) return new PreparedTextEdit(string.Empty, false);
        var fontPath = Path.GetFullPath(Path.Combine(fontRoot, font.RendererAssetPath));
        var expectedRoot = Path.GetFullPath(Path.Combine(fontRoot, "assets/fonts")) +
            Path.DirectorySeparatorChar;
        if (!fontPath.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Font path is outside the pinned catalogue.");

        await using var source = await store.OpenReadAsync(sourceKey, ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > 25 * 1024 * 1024)
                throw new InvalidDataException("Text-edit source exceeds the input limit.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using var image = new MagickImage(bytes);
        if (image.Width is 0 or > 20_000 || image.Height is 0 or > 20_000 ||
            (long)image.Width * image.Height > 40_000_000)
            throw new InvalidDataException("Text-edit source exceeds the pixel limit.");
        if (string.IsNullOrWhiteSpace(replacement)) return new PreparedTextEdit(hash, true);
        var fit = MagickTextLayout.Fit(replacement, box, (int)image.Width,
            (int)image.Height, style, fontPath, options);
        return new PreparedTextEdit(hash, fit.Fits);
    }
}
