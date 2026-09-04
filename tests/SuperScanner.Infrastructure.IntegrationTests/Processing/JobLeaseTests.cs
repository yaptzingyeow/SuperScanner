using Microsoft.EntityFrameworkCore;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Processing;
using SuperScanner.Infrastructure.Persistence;
using SuperScanner.Infrastructure.Processing;
using Testcontainers.PostgreSql;

namespace SuperScanner.Infrastructure.IntegrationTests.Processing;

public sealed class JobLeaseTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 9, 4, 2, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private DbContextOptions<AppDbContext> _options = null!;

    [Fact]
    public async Task DuplicateEnqueue_StoresOneJob()
    {
        var clock = new MutableClock(Start);
        await using var db = CreateDb();
        var queue = new PostgresJobQueue(db, clock);

        await queue.EnqueueAsync("ValidateUpload", "upload-1", "upload:1:validate", CancellationToken.None);
        await queue.EnqueueAsync("ValidateUpload", "upload-1", "upload:1:validate", CancellationToken.None);

        Assert.Equal(1, await db.ProcessingJobs.CountAsync());
    }

    [Fact]
    public async Task OnlyOneWorkerLeasesAJob()
    {
        var clock = new MutableClock(Start);
        await using (var seedDb = CreateDb())
        {
            await new PostgresJobQueue(seedDb, clock).EnqueueAsync(
                "ValidateUpload",
                "upload-1",
                "upload:1:validate",
                CancellationToken.None);
        }

        await using var dbA = CreateDb();
        await using var dbB = CreateDb();
        var claims = await Task.WhenAll(
            new PostgresJobQueue(dbA, clock).TryLeaseAsync(
                "worker-a",
                TimeSpan.FromMinutes(2),
                CancellationToken.None),
            new PostgresJobQueue(dbB, clock).TryLeaseAsync(
                "worker-b",
                TimeSpan.FromMinutes(2),
                CancellationToken.None));

        Assert.Single(claims, claim => claim is not null);
    }

    [Fact]
    public async Task Heartbeat_PreventsLeaseRecoveryUntilExtendedExpiry()
    {
        var clock = new MutableClock(Start);
        await using var ownerDb = CreateDb();
        var ownerQueue = new PostgresJobQueue(ownerDb, clock);
        await ownerQueue.EnqueueAsync("ValidateUpload", "upload-1", "upload:1:validate", CancellationToken.None);
        var lease = await ownerQueue.TryLeaseAsync(
            "worker-a",
            TimeSpan.FromMinutes(2),
            CancellationToken.None);

        clock.UtcNow = Start.AddMinutes(1);
        Assert.True(await ownerQueue.HeartbeatAsync(
            lease!.Id,
            "worker-a",
            TimeSpan.FromMinutes(2),
            CancellationToken.None));

        clock.UtcNow = Start.AddMinutes(2).AddSeconds(1);
        await using var contenderDb = CreateDb();
        var claim = await new PostgresJobQueue(contenderDb, clock).TryLeaseAsync(
            "worker-b",
            TimeSpan.FromMinutes(2),
            CancellationToken.None);

        Assert.Null(claim);
    }

    [Fact]
    public async Task ExpiredLease_IsRecoveredByOneOtherWorker()
    {
        var clock = new MutableClock(Start);
        await using var firstDb = CreateDb();
        var firstQueue = new PostgresJobQueue(firstDb, clock);
        await firstQueue.EnqueueAsync("ValidateUpload", "upload-1", "upload:1:validate", CancellationToken.None);
        var firstLease = await firstQueue.TryLeaseAsync(
            "worker-a",
            TimeSpan.FromMinutes(2),
            CancellationToken.None);

        clock.UtcNow = Start.AddMinutes(2);
        await using var recoveryDb = CreateDb();
        var recovered = await new PostgresJobQueue(recoveryDb, clock).TryLeaseAsync(
            "worker-b",
            TimeSpan.FromMinutes(2),
            CancellationToken.None);

        Assert.NotNull(recovered);
        Assert.Equal(firstLease!.Id, recovered!.Id);
        Assert.Equal(2, recovered.AttemptCount);
    }

    [Fact]
    public async Task TransientFailures_UseBoundedBackoffThenFailPermanently()
    {
        var delays = new[]
        {
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(30)
        };
        var clock = new MutableClock(Start);
        await using var db = CreateDb();
        var queue = new PostgresJobQueue(db, clock);
        await queue.EnqueueAsync("ValidateUpload", "upload-1", "upload:1:validate", CancellationToken.None);

        for (var index = 0; index < delays.Length; index++)
        {
            var lease = await queue.TryLeaseAsync("worker-a", TimeSpan.FromMinutes(2), CancellationToken.None);
            Assert.NotNull(lease);
            var failedAt = clock.UtcNow;

            await queue.RescheduleAsync(
                lease!.Id,
                "worker-a",
                "scanner_unavailable",
                CancellationToken.None);

            var job = await db.ProcessingJobs.AsNoTracking().SingleAsync();
            Assert.Equal(ProcessingJobStatus.Queued, job.Status);
            Assert.Equal(failedAt.Add(delays[index]), job.AvailableAt);
            clock.UtcNow = job.AvailableAt;
        }

        var finalLease = await queue.TryLeaseAsync("worker-a", TimeSpan.FromMinutes(2), CancellationToken.None);
        Assert.NotNull(finalLease);
        await queue.RescheduleAsync(
            finalLease!.Id,
            "worker-a",
            "scanner_unavailable",
            CancellationToken.None);

        var failedJob = await db.ProcessingJobs.AsNoTracking().SingleAsync();
        Assert.Equal(ProcessingJobStatus.Failed, failedJob.Status);
        Assert.Equal(6, failedJob.AttemptCount);
        Assert.Equal("scanner_unavailable", failedJob.ErrorCode);
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var db = CreateDb();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private AppDbContext CreateDb() => new(_options);

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
