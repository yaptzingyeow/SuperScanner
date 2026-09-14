using System.Diagnostics;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Uploads;
using SuperScanner.Infrastructure.ObjectStorage;
using SuperScanner.Infrastructure.Persistence;

namespace SuperScanner.Infrastructure.Processing;

public sealed class DocumentPreviewProcessor(AppDbContext db, R2ObjectStore store, IConfiguration configuration)
{
    public async Task ProcessAsync(Guid uploadId, CancellationToken cancellationToken)
    {
        var upload = await db.UploadIntents.SingleAsync(x => x.Id == uploadId, cancellationToken);
        if (upload.State != UploadIntentState.Accepted) throw new InvalidOperationException("Upload is not accepted.");
        var document = await db.Documents.Include(x => x.Pages)
            .SingleAsync(x => x.Id == upload.DocumentId, cancellationToken);
        var page = document.Pages.Single(x => x.Id == upload.PageId);
        if (page.PreviewObjectKey is null)
        {
            var directory = Directory.CreateTempSubdirectory("superscanner-preview-");
            try
            {
                var sourcePath = Path.Combine(directory.FullName, "source");
                await using (var source = await store.OpenReadAsync(page.OriginalObjectKey!, cancellationToken))
                await using (var file = File.Create(sourcePath))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int count;
                    while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        total += count;
                        if (total > 25 * 1024 * 1024) throw new InvalidDataException("Input size exceeded.");
                        await file.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    }
                }
                if (upload.DeclaredMediaType == "application/pdf")
                {
                    var prefix = Path.Combine(directory.FullName, "page");
                    var start = new ProcessStartInfo(configuration["Preview:PdfToPpmPath"] ?? "pdftoppm")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                    foreach (var argument in new[] { "-f", "1", "-l", "1", "-singlefile", "-scale-to", "2000", "-jpeg", sourcePath, prefix })
                        start.ArgumentList.Add(argument);
                    using var process = Process.Start(start) ?? throw new InvalidOperationException("PDF renderer unavailable.");
                    var errors = process.StandardError.ReadToEndAsync(cancellationToken);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(45));
                    try { await process.WaitForExitAsync(timeout.Token); }
                    catch { if (!process.HasExited) process.Kill(true); throw; }
                    await errors;
                    if (process.ExitCode != 0) throw new InvalidDataException("PDF could not be rendered.");
                    sourcePath = prefix + ".jpg";
                }
                using var image = new MagickImage();
                var settings = new MagickReadSettings { FrameIndex = 0, FrameCount = 1 };
                // Decode only the upload's validated format; never allow arbitrary delegates.
                settings.Format = upload.DeclaredMediaType switch
                {
                    "application/pdf" or "image/jpeg" => MagickFormat.Jpeg,
                    "image/png" => MagickFormat.Png,
                    "image/heic" => MagickFormat.Heic,
                    _ => throw new InvalidDataException("Unsupported format.")
                };
                image.Read(sourcePath, settings);
                image.AutoOrient();
                image.BackgroundColor = MagickColors.White;
                image.Alpha(AlphaOption.Remove);
                image.Strip();
                image.Resize(new MagickGeometry(2000, 2000) { Greater = true });
                image.Format = MagickFormat.Jpeg;
                image.Quality = 88;
                var previewKey = $"previews/{document.Id:N}/{page.Id:N}/v1.jpg";
                var thumbnailKey = $"thumbnails/{document.Id:N}/{page.Id:N}/v1.jpg";
                using var preview = new MemoryStream(image.ToByteArray());
                await store.WriteImageAsync(previewKey, preview, cancellationToken);
                image.Resize(new MagickGeometry(320, 320) { Greater = true });
                using var thumbnail = new MemoryStream(image.ToByteArray());
                await store.WriteImageAsync(thumbnailKey, thumbnail, cancellationToken);
                page.SetPreview(previewKey, thumbnailKey);
            }
            finally { directory.Delete(true); }
        }
        await db.SaveChangesAsync(cancellationToken);
        await CropDocumentStatus.RefreshAsync(db, document.Id, cancellationToken);
    }
}
