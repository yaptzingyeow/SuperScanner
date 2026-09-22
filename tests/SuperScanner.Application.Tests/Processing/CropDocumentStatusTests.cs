using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    [Fact]
    public async Task EnsureDetection_TakesDocumentLockBeforePageLockAndMutation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var order = new WorkerLockOrderObserver();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new WorkerLockOrderInterceptor(order))
            .Options);
        await db.Database.EnsureCreatedAsync();
        var document = Document.Create(Guid.NewGuid(), "owner", "Crop", DateTimeOffset.UtcNow);
        var page = document.AddPage(Guid.NewGuid(), 50, DateTimeOffset.UtcNow);
        page.SetPreview("preview", "thumb");
        db.Add(document);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        order.Operations.Clear();
        var processor = new CropProcessor(db, null!, new ConfigurationBuilder().Build(),
            Options.Create(new DocumentBoundaryOptions()), new DocumentBoundaryHealth(), NullLogger<CropProcessor>.Instance);

        await processor.EnsureDetectionForPageAsync(page.Id, CancellationToken.None);

        Assert.Equal(["document", "page", "page-mutation"], order.Operations);
    }

    [Fact]
    public async Task PerspectiveCompletion_TakesDocumentLockBeforePageLockAndMutation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var order = new WorkerLockOrderObserver();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new WorkerLockOrderInterceptor(order))
            .Options);
        await db.Database.EnsureCreatedAsync();
        var document = Document.Create(Guid.NewGuid(), "owner", "Crop", DateTimeOffset.UtcNow);
        var page = document.AddPage(Guid.NewGuid(), 50, DateTimeOffset.UtcNow);
        page.SetPreview("source", "source-thumb");
        page.InitializeCrop();
        page.BeginCrop(false, "[]");
        db.Add(document);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        order.Operations.Clear();
        var processor = new CropProcessor(db, null!, new ConfigurationBuilder().Build(),
            Options.Create(new DocumentBoundaryOptions()), new DocumentBoundaryHealth(), NullLogger<CropProcessor>.Instance);

        var completed = await processor.CompletePerspectiveCropAsync(
            page.Id, page.CropRevision, "applied-preview", "applied-thumb", CancellationToken.None);

        Assert.True(completed);
        Assert.Equal(["document", "page", "page-mutation"], order.Operations);
        db.ChangeTracker.Clear();
        var stored = await db.Pages.SingleAsync();
        Assert.Equal("Ready", stored.CropStatus);
        Assert.Equal(page.CropRevision, stored.AppliedCropRevision);
    }

    [Fact]
    public async Task StalePerspectiveCompletion_DoesNotOverwriteNewerCropRevision()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var order = new WorkerLockOrderObserver();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new WorkerLockOrderInterceptor(order))
            .Options);
        await db.Database.EnsureCreatedAsync();
        var document = Document.Create(Guid.NewGuid(), "owner", "Crop", DateTimeOffset.UtcNow);
        var page = document.AddPage(Guid.NewGuid(), 50, DateTimeOffset.UtcNow);
        page.SetPreview("source", "source-thumb");
        page.InitializeCrop();
        page.BeginCrop(false, "[]");
        page.BeginCrop(false, "[]");
        db.Add(document);
        await db.SaveChangesAsync();
        var processor = new CropProcessor(db, null!, new ConfigurationBuilder().Build(),
            Options.Create(new DocumentBoundaryOptions()), new DocumentBoundaryHealth(), NullLogger<CropProcessor>.Instance);

        var completed = await processor.CompletePerspectiveCropAsync(
            page.Id, page.CropRevision - 1, "stale-preview", "stale-thumb", CancellationToken.None);

        Assert.False(completed);
        db.ChangeTracker.Clear();
        var stored = await db.Pages.SingleAsync();
        Assert.Equal("Processing", stored.CropStatus);
        Assert.NotEqual("stale-preview", stored.PreviewObjectKey);
        Assert.NotEqual(page.CropRevision - 1, stored.AppliedCropRevision);
    }

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

internal sealed class WorkerLockOrderObserver
{
    public List<string> Operations { get; } = [];
}

internal sealed class WorkerLockOrderInterceptor(WorkerLockOrderObserver order) : DbCommandInterceptor
{
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Observe(command);
        command.CommandText = command.CommandText.Replace(" FOR UPDATE", "", StringComparison.Ordinal);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Observe(command);
        return ValueTask.FromResult(result);
    }

    private void Observe(DbCommand command)
    {
        if (command.CommandText.Contains("FROM documents", StringComparison.Ordinal) &&
            command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            order.Operations.Add("document");
        if (command.CommandText.Contains("FROM pages", StringComparison.Ordinal) &&
            command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            order.Operations.Add("page");
        if (command.CommandText.Contains("UPDATE \"pages\"", StringComparison.Ordinal))
            order.Operations.Add("page-mutation");
    }
}
