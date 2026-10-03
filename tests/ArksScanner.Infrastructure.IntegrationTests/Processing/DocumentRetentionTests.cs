using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ArksScanner.Application.Abstractions;
using ArksScanner.Application.Plans;
using ArksScanner.Domain.Documents;
using ArksScanner.Domain.Plans;
using ArksScanner.Infrastructure.Auditing;
using ArksScanner.Infrastructure.Persistence;
using ArksScanner.Infrastructure.Processing;
using Testcontainers.PostgreSql;

namespace ArksScanner.Infrastructure.IntegrationTests.Processing;

public sealed class DocumentRetentionTests : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 4, 2, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly MutableClock clock = new(T0);
    private PlanPhase phase = PlanPhase.Enforced;

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await using var db = Db();
        await db.Database.MigrateAsync();
        db.Accounts.Add(Account.Create("u1", "u1@example.test", "password", false, T0));
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    private AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);

    private async Task<int> RunAsync()
    {
        await using var db = Db();
        var repository = new EfPlanRepository(db, new PlanSettingsCache(), clock,
            new PlanSeed(new PlanSettingsValues(phase, null, 5, 3, 30, 7, "Asia/Kuala_Lumpur", 1.5m)));
        var audit = new HmacAuditWriter(db, Options.Create(new AuditOptions
        {
            SigningKeyBase64 = Convert.ToBase64String(Enumerable.Range(1, 32).Select(v => (byte)v).ToArray()),
            SigningKeyId = "retention-tests",
        }));
        return await new DocumentRetention(db, new PlanService(repository, clock), audit, clock).RunAsync(default);
    }

    private async Task<Guid> AddDocumentAsync()
    {
        await using var db = Db();
        var document = Document.Create(Guid.NewGuid(), "u1", "Old", clock.UtcNow);
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        return document.Id;
    }

    private async Task GrantProAsync(DateTimeOffset? endsAt)
    {
        await using var db = Db();
        db.Subscriptions.Add(Subscription.GrantManual(Guid.NewGuid(), "u1", clock.UtcNow, endsAt, null, "admin", clock.UtcNow));
        await db.SaveChangesAsync();
    }

    private async Task<Document> LoadAsync(Guid id)
    {
        await using var db = Db();
        return await db.Documents.IgnoreQueryFilters().AsNoTracking().SingleAsync(d => d.Id == id);
    }

    [Fact]
    public async Task Expired_free_documents_are_removed_and_audited()
    {
        var id = await AddDocumentAsync();
        Assert.Equal(0, await RunAsync());
        clock.UtcNow = T0.AddDays(8);

        Assert.Equal(1, await RunAsync());

        var document = await LoadAsync(id);
        Assert.NotNull(document.RemovedAt);
        Assert.Equal("retention", document.RemovedReason);
        await using var db = Db();
        Assert.True(await db.AuditEvents.AnyAsync(e => e.Action == "document.expired_removed" && e.TargetId == id));
    }

    [Fact]
    public async Task Pro_or_test_phase_documents_are_never_removed()
    {
        var id = await AddDocumentAsync();
        await GrantProAsync(null);
        clock.UtcNow = T0.AddDays(30);
        Assert.Equal(0, await RunAsync());
        Assert.Null((await LoadAsync(id)).RemovedAt);
    }

    [Fact]
    public async Task Test_phase_documents_are_never_removed()
    {
        phase = PlanPhase.Test;
        var id = await AddDocumentAsync();
        await RunAsync();
        clock.UtcNow = T0.AddDays(60);
        Assert.Equal(0, await RunAsync());
        Assert.Null((await LoadAsync(id)).RemovedAt);
    }

    [Fact]
    public async Task Account_upgraded_just_before_the_run_keeps_its_documents()
    {
        var id = await AddDocumentAsync();
        await RunAsync();
        clock.UtcNow = T0.AddDays(8);
        await GrantProAsync(null);

        Assert.Equal(0, await RunAsync());
        Assert.Null((await LoadAsync(id)).RemovedAt);
    }

    [Fact]
    public async Task Grace_gives_old_documents_fresh_days_when_pro_ends()
    {
        var id = await AddDocumentAsync();
        await GrantProAsync(T0.AddDays(1));
        await RunAsync();
        clock.UtcNow = T0.AddDays(10);

        Assert.Equal(0, await RunAsync());
        clock.UtcNow = T0.AddDays(16);
        Assert.Equal(0, await RunAsync());
        clock.UtcNow = T0.AddDays(18);
        Assert.Equal(1, await RunAsync());
        Assert.NotNull((await LoadAsync(id)).RemovedAt);
    }

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }
}
