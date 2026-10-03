using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ArksScanner.Domain.Documents;
using ArksScanner.Domain.Plans;
using ArksScanner.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace ArksScanner.Infrastructure.IntegrationTests.Persistence;

public sealed class PlansPersistenceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 6, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => postgres.StartAsync();
    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    private DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;

    [Fact]
    public async Task Migration_backfills_one_account_per_existing_document_owner()
    {
        await using (var old = new AppDbContext(Options()))
        {
            await old.GetService<IMigrator>().MigrateAsync("20261001040649_PageRotation");
            foreach (var owner in new[] { "u1", "u1", "u2" })
                await old.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO documents ("Id","OwnerFirebaseUid","Title","Status","Revision","PageOrderRevision","CreatedAt","UpdatedAt")
                    VALUES ({Guid.NewGuid()}, {owner}, 'Doc', 'Ready', 1, 1, {Now}, {Now})
                    """);
        }

        await using var upgraded = new AppDbContext(Options());
        await upgraded.Database.MigrateAsync();

        var accounts = await upgraded.Accounts.OrderBy(a => a.FirebaseUid).ToListAsync();
        Assert.Equal(["u1", "u2"], accounts.Select(a => a.FirebaseUid));
        Assert.All(accounts, a => Assert.Equal("unknown", a.SignInProvider));
    }

    [Fact]
    public async Task Plan_entities_round_trip()
    {
        await using (var db = new AppDbContext(Options()))
        {
            await db.Database.MigrateAsync();
            db.Accounts.Add(Account.Create("u1", "A@B.com", "google.com", false, Now));
            db.Admins.Add(AdminMember.Create("u1", null, Now));
            db.Subscriptions.Add(Subscription.GrantManual(Guid.NewGuid(), "u1", Now, null, "friend", "u1", Now));
            db.PlanSettings.Add(PlanSettings.Seed(new PlanSettingsValues(PlanPhase.Test, null, 5, 3, 30, 7, "Asia/Kuala_Lumpur", 1.5m), Now));
            await db.SaveChangesAsync();
        }

        await using var verify = new AppDbContext(Options());
        Assert.Equal("a@b.com", (await verify.Accounts.SingleAsync()).Email);
        Assert.Null((await verify.Subscriptions.SingleAsync()).EndsAt);
        Assert.Equal(PlanPhase.Test, (await verify.PlanSettings.SingleAsync()).Phase);
        Assert.Single(await verify.Admins.ToListAsync());
        Assert.Empty(await verify.UsageDays.ToListAsync());
        Assert.Empty(await verify.Payments.ToListAsync());
    }

    [Fact]
    public async Task Only_one_plan_settings_row_is_allowed()
    {
        await using var db = new AppDbContext(Options());
        await db.Database.MigrateAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO plan_settings ("Id","Phase","FreeOcrPagesPerDay","FreeWatermarkExportsPerDay","FreeMaxDocuments",
              "FreeRetentionDays","UsageTimeZone","OcrCostPerThousandPages","UpdatedAt")
            VALUES (2,'Test',5,3,30,7,'Asia/Kuala_Lumpur',1.5,now())
            """));
    }

    [Fact]
    public async Task Removed_documents_keep_their_rows_and_pages()
    {
        var document = Document.Create(Guid.NewGuid(), "u1", "Doc", Now);
        document.AddPage(Guid.NewGuid(), 10, Now);
        await using (var db = new AppDbContext(Options()))
        {
            await db.Database.MigrateAsync();
            db.Documents.Add(document);
            await db.SaveChangesAsync();
            document.Remove("retention", Now.AddDays(8));
            await db.SaveChangesAsync();
        }

        await using var verify = new AppDbContext(Options());
        var stored = await verify.Documents.Include(d => d.Pages).SingleAsync();
        Assert.Equal(Now.AddDays(8), stored.RemovedAt);
        Assert.Equal("retention", stored.RemovedReason);
        Assert.Empty(stored.ActivePages);
        Assert.Single(stored.Pages);
    }
}
