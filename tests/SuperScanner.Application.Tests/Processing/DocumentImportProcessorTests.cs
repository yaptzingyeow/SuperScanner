using ImageMagick;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Uploads;
using SuperScanner.Infrastructure.Persistence;
using SuperScanner.Infrastructure.Processing;

namespace SuperScanner.Application.Tests.Processing;

public sealed class DocumentImportProcessorTests
{
    [Fact]
    public async Task Poppler_InspectsAndRendersDeterministicThreePagePdf()
    {
        var directory = Directory.CreateTempSubdirectory("superscanner-poppler-test-");
        try
        {
            var sourcePath = Path.Combine(directory.FullName, "three-pages.pdf");
            var outputPath = Path.Combine(directory.FullName, "page-2.png");
            await File.WriteAllBytesAsync(sourcePath, PdfFixtures.ThreePage.Bytes);
            var tool = new PopplerPdfImportTool(Options.Create(new DocumentImportOptions()));

            var inspection = await tool.InspectAsync(sourcePath, CancellationToken.None);
            await tool.RenderPageAsync(sourcePath, 2, outputPath, CancellationToken.None);

            Assert.Equal(3, inspection.PageCount);
            Assert.False(inspection.IsEncrypted);
            Assert.True(inspection.EstimatedDecodedPixels > 0);
            Assert.True(new FileInfo(outputPath).Length > 0);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task Poppler_AccountsForEveryMixedPageDimension()
    {
        var directory = Directory.CreateTempSubdirectory("superscanner-poppler-test-");
        try
        {
            var sourcePath = Path.Combine(directory.FullName, "mixed-pages.pdf");
            await File.WriteAllBytesAsync(sourcePath, PdfFixtures.MixedSize.Bytes);
            var tool = new PopplerPdfImportTool(Options.Create(new DocumentImportOptions()));

            var inspection = await tool.InspectAsync(sourcePath, CancellationToken.None);

            Assert.Equal(2, inspection.PageCount);
            Assert.Equal(450_000, inspection.EstimatedDecodedPixels);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task Photo_UsesAcceptedSourceAndQueuesPageDetection()
    {
        await using var fixture = await Fixture.CreateAsync("image/png");
        await fixture.ProcessAsync();
        var page = Assert.Single(fixture.Document.ActivePages);
        Assert.Equal("imports/accepted", page.OriginalObjectKey);
        Assert.Equal("image/png", page.OriginalMediaType);
        Assert.NotNull(page.PreviewObjectKey);
        Assert.Equal("Detecting", page.CropStatus);
        Assert.Equal(PageState.Processing, page.State);
        Assert.Equal($"{page.Id}:1", Assert.Single(fixture.Db.ProcessingJobs).Payload);
        Assert.Equal(1, fixture.Upload.CreatedPageCount);
        Assert.Equal(1, fixture.Document.Revision); // Membership only; detection is not exportable content.
    }

    [Fact]
    public async Task ThreePagePdf_CreatesOrderedDeterministicPagesAndTypedSources()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ProcessAsync();
        Assert.Equal(new[] { 1, 2, 3 }, fixture.Document.ActivePages.Select(x => x.SourcePageIndex));
        Assert.Equal(new[] { 1, 2, 3 }, fixture.Document.ActivePages.Select(x => x.Position));
        Assert.All(fixture.Document.Pages, page =>
        {
            Assert.Equal($"page-sources/{fixture.Document.Id:N}/{page.Id:N}/source.png", page.OriginalObjectKey);
            Assert.Equal("image/png", fixture.Store.Objects[page.OriginalObjectKey!].MediaType);
            Assert.Equal("Detecting", page.CropStatus);
        });
        Assert.Equal(3, fixture.Upload.CreatedPageCount);
        Assert.Equal(0, fixture.Upload.FailedPageCount);
        Assert.Equal(3, fixture.Upload.DiscoveredPageCount);
    }

    [Fact]
    public async Task Retry_ReusesSourcesPagesAndDetectionJobs()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ProcessAsync();
        var ids = fixture.Document.Pages.Select(x => x.Id).ToArray();
        var keys = fixture.Store.Objects.Keys.Order().ToArray();
        await fixture.ProcessAsync();
        Assert.Equal(ids, fixture.Document.Pages.Select(x => x.Id));
        Assert.Equal(keys, fixture.Store.Objects.Keys.Order());
        Assert.Equal(3, await fixture.Db.ProcessingJobs.CountAsync());
        Assert.Equal(3, fixture.Pdf.RenderedPages.Count);
        Assert.All(fixture.Store.WriteCounts, pair => Assert.Equal(1, pair.Value));
    }

    [Fact]
    public async Task Retry_DoesNotExceedAggregateRenderedLimitWithPersistedSources()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Options.MaxRenderedBytes = Png().LongLength * 2;

        await fixture.ProcessAsync();
        await fixture.ProcessAsync();

        Assert.Equal(2, fixture.Store.Objects.Keys.Count(key => key.StartsWith("page-sources/", StringComparison.Ordinal)));
        Assert.Equal(PageState.Failed, fixture.Document.ActivePages.Last().State);
        Assert.Equal("render_size_limit", fixture.Document.ActivePages.Last().FailureCode);
    }

    [Fact]
    public async Task Retry_AfterTransientPreviewFailure_ClearsImportFailure()
    {
        await using var fixture = await Fixture.CreateAsync("image/png");
        fixture.Store.FailNextWriteWithPrefix = "previews/";

        await fixture.ProcessAsync();
        var page = Assert.Single(fixture.Document.ActivePages);
        Assert.Equal(PageState.Failed, page.State);

        await fixture.ProcessAsync();

        Assert.Equal(PageState.Processing, page.State);
        Assert.Null(page.FailureCode);
        Assert.Equal("Detecting", page.CropStatus);
    }

    [Theory]
    [InlineData(3, true, 2000000, "pdf_encrypted")]
    [InlineData(51, true, 250000001, "pdf_encrypted")]
    [InlineData(51, false, 2000000, "import_page_limit")]
    [InlineData(51, false, 250000001, "import_page_limit")]
    [InlineData(3, false, 250000001, "import_pixel_limit")]
    [InlineData(0, false, 0, "pdf_invalid")]
    public async Task UnsafePdf_IsRejectedBeforeCreatingPages(int pages, bool encrypted, long pixels, string code)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Pdf.Inspection = encrypted
            ? PdfFixtures.Encrypted.Inspection with { PageCount = pages, EstimatedDecodedPixels = pixels }
            : new PdfInspection(pages, false, pixels);
        await fixture.ProcessAsync();
        Assert.Empty(fixture.Document.Pages);
        Assert.Empty(fixture.Pdf.RenderedPages);
        Assert.Equal(code, fixture.Upload.ExpansionErrorCode);
        Assert.Equal(0, fixture.Upload.CreatedPageCount);
    }

