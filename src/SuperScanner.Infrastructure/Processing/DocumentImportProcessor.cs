using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Uploads;
using SuperScanner.Infrastructure.Persistence;

namespace SuperScanner.Infrastructure.Processing;

public sealed class DocumentImportProcessor(
    AppDbContext db,
    IObjectStore store,
    IPdfImportTool pdfTool,
    DocumentPreviewProcessor previewProcessor,
    CropProcessor cropProcessor,
    IOptions<DocumentImportOptions> options)
{
    public async Task FailAsync(Guid uploadId, CancellationToken ct)
    {
        // Discard any partially tracked mutation left by the failed attempt before recording its terminal outcome.
        db.ChangeTracker.Clear();
        var upload = await db.UploadIntents.SingleAsync(x => x.Id == uploadId, ct);
        if (upload.State != UploadIntentState.Accepted || upload.AcceptedObjectKey is null) return;
        var pages = await db.Pages.Where(p => p.SourceUploadId == uploadId && p.RemovedAt == null).ToListAsync(ct);
        foreach (var page in pages.Where(p => p.State == PageState.Importing)) page.MarkFailed("import_failed");
        if (upload.DiscoveredPageCount < pages.Count)
        {
            upload.BeginExpansion(pages.Count);
        }
        upload.RecordExpansion(pages.Count(p => p.State != PageState.Failed),
            pages.Count(p => p.State == PageState.Failed), "import_failed");
        await db.SaveChangesAsync(ct);
        await CropDocumentStatus.RefreshAsync(db, upload.DocumentId, ct);
    }

    public async Task ProcessAsync(Guid uploadId, CancellationToken cancellationToken)
    {
        var upload = await db.UploadIntents.SingleAsync(x => x.Id == uploadId, cancellationToken);
        if (upload.State != UploadIntentState.Accepted || upload.AcceptedObjectKey is null)
        {
            throw new InvalidOperationException("Upload is not accepted.");
        }

        var document = await db.Documents.Include(x => x.Pages)
            .SingleAsync(x => x.Id == upload.DocumentId, cancellationToken);
        var directory = Directory.CreateTempSubdirectory("superscanner-import-");
        try
        {
            var sourcePath = Path.Combine(directory.FullName, "source");
            try
            {
                await DownloadAsync(upload.AcceptedObjectKey, sourcePath, options.Value.MaxUploadBytes, cancellationToken);
            }
            catch (ImportFailureException exception)
            {
                RecordRejectedExpansion(upload, exception.Code);
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            var isPdf = upload.DeclaredMediaType == "application/pdf";
            int pageCount;
            PdfInspection? inspection = null;
            try
            {
                if (isPdf)
                {
                    inspection = await InspectPdfAsync(sourcePath, cancellationToken);
                    if (inspection.IsEncrypted) throw new ImportFailureException("pdf_encrypted");
                    if (inspection.PageCount < 1) throw new ImportFailureException("pdf_invalid");
                    pageCount = inspection.PageCount;
                }
                else
                {
                    pageCount = InspectImage(sourcePath, upload.DeclaredMediaType);
                }
            }
            catch (ImportFailureException exception)
            {
                RecordRejectedExpansion(upload, exception.Code);
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            if (pageCount > options.Value.MaxPagesPerImport)
            {
                RecordRejectedExpansion(upload, "import_page_limit");
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            if (inspection is { EstimatedDecodedPixels: var pixels } &&
                (pixels < 0 || pixels > options.Value.MaxDecodedPixels))
            {
                RecordRejectedExpansion(upload, "import_pixel_limit");
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            IReadOnlyList<Page> pages;
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                upload.BeginExpansion(pageCount);
                var existingPageIds = document.Pages.Select(page => page.Id).ToHashSet();
                pages = document.AppendImportedPages(upload.Id, Enumerable.Range(1, pageCount).ToArray(),
                    options.Value.MaxPagesPerDocument, DateTimeOffset.UtcNow);
                db.Pages.AddRange(pages.Where(page => !existingPageIds.Contains(page.Id)));
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (InvalidOperationException)
            {
                RecordRejectedExpansion(upload, "document_page_limit");
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            long renderedBytes = isPdf ? await GetPersistedRenderedBytesAsync(pages, cancellationToken) : 0;
            foreach (var page in pages.Where(page => page.RemovedAt is null))
            {
                try
                {
                    if (page.OriginalObjectKey is null)
                    {
                        if (isPdf)
                        {
                            renderedBytes = await AttachRenderedPageAsync(sourcePath, directory.FullName, document, page,
                                renderedBytes, cancellationToken);
                        }
                        else
                        {
                            page.MarkImportReady(upload.AcceptedObjectKey, upload.DeclaredMediaType);
                        }
                        await db.SaveChangesAsync(cancellationToken);
                    }

                    await previewProcessor.ProcessPageAsync(page.Id, cancellationToken);
                    await cropProcessor.EnsureDetectionForPageAsync(page.Id, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    page.MarkFailed(SafePageFailureCode(exception));
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            var activePages = pages.Where(page => page.RemovedAt is null).ToArray();
            var failed = activePages.Count(page => page.State == PageState.Failed);
            upload.RecordExpansion(activePages.Length - failed, failed,
                failed == 0 ? null : activePages.First(page => page.State == PageState.Failed).FailureCode);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private async Task<PdfInspection> InspectPdfAsync(string sourcePath, CancellationToken ct)
    {
        PdfInspection inspection;
        try
        {
            inspection = await pdfTool.InspectAsync(sourcePath, ct);
        }
        catch (PdfImportException exception)
        {
            throw new ImportFailureException(exception.Code);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw new ImportFailureException("pdf_invalid");
        }

        return inspection;
    }

    private int InspectImage(string sourcePath, string mediaType)
    {
        try
        {
            var format = mediaType switch
            {
                "image/jpeg" => MagickFormat.Jpeg,
                "image/png" => MagickFormat.Png,
                "image/heic" => MagickFormat.Heic,
                _ => throw new ImportFailureException("image_invalid")
            };
            var info = new MagickImageInfo(sourcePath, new MagickReadSettings { Format = format });
            var pixels = checked((long)info.Width * info.Height);
            if (pixels < 1) throw new ImportFailureException("image_invalid");
            if (pixels > options.Value.MaxDecodedPixels) throw new ImportFailureException("import_pixel_limit");
            return 1;
        }
        catch (ImportFailureException) { throw; }
        catch (Exception)
        {
            throw new ImportFailureException("image_invalid");
        }
    }

    private async Task<long> AttachRenderedPageAsync(
        string sourcePath,
        string directory,
        Document document,
        Page page,
        long renderedBytes,
        CancellationToken ct)
    {
        var sourceKey = $"page-sources/{document.Id:N}/{page.Id:N}/source.png";
        if (await store.HeadAsync(sourceKey, ct) is null)
        {
            var renderedPath = Path.Combine(directory, $"page-{page.SourcePageIndex}.png");
            try
            {
                await pdfTool.RenderPageAsync(sourcePath, page.SourcePageIndex, renderedPath, ct);
            }
            catch (PdfImportException exception)
            {
                throw new ImportFailureException(exception.Code);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                throw new ImportFailureException("pdf_render_failed");
            }

            var info = new FileInfo(renderedPath);
            if (!info.Exists) throw new ImportFailureException("pdf_render_failed");
            if (info.Length > options.Value.MaxRenderedBytes - renderedBytes)
                throw new ImportFailureException("render_size_limit");
            await using var rendered = File.OpenRead(renderedPath);
            await store.WriteAsync(sourceKey, "image/png", rendered, ct);
            renderedBytes += info.Length;
        }
        page.MarkImportReady(sourceKey, "image/png");
        return renderedBytes;
    }

    private async Task<long> GetPersistedRenderedBytesAsync(IReadOnlyList<Page> pages, CancellationToken ct)
    {
        long total = 0;
        foreach (var page in pages)
        {
            if (page.OriginalObjectKey is null) continue;
            var source = await store.HeadAsync(page.OriginalObjectKey, ct);
            if (source is not null) total = checked(total + source.SizeBytes);
        }
        return total;
    }

    private async Task DownloadAsync(string key, string destination, long maxBytes, CancellationToken ct)
    {
        if (await store.HeadAsync(key, ct) is { SizeBytes: var size } && size > maxBytes)
            throw new ImportFailureException("import_size_limit");
        await using var source = await store.OpenReadAsync(key, ct);
        await using var target = File.Create(destination);
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += count;
            if (total > maxBytes) throw new ImportFailureException("import_size_limit");
            await target.WriteAsync(buffer.AsMemory(0, count), ct);
        }
    }

    private static string SafePageFailureCode(Exception exception) => exception switch
    {
        ImportFailureException { Code: "render_size_limit" } => "render_size_limit",
        ImportFailureException { Code: "pdf_render_failed" } => "pdf_render_failed",
        _ => "pdf_render_failed"
    };

    private static void RecordRejectedExpansion(UploadIntent upload, string code)
    {
        if (upload.DiscoveredPageCount == 0)
        {
            upload.BeginExpansion(0);
        }
        upload.RecordExpansion(upload.CreatedPageCount, upload.FailedPageCount, code);
    }

    private sealed class ImportFailureException(string code) : Exception(code)
    {
        public string Code { get; } = code;
    }
}
