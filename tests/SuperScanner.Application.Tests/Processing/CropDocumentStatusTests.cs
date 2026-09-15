using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Processing;
using SuperScanner.Infrastructure.Persistence;
using SuperScanner.Infrastructure.Processing;

namespace SuperScanner.Application.Tests.Processing;

public sealed class CropDocumentStatusTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CropCompletion_SynchronizesPageStateAndProtectsNewerRevision(bool detect)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var document = Document.Create(Guid.NewGuid(), "owner", "Crop", DateTimeOffset.UtcNow);
        var page = document.AddPage(Guid.NewGuid(), 50, DateTimeOffset.UtcNow);
        page.SetPreview("preview", "thumb");
        page.InitializeCrop();
        db.Add(document);
        await db.SaveChangesAsync();
        var processor = new CropProcessor(db, null!, new ConfigurationBuilder().Build(),
            Options.Create(new DocumentBoundaryOptions()), new DocumentBoundaryHealth(), NullLogger<CropProcessor>.Instance);
        var detection = new CropDetectionResult([new(0, 0), new(1, 0), new(1, 1), new(0, 1)], 0, "FullImage", null, "full_image");

        if (detect) await processor.CompleteDetectionAsync(page.Id, page.CropRevision, detection, CancellationToken.None);
        else await processor.FailAsync(page.Id, page.CropRevision, CancellationToken.None);
        db.ChangeTracker.Clear();
        var stored = await db.Pages.SingleAsync();
        Assert.Equal(detect ? PageState.NeedsCrop : PageState.Failed, stored.State);
        Assert.Equal(detect ? null : "crop_failed", stored.FailureCode);

        stored.BeginCrop(true, null);
        await db.SaveChangesAsync();
        if (detect) await processor.CompleteDetectionAsync(stored.Id, stored.CropRevision - 1, detection, CancellationToken.None);
        else await processor.FailAsync(stored.Id, stored.CropRevision - 1, CancellationToken.None);
        db.ChangeTracker.Clear();
        Assert.Equal(PageState.Processing, (await db.Pages.SingleAsync()).State);
    }

    [Theory]
    [InlineData("", DocumentStatus.Draft)]
    [InlineData("Failed", DocumentStatus.Failed)]
    [InlineData("Ready,Failed", DocumentStatus.Ready)]
    [InlineData("NeedsCrop,Failed", DocumentStatus.NeedsCrop)]
    [InlineData("NeedsCrop,Ready", DocumentStatus.NeedsCrop)]
    [InlineData("Processing,NeedsCrop,Failed", DocumentStatus.Processing)]
    [InlineData("Importing,Ready", DocumentStatus.Processing)]
    public async Task Refresh_UsesActivePageStatePrecedence(string states, DocumentStatus expected)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        var document = Document.Create(Guid.NewGuid(), "owner", "Mixed", now);
        foreach (var state in states.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var page = document.AddPage(Guid.NewGuid(), 50, now);
            db.Entry(page).Property(p => p.State).CurrentValue = Enum.Parse<PageState>(state);
        }
        var removed = document.AddPage(Guid.NewGuid(), 50, now);
        document.RemovePage(removed.Id, "owner", now);
        db.Add(document);
        await db.SaveChangesAsync();

        await CropDocumentStatus.RefreshAsync(db, document.Id, CancellationToken.None);
        db.ChangeTracker.Clear();

        Assert.Equal(expected, (await db.Documents.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(2, 1, ProcessingJobStatus.Queued)]
    [InlineData(2, 2, ProcessingJobStatus.Failed)]
    [InlineData(8, 6, ProcessingJobStatus.Queued)]
    [InlineData(8, 8, ProcessingJobStatus.Failed)]
    public async Task ImportRetry_UsesConfiguredAttemptLimit(int maxAttempts, int attempt, ProcessingJobStatus expected)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        var job = ProcessingJob.Create(Guid.NewGuid(), "ExpandDocumentImport", Guid.NewGuid().ToString(), "import", now);
        job.Lease("worker", now, TimeSpan.FromMinutes(2));
        db.Add(job);
        db.Entry(job).Property(j => j.AttemptCount).CurrentValue = attempt;
        await db.SaveChangesAsync();
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<IClock>(new FixedClock(now));
        services.AddSingleton<IOptions<DocumentImportOptions>>(Options.Create(new DocumentImportOptions { MaxAttempts = maxAttempts }));
        await using var provider = services.BuildServiceProvider();
        var queue = ActivatorUtilities.CreateInstance<PostgresJobQueue>(provider);

        await queue.RescheduleAsync(job.Id, "worker", "import_failed", CancellationToken.None);
        db.ChangeTracker.Clear();

        var stored = await db.ProcessingJobs.SingleAsync();
        Assert.Equal(expected, stored.Status);
        Assert.Equal("import_failed", stored.ErrorCode);
    }

    private sealed record FixedClock(DateTimeOffset UtcNow) : IClock;
}
