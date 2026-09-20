using System.Net;
using System.Text.Json;
using Amazon.S3;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;
using SuperScanner.Infrastructure.Persistence;

namespace SuperScanner.Infrastructure.Processing;

public sealed class DocumentPdfLimits
{
    public long MaxSourceBytes { get; init; } = 25 * 1024 * 1024;
    public long MaxTotalSourceBytes { get; init; } = 250 * 1024 * 1024;
    public long MaxPagePixels { get; init; } = 40_000_000;
    public long MaxTotalPixels { get; init; } = 200_000_000;
    public long MaxPdfBytes { get; init; } = 250 * 1024 * 1024;
    public int MaxPages { get; init; } = 50;
}

public sealed class DocumentPdfBuilder(AppDbContext db, IObjectStore store, IClock clock, DocumentPdfLimits? limits = null)
{
    private readonly DocumentPdfLimits limits = limits ?? new();

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
            var key = $"exports/{export.DocumentId}/{export.Id}/document.pdf";
            if (!await HasCompleteOutputAsync(key, snapshot.Length, ct))
                await BuildAndStoreAsync(snapshot, key, ct);
            ct.ThrowIfCancellationRequested();
            export.Complete(key, clock.UtcNow);
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

    private async Task BuildAndStoreAsync(DocumentExportSnapshotEntry[] snapshot, string key, CancellationToken ct)
    {
        using var pdf = new PdfDocument();
        long sourceBytes = 0, pixels = 0;
        foreach (var entry in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            using var source = await ReadImageAsync(entry.ProcessedObjectKey,
                Math.Min(limits.MaxSourceBytes, limits.MaxTotalSourceBytes - sourceBytes), ct);
            sourceBytes += source.Length;
            try
            {
                var info = new MagickImageInfo(source);
                if (info.Format != MagickFormat.Jpeg || info.Width == 0 || info.Height == 0)
                    throw new BuildFailure("export_decode_failed");
                var pagePixels = checked((long)info.Width * info.Height);
                if (pagePixels > limits.MaxPagePixels || pagePixels > limits.MaxTotalPixels - pixels)
                    throw new BuildFailure("export_size_limit");
                pixels += pagePixels;
                source.Position = 0;
                using var image = XImage.FromStream(source);
                var page = pdf.AddPage();
                page.Width = XUnit.FromPoint(image.PixelWidth * 72d / 96);
                page.Height = XUnit.FromPoint(image.PixelHeight * 72d / 96);
                using var graphics = XGraphics.FromPdfPage(page);
                graphics.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
            }
            catch (BuildFailure) { throw; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { throw new BuildFailure("export_decode_failed"); }
        }

        // Stage locally with a hard write bound. Object storage receives only a complete PDF,
        // in one private PUT; no intermediate object can be exposed as a Ready export.
        await using var output = new BoundedPdfStream(limits.MaxPdfBytes, ct);
        pdf.Save(output, closeStream: false);
        ct.ThrowIfCancellationRequested();
        output.Position = 0;
        var created = await store.WriteIfAbsentAsync(key, "application/pdf", output, ct);
        if (created == ObjectCreationResult.AlreadyExists && !await HasCompleteOutputAsync(key, snapshot.Length, ct))
            throw new BuildFailure("export_build_failed");
    }

    private async Task<bool> HasCompleteOutputAsync(string key, int pageCount, CancellationToken ct)
    {
        // A successful PUT may outlive a canceled/failed database commit. Never overwrite that
        // immutable key. This HEAD is a recovery optimization, not publication fencing:
        // WriteIfAbsentAsync enforces that atomically even if a remote PUT outlives our lock.
        // Validate before making the existing output visible, or fail closed if it is corrupt.
        var info = await store.HeadAsync(key, ct);
        if (info is null) return false;
        if (info.SizeBytes > limits.MaxPdfBytes) throw new BuildFailure("export_size_limit");
        await using var output = new BoundedPdfStream(limits.MaxPdfBytes, ct);
        await using var input = await store.OpenReadAsync(key, ct);
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0) output.Write(buffer, 0, count);
        output.Position = 0;
        using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        if (pdf.PageCount != pageCount) throw new BuildFailure("export_build_failed");
        return true;
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

    private sealed class BoundedPdfStream(long maxBytes, CancellationToken ct) : FileStream(
        Path.Combine(Path.GetTempPath(), $"superscanner-export-{Guid.NewGuid():N}.tmp"),
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