    [Fact]
    public async Task InsufficientCapacity_CreatesNoPartialPlaceholders()
    {
        await using var fixture = await Fixture.CreateAsync();
        var existingPages = fixture.Document.AppendImportedPages(Guid.NewGuid(), Enumerable.Range(1, 48).ToArray(), 50, DateTimeOffset.UtcNow);
        fixture.Db.Pages.AddRange(existingPages);
        await fixture.Db.SaveChangesAsync();
        await fixture.ProcessAsync();
        Assert.Equal(48, fixture.Document.Pages.Count);
        Assert.DoesNotContain(fixture.Document.Pages, p => p.SourceUploadId == fixture.Upload.Id);
        Assert.Equal("document_page_limit", fixture.Upload.ExpansionErrorCode);
        Assert.Empty(fixture.Pdf.RenderedPages);
    }

    [Fact]
    public async Task MiddlePageFailure_AllowsOtherPagesToAdvanceAndRetryRepairsOnlyFailure()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Pdf.FailPage = 2;
        await fixture.ProcessAsync();
        var pages = fixture.Document.ActivePages.ToArray();
        Assert.Equal(new[] { PageState.Processing, PageState.Failed, PageState.Processing }, pages.Select(x => x.State));
        Assert.Equal("pdf_render_failed", pages[1].FailureCode);
        Assert.Equal(2, fixture.Upload.CreatedPageCount);
        Assert.Equal(1, fixture.Upload.FailedPageCount);
        Assert.Equal(2, await fixture.Db.ProcessingJobs.CountAsync());
        fixture.Pdf.FailPage = null;
        await fixture.ProcessAsync();
        Assert.Equal(pages.Select(x => x.Id), fixture.Document.ActivePages.Select(x => x.Id));
        Assert.All(pages, p => Assert.Equal("Detecting", p.CropStatus));
        Assert.Equal(3, fixture.Upload.CreatedPageCount);
        Assert.Equal(0, fixture.Upload.FailedPageCount);
        Assert.Null(fixture.Upload.ExpansionErrorCode);
    }

    [Fact]
    public async Task ActualDownloadLimit_IsEnforcedBeforeInspection()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Options.MaxUploadBytes = 5;
        await fixture.ProcessAsync();
        Assert.Empty(fixture.Document.Pages);
        Assert.Equal("import_size_limit", fixture.Upload.ExpansionErrorCode);
        Assert.Equal(0, fixture.Pdf.InspectCalls);
    }

    [Fact]
    public async Task DecodedPhotoLimit_IsEnforcedBeforePageCreation()
    {
        await using var fixture = await Fixture.CreateAsync("image/png");
        fixture.Options.MaxDecodedPixels = 10;
        await fixture.ProcessAsync();
        Assert.Empty(fixture.Document.Pages);
        Assert.Equal("import_pixel_limit", fixture.Upload.ExpansionErrorCode);
    }

    [Fact]
    public async Task RemovedPage_IsNotResurrectedByImportRetry()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ProcessAsync();
        var page = fixture.Document.ActivePages.Last();
        fixture.Document.RemovePage(page.Id, "owner", DateTimeOffset.UtcNow);
        await fixture.Db.SaveChangesAsync();
        await fixture.ProcessAsync();
        Assert.Equal(2, fixture.Document.ActivePages.Count);
        Assert.Equal(3, fixture.Document.Pages.Count);
        Assert.NotNull(page.RemovedAt);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public AppDbContext Db { get; private set; } = null!;
        public Document Document { get; private set; } = null!;
        public UploadIntent Upload { get; private set; } = null!;
        public MemoryStore Store { get; } = new();
        public FakePdfTool Pdf { get; } = new();
        public DocumentImportOptions Options { get; } = new();
        private DocumentImportProcessor Processor { get; set; } = null!;

        public static async Task<Fixture> CreateAsync(string mediaType = "application/pdf")
        {
            var fixture = new Fixture();
            await fixture.connection.OpenAsync();
            fixture.Db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(fixture.connection).Options);
            await fixture.Db.Database.EnsureCreatedAsync();
            var now = DateTimeOffset.UtcNow;
            fixture.Document = Document.Create(Guid.NewGuid(), "owner", "Synthetic scan", now);
            fixture.Upload = UploadIntent.Create(Guid.NewGuid(), "owner", fixture.Document.Id,
                "quarantine/source", "synthetic", mediaType, 100, new string('a', 64), now.AddHours(1));
            fixture.Upload.TryMarkPendingValidation(now);
            fixture.Upload.Accept("imports/accepted", now);
            fixture.Db.AddRange(fixture.Document, fixture.Upload);
            await fixture.Db.SaveChangesAsync();
            fixture.Db.ChangeTracker.Clear();
            fixture.Document = await fixture.Db.Documents.Include(x => x.Pages).SingleAsync();
            fixture.Upload = await fixture.Db.UploadIntents.SingleAsync();
            fixture.Store.Objects["imports/accepted"] = (mediaType, mediaType == "application/pdf" ? PdfFixtures.ThreePage.Bytes : Png());
            var options = Microsoft.Extensions.Options.Options.Create(fixture.Options);
            var preview = new DocumentPreviewProcessor(fixture.Db, fixture.Store);
            var crop = new CropProcessor(fixture.Db, fixture.Store, new ConfigurationBuilder().Build(),
                Microsoft.Extensions.Options.Options.Create(new DocumentBoundaryOptions()),
                new DocumentBoundaryHealth(), NullLogger<CropProcessor>.Instance);
            fixture.Processor = new(fixture.Db, fixture.Store, fixture.Pdf, preview, crop, options);
            return fixture;
        }

        public Task ProcessAsync() => Processor.ProcessAsync(Upload.Id, CancellationToken.None);
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    private static byte[] Png()
    {
        using var image = new MagickImage(MagickColors.White, 32, 48);
        image.Format = MagickFormat.Png;
        return image.ToByteArray();
    }

    // Deterministic, non-personal PDF inputs keep this fake-tool suite self-contained.
    private static class PdfFixtures
    {
        public static readonly PdfFixture ThreePage = new(Create(3), new PdfInspection(3, false, 2_000_000));
        public static readonly PdfFixture Encrypted = new(Create(3), new PdfInspection(3, true, 2_000_000));
        public static readonly PdfFixture MixedSize = new(Create([(72, 72), (144, 144)]), new PdfInspection(2, false, 450_000));

        private static byte[] Create(int pageCount) => Create(Enumerable.Repeat((612, 792), pageCount).ToArray());

        private static byte[] Create(IReadOnlyList<(int Width, int Height)> pageSizes)
        {
            var pageCount = pageSizes.Count;
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                $"<< /Type /Pages /Kids [{string.Join(' ', Enumerable.Range(0, pageCount).Select(index => $"{4 + index * 2} 0 R"))}] /Count {pageCount} >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
            };
            for (var page = 1; page <= pageCount; page++)
            {
                var pageObject = 4 + (page - 1) * 2;
                var content = $"BT /F1 12 Tf 72 720 Td (Synthetic page {page}) Tj ET";
                var size = pageSizes[page - 1];
                objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {size.Width} {size.Height}] /Resources << /Font << /F1 3 0 R >> >> /Contents {pageObject + 1} 0 R >>");
                objects.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
            }
            var pdf = new System.Text.StringBuilder("%PDF-1.4\n");
            var offsets = new List<int> { 0 };
            for (var index = 0; index < objects.Count; index++)
            {
                offsets.Add(pdf.Length);
                pdf.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
            }
            var xref = pdf.Length;
            pdf.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n");
            foreach (var offset in offsets.Skip(1)) pdf.Append(offset.ToString("D10")).Append(" 00000 n \n");
            pdf.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n")
                .Append(xref).Append("\n%%EOF\n");
            return System.Text.Encoding.ASCII.GetBytes(pdf.ToString());
        }
    }

    private sealed record PdfFixture(byte[] Bytes, PdfInspection Inspection);

    private sealed class FakePdfTool : IPdfImportTool
    {
        public PdfInspection Inspection { get; set; } = PdfFixtures.ThreePage.Inspection;
        public int? FailPage { get; set; }
        public int InspectCalls { get; private set; }
        public List<int> RenderedPages { get; } = [];
        public Task<PdfInspection> InspectAsync(string sourcePath, CancellationToken ct)
        { InspectCalls++; return Task.FromResult(Inspection); }
        public async Task RenderPageAsync(string sourcePath, int pageIndex, string outputPngPath, CancellationToken ct)
        {
            RenderedPages.Add(pageIndex);
            if (pageIndex == FailPage) throw new PdfImportException("pdf_render_failed");
            await File.WriteAllBytesAsync(outputPngPath, Png(), ct);
        }
    }

    private sealed class MemoryStore : IObjectStore
    {
        public Dictionary<string, (string MediaType, byte[] Bytes)> Objects { get; } = [];
        public Dictionary<string, int> WriteCounts { get; } = [];
        public string? FailNextWriteWithPrefix { get; set; }
        public Task<StoredObjectInfo?> HeadAsync(string key, CancellationToken ct) => Task.FromResult(
            Objects.TryGetValue(key, out var value) ? new StoredObjectInfo(value.Bytes.Length, value.MediaType, "etag") : null);
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(Objects[key].Bytes));
        public async Task WriteAsync(string key, string mediaType, Stream content, CancellationToken ct)
        {
            if (FailNextWriteWithPrefix is { } prefix && key.StartsWith(prefix, StringComparison.Ordinal))
            {
                FailNextWriteWithPrefix = null;
                throw new IOException("Synthetic write failure.");
            }
            using var bytes = new MemoryStream();
            await content.CopyToAsync(bytes, ct);
            Objects.Add(key, (mediaType, bytes.ToArray()));
            WriteCounts[key] = WriteCounts.GetValueOrDefault(key) + 1;
        }
        public Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task PromoteAsync(string quarantineKey, string acceptedKey, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    }
}
