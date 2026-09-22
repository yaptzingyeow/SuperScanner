using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;
using SuperScanner.Infrastructure.Ocr;
using SuperScanner.Infrastructure.Persistence;
using SuperScanner.Infrastructure.Processing;
using SuperScanner.Application.Tests.Processing;

namespace SuperScanner.Application.Tests.Ocr;

public sealed class OcrSchedulingTests
{
    [Fact]
    public async Task SuccessfulCrop_WhenEnabled_QueuesExactNewPreviewOnce()
    {
        await using var fixture = await Fixture.CreateAsync(enabled: true);

        var completed = await fixture.Crop.CompletePerspectiveCropAsync(
            fixture.PageId, fixture.Revision, "previews/new.jpg", "thumbs/new.jpg", default);

        Assert.True(completed);
        var result = Assert.Single(await fixture.Db.PageOcrResults.ToListAsync());
        Assert.Equal("previews/new.jpg", result.SourceObjectKey);
        var job = Assert.Single(await fixture.Db.ProcessingJobs
            .Where(candidate => candidate.Type == "RecognizePageText").ToListAsync());
        Assert.Equal(result.Id.ToString(), job.Payload);
    }

    [Fact]
    public async Task SupersededCrop_DoesNotQueueOcrForRejectedRevision()
    {
        await using var fixture = await Fixture.CreateAsync(enabled: true);

        var completed = await fixture.Crop.CompletePerspectiveCropAsync(
            fixture.PageId, fixture.Revision - 1, "previews/old.jpg", "thumbs/old.jpg", default);

        Assert.False(completed);
        Assert.Empty(await fixture.Db.PageOcrResults.ToListAsync());
        Assert.Empty(await fixture.Db.ProcessingJobs.Where(x => x.Type == "RecognizePageText").ToListAsync());
    }

    [Fact]
    public async Task SuccessfulCrop_WhenDisabled_DoesNotCreateOcrWork()
    {
        await using var fixture = await Fixture.CreateAsync(enabled: false);

        Assert.True(await fixture.Crop.CompletePerspectiveCropAsync(
            fixture.PageId, fixture.Revision, "previews/new.jpg", "thumbs/new.jpg", default));

        Assert.Empty(await fixture.Db.PageOcrResults.ToListAsync());
        Assert.Empty(await fixture.Db.ProcessingJobs.Where(x => x.Type == "RecognizePageText").ToListAsync());
    }

    [Fact]
    public async Task NewCropRevision_CreatesOneAdditionalSourceResult()
    {
        await using var fixture = await Fixture.CreateAsync(enabled: true);
        Assert.True(await fixture.Crop.CompletePerspectiveCropAsync(
            fixture.PageId, fixture.Revision, "previews/first.jpg", "thumbs/first.jpg", default));
        fixture.Db.ChangeTracker.Clear();
        var page = await fixture.Db.Pages.SingleAsync();
        page.BeginCrop(false, "[]");
        await fixture.Db.SaveChangesAsync();

        Assert.True(await fixture.Crop.CompletePerspectiveCropAsync(
            page.Id, page.CropRevision, "previews/second.jpg", "thumbs/second.jpg", default));

        Assert.Equal(2, await fixture.Db.PageOcrResults.CountAsync());
        Assert.Equal(2, await fixture.Db.ProcessingJobs.CountAsync(x => x.Type == "RecognizePageText"));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private Fixture(SqliteConnection connection, AppDbContext db, Guid pageId, int revision, CropProcessor crop)
        {
            this.connection = connection;
            Db = db;
            PageId = pageId;
            Revision = revision;
            Crop = crop;
        }
        public AppDbContext Db { get; }
        public Guid PageId { get; }
        public int Revision { get; }
        public CropProcessor Crop { get; }

        public static async Task<Fixture> CreateAsync(bool enabled)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new WorkerLockOrderInterceptor(new WorkerLockOrderObserver()))
                .Options);
            await db.Database.EnsureCreatedAsync();
            var now = new DateTimeOffset(2026, 9, 22, 9, 0, 0, TimeSpan.Zero);
            var document = Document.Create(Guid.NewGuid(), "owner", "OCR scheduling", now);
            var page = document.AddPage(Guid.NewGuid(), 50, now);
            page.SetPreview("previews/source.jpg", "thumbs/source.jpg");
            page.InitializeCrop();
            page.BeginCrop(false, "[]");
            db.Add(document);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var clock = new FixedClock(now);
            var options = Options.Create(new OcrOptions
            {
                Enabled = enabled,
                Provider = enabled ? "Fake" : "Disabled"
            });
            var scheduler = new OcrJobScheduler(
                new EfOcrRepository(db), new PostgresJobQueue(db, clock), clock, options);
            var crop = new CropProcessor(db, null!, new ConfigurationBuilder().Build(),
                Options.Create(new DocumentBoundaryOptions()), new DocumentBoundaryHealth(),
                NullLogger<CropProcessor>.Instance, scheduler);
            return new Fixture(connection, db, page.Id, page.CropRevision, crop);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed record FixedClock(DateTimeOffset UtcNow) : IClock;
}
