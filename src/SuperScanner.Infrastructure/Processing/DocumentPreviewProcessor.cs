using ImageMagick;
using Microsoft.EntityFrameworkCore;
using SuperScanner.Application.Abstractions;
using SuperScanner.Infrastructure.Persistence;

namespace SuperScanner.Infrastructure.Processing;

public sealed class DocumentPreviewProcessor(AppDbContext db, IObjectStore store)
{
    public async Task ProcessPageAsync(Guid pageId, CancellationToken cancellationToken)
    {
        var page = await db.Pages.SingleAsync(x => x.Id == pageId, cancellationToken);
        if (page.OriginalObjectKey is null) throw new InvalidOperationException("Page source is not available.");
        if (page.PreviewObjectKey is not null) return;

        var directory = Directory.CreateTempSubdirectory("superscanner-preview-");
        try
        {
            var sourcePath = Path.Combine(directory.FullName, "source");
            await using (var source = await store.OpenReadAsync(page.OriginalObjectKey, cancellationToken))
            await using (var file = File.Create(sourcePath))
            {
                await source.CopyToAsync(file, cancellationToken);
            }

            using var image = new MagickImage();
            image.Read(sourcePath, new MagickReadSettings { Format = GetFormat(page.OriginalMediaType), FrameIndex = 0, FrameCount = 1 });
            image.AutoOrient();
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaOption.Remove);
            image.Strip();
            image.Resize(new MagickGeometry(2000, 2000) { Greater = true });
            image.Format = MagickFormat.Jpeg;
            image.Quality = 88;
            var previewKey = $"previews/{page.DocumentId:N}/{page.Id:N}/v1.jpg";
            var thumbnailKey = $"thumbnails/{page.DocumentId:N}/{page.Id:N}/v1.jpg";
            using var preview = new MemoryStream(image.ToByteArray());
            await store.WriteAsync(previewKey, "image/jpeg", preview, cancellationToken);
            image.Resize(new MagickGeometry(320, 320) { Greater = true });
            using var thumbnail = new MemoryStream(image.ToByteArray());
            await store.WriteAsync(thumbnailKey, "image/jpeg", thumbnail, cancellationToken);
            page.SetPreview(previewKey, thumbnailKey);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static MagickFormat GetFormat(string mediaType) => mediaType switch
    {
        "image/jpeg" => MagickFormat.Jpeg,
        "image/png" => MagickFormat.Png,
        "image/heic" => MagickFormat.Heic,
        _ => throw new InvalidDataException("Unsupported source media type.")
    };
}
