using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ArksScanner.Application.Abstractions;
using ArksScanner.Application.Plans;
using ArksScanner.Domain.Plans;
using ArksScanner.Infrastructure.Ads;
using ArksScanner.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace ArksScanner.Infrastructure.IntegrationTests.Ads;

public sealed class AdRewardServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 25, 2, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly MutableClock clock = new(T0);
    private AdsOptions options = new() { RewardedEnabled = true, RewardedOcrPages = 5, MaxRewardsPerDay = 2 };

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await using var db = Db();
        await db.Database.MigrateAsync();
        db.Accounts.Add(Account.Create("u1", "u1@example.test", "password", false, T0));
        db.Accounts.Add(Account.Create("u2", "u2@example.test", "password", false, T0));
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task A_verified_callback_adds_bonus_pages_once_even_when_google_retries()
    {
        var reward = await Service().IssueAsync("u1", default);

        Assert.True(await Service().GrantVerifiedAsync(new AdMobReward("tx-1", reward.Id.ToString(), "u1"), default));
        Assert.True(await Service().GrantVerifiedAsync(new AdMobReward("tx-1", reward.Id.ToString(), "u1"), default));

        Assert.Equal(5, await BonusAsync("u1"));
        Assert.True(await Service().IsGrantedAsync("u1", reward.Id, default));
    }

    [Fact]
    public async Task Callbacks_for_unknown_expired_or_someone_elses_rewards_grant_nothing()
    {
        var reward = await Service().IssueAsync("u1", default);

        Assert.False(await Service().GrantVerifiedAsync(new AdMobReward("tx-a", Guid.NewGuid().ToString(), "u1"), default));
        Assert.False(await Service().GrantVerifiedAsync(new AdMobReward("tx-b", reward.Id.ToString(), "u2"), default));
        clock.UtcNow = T0.AddHours(2);
        Assert.False(await Service().GrantVerifiedAsync(new AdMobReward("tx-c", reward.Id.ToString(), "u1"), default));

        Assert.Equal(0, await BonusAsync("u1"));
    }

    [Fact]
    public async Task One_transaction_cannot_grant_two_rewards()
    {
        var first = await Service().IssueAsync("u1", default);
        var second = await Service().IssueAsync("u1", default);

        Assert.True(await Service().GrantVerifiedAsync(new AdMobReward("tx-1", first.Id.ToString(), null), default));
        Assert.False(await Service().GrantVerifiedAsync(new AdMobReward("tx-1", second.Id.ToString(), null), default));

        Assert.Equal(5, await BonusAsync("u1"));
    }

    [Fact]
    public async Task The_daily_cap_stops_new_rewards_and_the_offer_counts_down()
    {
        Assert.Equal(2, (await Service().GetOfferAsync("u1", default)).RewardsLeftToday);
        for (var i = 0; i < 2; i++)
        {
            var reward = await Service().IssueAsync("u1", default);
            await Service().GrantVerifiedAsync(new AdMobReward($"tx-{i}", reward.Id.ToString(), null), default);
        }

        var offer = await Service().GetOfferAsync("u1", default);
        Assert.False(offer.Rewarded);
        Assert.Equal(0, offer.RewardsLeftToday);
        Assert.Equal("ad_reward_daily_limit",
            (await Assert.ThrowsAsync<AdRewardException>(() => Service().IssueAsync("u1", default))).Code);
        Assert.Equal(10, await BonusAsync("u1"));
    }

    [Fact]
    public async Task Pro_accounts_and_switched_off_ads_get_no_rewarded_ads()
    {
        await using (var db = Db())
        {
            db.Subscriptions.Add(Subscription.GrantManual(Guid.NewGuid(), "u2", T0, null, null, "admin", T0));
            await db.SaveChangesAsync();
        }
        Assert.False((await Service().GetOfferAsync("u2", default)).Rewarded);
        Assert.Equal("ads_unavailable",
            (await Assert.ThrowsAsync<AdRewardException>(() => Service().IssueAsync("u2", default))).Code);

        options = new AdsOptions { RewardedEnabled = false };
        Assert.Equal("ads_unavailable",
            (await Assert.ThrowsAsync<AdRewardException>(() => Service().IssueAsync("u1", default))).Code);
    }

    [Fact]
    public async Task The_app_can_claim_its_own_reward_only_when_unverified_rewards_are_switched_on()
    {
        var reward = await Service().IssueAsync("u1", default);
        Assert.Equal("ad_reward_unverified_disabled", (await Assert.ThrowsAsync<AdRewardException>(
            () => Service().ClaimUnverifiedAsync("u1", reward.Id, default))).Code);

        options = new AdsOptions { RewardedEnabled = true, RewardedOcrPages = 5, MaxRewardsPerDay = 2, AllowUnverifiedRewards = true };
        Assert.Equal("ad_reward_not_found", (await Assert.ThrowsAsync<AdRewardException>(
            () => Service().ClaimUnverifiedAsync("u2", reward.Id, default))).Code);
        await Service().ClaimUnverifiedAsync("u1", reward.Id, default);
        await Service().ClaimUnverifiedAsync("u1", reward.Id, default);

        Assert.Equal(5, await BonusAsync("u1"));
    }

    [Fact]
    public async Task Web_anchor_ads_need_the_switch_a_publisher_id_and_a_free_account()
    {
        Assert.False((await Service().GetOfferAsync("u1", default)).WebAnchor);
        options = new AdsOptions { WebAnchorEnabled = true, AdSenseClientId = "ca-pub-1234567890123456" };
        var offer = await Service().GetOfferAsync("u1", default);
        Assert.True(offer.WebAnchor);
        Assert.Equal("ca-pub-1234567890123456", offer.AdSenseClientId);
    }

    private AdRewardService Service()
    {
        var db = Db();
        var repository = new EfPlanRepository(db, new PlanSettingsCache(), clock,
            new PlanSeed(new PlanSettingsValues(PlanPhase.Enforced, null, 5, 3, 30, 7, "Asia/Kuala_Lumpur", 1.5m)));
        return new AdRewardService(db, new PlanService(repository, clock), clock, Options.Create(options));
    }

    private async Task<int> BonusAsync(string uid)
    {
        await using var db = Db();
        return await db.UsageDays.Where(u => u.AccountUid == uid).SumAsync(u => u.BonusOcrPages);
    }

    private AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }
}
