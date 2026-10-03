using System.Net;
using System.Text.Json;
using Amazon.S3;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using ArksScanner.Application.Abstractions;
using ArksScanner.Application.Ocr;
using ArksScanner.Domain.Documents;
using ArksScanner.Domain.Ocr;
using ArksScanner.Infrastructure.Persistence;

namespace ArksScanner.Infrastructure.Processing;

public sealed class DocumentPdfLimits
{
    public long MaxSourceBytes { get; init; } = 25 * 1024 * 1024;
    public long MaxTotalSourceBytes { get; init; } = 250 * 1024 * 1024;
    public long MaxPagePixels { get; init; } = 40_000_000;
    public long MaxTotalPixels { get; init; } = 200_000_000;
    public long MaxPdfBytes { get; init; } = 250 * 1024 * 1024;
    public int MaxPages { get; init; } = 50;
}

public sealed class DocumentPdfBuilder(
    AppDbContext db,
    IObjectStore store,
    IClock clock,
    DocumentPdfLimits? limits = null,
    IPdfTextLayerWriter? textLayerWriter = null,
    PdfExportOptions? pdfExportOptions = null)
{
    private readonly DocumentPdfLimits limits = limits ?? new();
    private readonly PdfExportOptions pdfExportOptions = pdfExportOptions ?? new();
    private readonly IPdfTextLayerWriter textLayerWriter = textLayerWriter ?? new PdfSharpTextLayerWriter(
        Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "NotoSans-Regular.ttf"));

    public async Task FailAsync(Guid exportId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var export = await FindForUpdateAsync(exportId, ct);
        if (export?.State is DocumentExportState.Queued or DocumentExportState.Processing)
        {
            export.Fail("export_build_failed", clock.UtcNow);
            await db.SaveChangesAsync(ct);
        }
        // A previous commit may have succeeded before its acknowledgement was lost. Preserve
        // Ready (and terminal Failed) when reconciling the final infrastructure failure.
        await transaction.CommitAsync(ct);
    }

    public async Task BuildAsync(Guid exportId, CancellationToken ct)
    {
        // The export row lock serializes overlapping/reclaimed job leases. Reload after acquiring
        // it so a completed attempt can never be replaced by a stale tracked instance.
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var export = await FindForUpdateAsync(exportId, ct);
        if (export is null || export.State is DocumentExportState.Ready or DocumentExportState.Failed) return;
        if (export.State == DocumentExportState.Queued) export.Start(clock.UtcNow);
        try
        {
            var snapshot = JsonSerializer.Deserialize<DocumentExportSnapshotEntry[]>(export.SnapshotJson)
                ?? throw new BuildFailure("export_build_failed");
            if (snapshot.Length < 1 || snapshot.Length != export.ReadyPageCount)
                throw new BuildFailure("export_build_failed");
            if (snapshot.Length > limits.MaxPages) throw new BuildFailure("export_size_limit");
            var ocrById = this.pdfExportOptions.SearchableTextEnabled
                ? await LoadSnapshottedOcrAsync(snapshot, ct)
                : new Dictionary<Guid, PageOcrResult>();
            var key = $"exports/{export.DocumentId}/{export.Id}/document.pdf";
            var existingSearchablePageCount = await GetCompleteOutputSearchablePageCountAsync(key, snapshot.Length, ct);
            var buildResult = existingSearchablePageCount.HasValue
                ? new DocumentPdfBuildResult(existingSearchablePageCount.Value, 0)
                : await BuildAndStoreAsync(snapshot, ocrById, key, ct);
            ct.ThrowIfCancellationRequested();
            export.Complete(key, clock.UtcNow, buildResult.SearchablePageCount);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            export.Fail(exception is BuildFailure failure ? failure.Code : "export_build_failed", clock.UtcNow);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<DocumentExport?> FindForUpdateAsync(Guid exportId, CancellationToken ct) => db.Database.IsNpgsql()
        ? (await db.DocumentExports.FromSqlInterpolated(
            $"SELECT * FROM document_exports WHERE \"Id\" = {exportId} FOR UPDATE").ToListAsync(ct)).SingleOrDefault()
        : await db.DocumentExports.SingleOrDefaultAsync(x => x.Id == exportId, ct);

    private async Task<DocumentPdfBuildResult> BuildAndStoreAsync(
        DocumentExportSnapshotEntry[] snapshot,
        IReadOnlyDictionary<Guid, PageOcrResult> ocrById,
        string key,
        CancellationToken ct)
    {
        using var pdf = new PdfDocument();
        long sourceBytes = 0, pixels = 0;
        var searchablePageCount = 0;
        var skippedWordCount = 0;
        foreach (var entry in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            using var source = await ReadImageAsync(entry.ProcessedObjectKey,
                Math.Min(limits.MaxSourceBytes, limits.MaxTotalSourceBytes - sourceBytes), ct);
            sourceBytes += source.Length;
            PdfPage page;
            PdfPagePlacement placement;
            try
            {
                var info = new MagickImageInfo(source);
                if (info.Format is not (MagickFormat.Jpeg or MagickFormat.Png) || info.Width == 0 || info.Height == 0)
                    throw new BuildFailure("export_decode_failed");
                var pagePixels = checked((long)info.Width * info.Height);
                if (pagePixels > limits.MaxPagePixels || pagePixels > limits.MaxTotalPixels - pixels)
                    throw new BuildFailure("export_size_limit");
                pixels += pagePixels;
                source.Position = 0;
                // Text edits are lossless PNGs, including grayscale/indexed/16-bit variants
                // that PDFsharp cannot read directly. Normalize their encoding, not their pixels.
                using var normalized = new MemoryStream();
                if (info.Format == MagickFormat.Png)
                {
                    using var decoded = new MagickImage(source);
                    decoded.Depth = 8;
                    normalized.Write(decoded.ToByteArray(MagickFormat.Png32));
                    normalized.Position = 0;
                }
                using var image = XImage.FromStream(info.Format == MagickFormat.Png ? normalized : source);
                page = pdf.AddPage();
                placement = PdfPagePlacement.Create(image.PixelWidth, image.PixelHeight, entry.PageLayout);
                page.Width = XUnit.FromPoint(placement.PageWidth);
                page.Height = XUnit.FromPoint(placement.PageHeight);
                using (var graphics = XGraphics.FromPdfPage(page))
                    graphics.DrawImage(image, placement.ContentX, placement.ContentY,
                        placement.ContentWidth, placement.ContentHeight);
            }
            catch (BuildFailure) { throw; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { throw new BuildFailure("export_decode_failed"); }

            foreach (var signature in entry.Signatures)
            {
                using var ink = await ReadImageAsync(signature.AssetKey,
                    Math.Min(5 * 1024 * 1024, limits.MaxTotalSourceBytes - sourceBytes), ct);
                sourceBytes += ink.Length;
                try
                {
                    var info = new MagickImageInfo(ink);
                    var inkPixels = checked((long)info.Width * info.Height);
                    if (info.Format != MagickFormat.Png || inkPixels <= 0)
                        throw new BuildFailure("export_decode_failed");
                    if (inkPixels > 12_000_000 || inkPixels > limits.MaxTotalPixels - pixels)
                        throw new BuildFailure("export_size_limit");
                    pixels += inkPixels;
                    var box = signature.Box ?? throw new BuildFailure("export_decode_failed");
                    _ = new SignatureBox(box.X, box.Y, box.Width, box.Height);
                    ink.Position = 0;
                    using var image = XImage.FromStream(ink);
                    using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                    graphics.DrawImage(image,
                        placement.ContentX + box.X * placement.ContentWidth,
                        placement.ContentY + box.Y * placement.ContentHeight,
                        box.Width * placement.ContentWidth, box.Height * placement.ContentHeight);
                }
                catch (BuildFailure) { throw; }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception) { throw new BuildFailure("export_decode_failed"); }
            }

            if (entry.Marks.Count > 0)
            {
                try
                {
                    using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                    graphics.TranslateTransform(placement.ContentX, placement.ContentY);
                    foreach (var mark in entry.Marks)
                        PdfMarkRenderer.Draw(graphics, mark, placement.ContentWidth, placement.ContentHeight);
                }
                catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
                { throw new BuildFailure("export_decode_failed"); }
            }

            if (entry.Watermark is { } userWatermark)
            {
                try
                {
                    PdfUserWatermark.Draw(page, userWatermark, Path.Combine(AppContext.BaseDirectory, "assets", "fonts"));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                { throw new BuildFailure("export_build_failed"); }
            }

            if (pdfExportOptions.BrandWatermark && entry.BrandStamp)
            {
                try
                {
                    PdfBrandWatermark.Draw(page,
                        Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "NotoSans-Bold.ttf"));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                { throw new BuildFailure("export_build_failed"); }
            }

            if (FindEligibleOcr(entry, ocrById) is { } ocr)
            {
                var sourceWords = ocr.Elements.Count(element => element.Kind == OcrElementKind.Word);
                var words = PdfTextLayerProjector.Project(
                    ocr.Elements,
                    placement.ContentWidth,
                    placement.ContentHeight,
                    pdfExportOptions.ToLimits())
                    .Select(word => word with {
                        X = word.X + placement.ContentX,
                        Y = word.Y + placement.PageHeight - placement.ContentY - placement.ContentHeight
                    }).ToArray();
                skippedWordCount += sourceWords - words.Length;
                if (words.Length > 0)
                {
                    try
                    {
                        textLayerWriter.Write(page, words);
                        searchablePageCount++;
                    }
                    catch (PdfTextLayerWriteException)
                    {
                        throw new BuildFailure("export_build_failed");
                    }
                }
            }
        }

        // Stage locally with a hard write bound. Object storage receives only a complete PDF,
        // in one private PUT; no intermediate object can be exposed as a Ready export.
        await using var output = new BoundedPdfStream(limits.MaxPdfBytes, ct);
        pdf.Save(output, closeStream: false);
        ct.ThrowIfCancellationRequested();
        output.Position = 0;
        var created = await store.WriteIfAbsentAsync(key, "application/pdf", output, ct);
        if (created == ObjectCreationResult.AlreadyExists &&
            !((await GetCompleteOutputSearchablePageCountAsync(key, snapshot.Length, ct)).HasValue))
            throw new BuildFailure("export_build_failed");
        return new DocumentPdfBuildResult(searchablePageCount, skippedWordCount);
    }

    private async Task<IReadOnlyDictionary<Guid, PageOcrResult>> LoadSnapshottedOcrAsync(
        IReadOnlyCollection<DocumentExportSnapshotEntry> snapshot,
        CancellationToken ct)
    {
        var ids = snapshot
            .Where(entry => entry.OcrResultId.HasValue)
            .Select(entry => entry.OcrResultId!.Value)
            .Distinct()
            .Take(limits.MaxPages)
            .ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, PageOcrResult>();

        return await db.PageOcrResults
            .AsNoTracking()
            .Include(result => result.Elements)
            .Where(result => ids.Contains(result.Id))
            .ToDictionaryAsync(result => result.Id, ct);
    }

    private static PageOcrResult? FindEligibleOcr(
        DocumentExportSnapshotEntry entry,
        IReadOnlyDictionary<Guid, PageOcrResult> ocrById)
    {
        if (entry.OcrResultId is not Guid id ||
            entry.OcrSourceObjectKey is null ||
            entry.OcrSourceFingerprint is null ||
            !string.Equals(entry.OcrSourceObjectKey, entry.ProcessedObjectKey, StringComparison.Ordinal) ||
            !string.Equals(
                entry.OcrSourceFingerprint,
                OcrSourceFingerprint.Create(entry.ProcessedObjectKey),
                StringComparison.Ordinal) ||
            !ocrById.TryGetValue(id, out var result) ||
            result.State != OcrResultState.Ready ||
            result.PageId != entry.PageId ||
            !string.Equals(result.SourceObjectKey, entry.OcrSourceObjectKey, StringComparison.Ordinal) ||
            !string.Equals(result.SourceFingerprint, entry.OcrSourceFingerprint, StringComparison.Ordinal))
        {
            return null;
        }

        return result;
    }

    private async Task<int?> GetCompleteOutputSearchablePageCountAsync(string key, int pageCount, CancellationToken ct)
    {
        // A successful PUT may outlive a canceled/failed database commit. Never overwrite that
        // immutable key. This HEAD is a recovery optimization, not publication fencing:
        // WriteIfAbsentAsync enforces that atomically even if a remote PUT outlives our lock.
        // Validate before making the existing output visible, or fail closed if it is corrupt.
        var info = await store.HeadAsync(key, ct);
        if (info is null) return null;
        if (info.SizeBytes > limits.MaxPdfBytes) throw new BuildFailure("export_size_limit");
        await using var output = new BoundedPdfStream(limits.MaxPdfBytes, ct);
        await using var input = await store.OpenReadAsync(key, ct);
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0) output.Write(buffer, 0, count);
        output.Position = 0;
        using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        if (pdf.PageCount != pageCount) throw new BuildFailure("export_build_failed");
        return pdf.Pages.Cast<PdfPage>().Count(page =>
            page.Resources?.Elements.GetDictionary("/Font") is not null);
    }

    private async Task<MemoryStream> ReadImageAsync(string key, long maxBytes, CancellationToken ct)
    {
        var image = new MemoryStream();
        try
        {
            var info = await store.HeadAsync(key, ct) ?? throw new BuildFailure("export_asset_missing");
            if (info.SizeBytes > maxBytes) throw new BuildFailure("export_size_limit");
            await using var input = await store.OpenReadAsync(key, ct);
            var buffer = new byte[81920];
            int count;
            while ((count = await input.ReadAsync(buffer, ct)) > 0)
            {
                if (count > maxBytes - image.Length) throw new BuildFailure("export_size_limit");
                await image.WriteAsync(buffer.AsMemory(0, count), ct);
            }
            image.Position = 0;
            return image;
        }
        catch (Exception exception)
        {
            image.Dispose();
            if (exception is FileNotFoundException || exception is AmazonS3Exception { StatusCode: HttpStatusCode.NotFound })
                throw new BuildFailure("export_asset_missing");
            throw;
        }
    }

    private sealed class BuildFailure(string code) : Exception(code) { public string Code { get; } = code; }

    private sealed record DocumentPdfBuildResult(int SearchablePageCount, int SkippedWordCount);

    private sealed class BoundedPdfStream(long maxBytes, CancellationToken ct) : FileStream(
        Path.Combine(Path.GetTempPath(), $"arksscanner-export-{Guid.NewGuid():N}.tmp"),
        FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose)
    {
        private void Check(long end)
        {
            ct.ThrowIfCancellationRequested();
            if (end < 0 || end > maxBytes) throw new BuildFailure("export_size_limit");
        }
        public override void Write(byte[] buffer, int offset, int count) { Check(Position + count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(Position + buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Check(Position + 1); base.WriteByte(value); }
        public override void SetLength(long value) { Check(value); base.SetLength(value); }
    }
}
