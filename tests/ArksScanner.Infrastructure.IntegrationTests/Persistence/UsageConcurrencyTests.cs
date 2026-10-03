using Microsoft.EntityFrameworkCore;
using ArksScanner.Application.Abstractions;
using ArksScanner.Domain.Plans;
using ArksScanner.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace ArksScanner.Infrastructure.IntegrationTests.Persistence;

public sealed class UsageConcurrencyTests : IAsyncLifetime
{
    private static readonly DateOnly Day = new(2026, 10, 4);
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly MutableClock clock = new(new DateTimeOffset(2026, 10, 4, 6, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await using var db = Db();
        await db.Database.MigrateAsync();
        db.Accounts.Add(Account.Create("u1", null, "password", false, clock.UtcNow));
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    private AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);

    private EfPlanRepository Repository(AppDbContext db, PlanSettingsCache? cache = null) =>
        new(db, cache ?? new PlanSettingsCache(), clock, new PlanSeed(new PlanSettingsValues(PlanPhase.Enforced, null, 5, 3, 30, 7, "Asia/Kuala_Lumpur", 1.5m)));

    [Fact]
    public async Task Twenty_parallel_consumes_with_one_page_left_succeed_exactly_once()
    {
        await using (var db = Db())
            for (var i = 0; i < 4; i++)
                Assert.NotNull(await Repository(db).TryConsumeAsync("u1", Day, UsageKind.Ocr, 1, 5, default));

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var db = Db();
            return await Repository(db).TryConsumeAsync("u1", Day, UsageKind.Ocr, 1, 5, default);
        }));

        Assert.Single(results, r => r is not null);
        await using var verify = Db();
        Assert.Equal(5, (await Repository(verify).GetUsageAsync("u1", Day, default)).OcrPages);
    }

    [Fact]
    public async Task First_use_respects_a_zero_limit_and_unlimited_always_counts()
    {
        await using var db = Db();
        var repository = Repository(db);
        Assert.Null(await repository.TryConsumeAsync("u1", Day, UsageKind.Watermark, 1, 0, default));
        Assert.Equal(1, await repository.TryConsumeAsync("u1", Day, UsageKind.Ocr, 1, null, default));
        Assert.Equal(2, await repository.TryConsumeAsync("u1", Day, UsageKind.Ocr, 1, null, default));
    }

    [Fact]
    public async Task Settings_are_seeded_from_configuration_and_cached_for_30_seconds()
    {
        var cache = new PlanSettingsCache();
        await using (var db = Db())
        {
            var seeded = await Repository(db, cache).GetSettingsAsync(default);
            Assert.Equal(PlanPhase.Enforced, seeded.Phase);
            Assert.Equal(5, seeded.FreeOcrPagesPerDay);
            Assert.Equal(3, seeded.FreeWatermarkExportsPerDay);
        }

        await using (var change = Db())
        {
            var row = await change.PlanSettings.SingleAsync();
            row.Update(row.Values with { FreeOcrPagesPerDay = 9 }, "admin", clock.UtcNow);
            await change.SaveChangesAsync();
        }

        await using var read = Db();
        Assert.Equal(5, (await Repository(read, cache).GetSettingsAsync(default)).FreeOcrPagesPerDay);
        clock.UtcNow = clock.UtcNow.AddSeconds(31);
        Assert.Equal(9, (await Repository(read, cache).GetSettingsAsync(default)).FreeOcrPagesPerDay);
    }

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }
}
