using ImageMagick;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.TextEditing;
using TextAlignment = SuperScanner.Domain.TextEditing.TextAlignment;
using SuperScanner.Infrastructure.TextEditing;

namespace SuperScanner.Application.Tests.TextEditing;

public sealed class TextEditPreparationTests
{
    [Fact]
    public async Task Source_hash_is_actual_image_bytes_and_short_text_fits()
    {
        using var image = new MagickImage(MagickColors.White, 600, 800);
        var bytes = image.ToByteArray(MagickFormat.Png);
        var preparation = Create(bytes);
        var result = await preparation.PrepareAsync("private/source.png", "Tan BB",
            new NormalizedBox(.1, .1, .8, .1), Style(), default);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),
            result.SourceSha256Hex);
        Assert.True(result.Fits);
    }

    [Fact]
    public async Task Narrow_box_reports_overflow()
    {
        using var image = new MagickImage(MagickColors.White, 600, 800);
        var preparation = Create(image.ToByteArray(MagickFormat.Png));
        var result = await preparation.PrepareAsync("private/source.png", "A much longer replacement",
            new NormalizedBox(.1, .1, .01, .1), Style(), default);
        Assert.False(result.Fits);
    }

    [Fact]
    public async Task Fit_respects_configured_minimum_font_scale()
    {
        using var image = new MagickImage(MagickColors.White, 600, 800);
        var bytes = image.ToByteArray(MagickFormat.Png);
        var permissive = Create(bytes, new TextEditingOptions { MinimumFontScale = .2 });
        var strict = Create(bytes, new TextEditingOptions { MinimumFontScale = 1 });
        var box = new NormalizedBox(.1, .1, .12, .1);
        var permissiveResult = await permissive.PrepareAsync("private/source.png", "Tan BB", box, Style(), default);
        var strictResult = await strict.PrepareAsync("private/source.png", "Tan BB", box, Style(), default);
        Assert.True(permissiveResult.Fits);
        Assert.False(strictResult.Fits);
    }

    [Fact]
    public async Task Unknown_font_is_rejected()
    {
        using var image = new MagickImage(MagickColors.White, 600, 800);
        var preparation = Create(image.ToByteArray(MagickFormat.Png));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => preparation.PrepareAsync(
            "private/source.png", "Tan BB", new NormalizedBox(.1, .1, .8, .1),
            new TextEditStyle("unknown-font", "v1", .04, 400, "#000000", 0, .2, 0,
                TextAlignment.Left), default));
    }

    [Fact]
    public async Task Weight_that_disagrees_with_the_pinned_font_face_is_rejected()
    {
        using var image = new MagickImage(MagickColors.White, 600, 800);
        var preparation = Create(image.ToByteArray(MagickFormat.Png));
        var result = await preparation.PrepareAsync("private/source.png", "Tan BB",
            new NormalizedBox(.1, .1, .8, .1),
            new TextEditStyle("noto-sans", "archive-main-regular", .04,
                700, "#000000", 0, .2, 0, TextAlignment.Left), default);
        Assert.False(result.Fits);
    }

    private static TextEditPreparation Create(byte[] bytes, TextEditingOptions? options = null)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SuperScanner.slnx")))
            directory = directory.Parent;
        var root = directory?.FullName ?? throw new DirectoryNotFoundException();
        return new TextEditPreparation(new Store(bytes),
            BundledFontCatalogue.Load(Path.Combine(root, "assets/fonts/manifest.json")), root,
            options ?? new TextEditingOptions());
    }

    private static TextEditStyle Style() => new("noto-sans", "archive-main-regular", .04,
        400, "#000000", 0, .2, 0, TextAlignment.Left);

    private sealed class Store(byte[] bytes) : IObjectStore
    {
        public Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<StoredObjectInfo?> HeadAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(bytes));
        public Task PromoteAsync(string from, string to, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task<ObjectCreationResult> WriteIfAbsentAsync(string key, string type, Stream content, CancellationToken ct) => throw new NotSupportedException();
    }
}
