using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ArksScanner.Application.Abstractions;
using ArksScanner.Application.Plans;
using ArksScanner.Domain.Plans;
using ArksScanner.Infrastructure.Persistence;

namespace ArksScanner.Infrastructure.Ads;

/// <summary>A reward can't be issued or claimed; <see cref="Code"/> is the API error code.</summary>
public sealed class AdRewardException(string code) : Exception($"Ad reward refused: {code}.")
{
    public string Code { get; } = code;
}

/// <summary>Which ads this account may see right now.</summary>
public sealed record AdOffer(bool WebAnchor, string? AdSenseClientId, bool Rewarded, int RewardedOcrPages,
    int RewardsLeftToday, bool UnverifiedClaimAllowed);

/// <summary>
/// Rewarded ads for Free accounts: issue a one-time reward before the ad, grant it once when AdMob's signed
/// callback confirms the view, and add its pages to that day's bonus OCR allowance.
/// </summary>
public sealed class AdRewardService(AppDbContext db, PlanService plans, IClock clock, IOptions<AdsOptions> options)
{
    /// <summary>Issued rewards that never got a callback still count towards this, so the table can't be flooded.</summary>
    private const int IssuedPerGrantAllowance = 5;

    private AdsOptions Options => options.Value;

    public async Task<AdOffer> GetOfferAsync(string uid, CancellationToken ct)
    {
        var entitlements = await plans.GetEntitlementsAsync(uid, ct);
        var free = entitlements.Plan == PlanKind.Free;
        var webAnchor = free && Options.WebAnchorEnabled && !string.IsNullOrWhiteSpace(Options.AdSenseClientId);
        var rewarded = free && Options.RewardedEnabled && entitlements.OcrPagesPerDay is not null;
        var left = 0;
        if (rewarded)
        {
            var day = (await plans.GetUsageAsync(uid, ct)).Day;
            left = Math.Max(0, Options.MaxRewardsPerDay - await GrantedOnAsync(uid, day, ct));
        }
        return new(webAnchor, webAnchor ? Options.AdSenseClientId : null, rewarded && left > 0, Options.RewardedOcrPages,
            left, rewarded && Options.AllowUnverifiedRewards);
    }

    /// <summary>Starts a rewarded ad: the returned id goes into the ad's server-side verification custom data.</summary>
    public async Task<AdReward> IssueAsync(string uid, CancellationToken ct)
    {
        var entitlements = await plans.GetEntitlementsAsync(uid, ct);
        if (!Options.RewardedEnabled || entitlements.Plan != PlanKind.Free || entitlements.OcrPagesPerDay is null)
            throw new AdRewardException("ads_unavailable");
        var day = (await plans.GetUsageAsync(uid, ct)).Day;
        if (await GrantedOnAsync(uid, day, ct) >= Options.MaxRewardsPerDay)
            throw new AdRewardException("ad_reward_daily_limit");
        var issued = await db.AdRewards.CountAsync(r => r.AccountUid == uid && r.Day == day, ct);
        if (issued >= Options.MaxRewardsPerDay * IssuedPerGrantAllowance)
            throw new AdRewardException("ad_reward_daily_limit");

        var reward = AdReward.Issue(uid, day, Options.RewardedOcrPages, clock.UtcNow);
        db.AdRewards.Add(reward);
        await db.SaveChangesAsync(ct);
        return reward;
    }

    /// <summary>Grants the reward named by a signature-verified AdMob callback. Repeats of the same callback are no-ops.</summary>
    public async Task<bool> GrantVerifiedAsync(AdMobReward verified, CancellationToken ct)
    {
        if (!Guid.TryParse(verified.CustomData, out var id)) return false;
        if (await db.AdRewards.AnyAsync(r => r.TransactionId == verified.TransactionId, ct))
            return await db.AdRewards.AnyAsync(r => r.TransactionId == verified.TransactionId && r.Id == id, ct);
        var reward = await db.AdRewards.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (reward is null || reward.IsGranted || Expired(reward)) return false;
        if (!string.IsNullOrEmpty(verified.UserId) && verified.UserId != reward.AccountUid) return false;
        try
        {
            return await GrantAsync(reward, "admob", verified.TransactionId, ct);
        }
        catch (DbUpdateException)
        {
            // Two deliveries of the same callback raced; the unique transaction id let only one through.
            return false;
        }
    }

    /// <summary>Test only (Ads:AllowUnverifiedRewards): the app claims its own reward.</summary>
    public async Task ClaimUnverifiedAsync(string uid, Guid id, CancellationToken ct)
    {
        if (!Options.AllowUnverifiedRewards) throw new AdRewardException("ad_reward_unverified_disabled");
        var reward = await db.AdRewards.SingleOrDefaultAsync(r => r.Id == id && r.AccountUid == uid, ct)
            ?? throw new AdRewardException("ad_reward_not_found");
        if (reward.IsGranted) return;
        if (Expired(reward)) throw new AdRewardException("ad_reward_expired");
        await GrantAsync(reward, "unverified", null, ct);
    }

    public Task<bool> IsGrantedAsync(string uid, Guid id, CancellationToken ct) =>
        db.AdRewards.AnyAsync(r => r.Id == id && r.AccountUid == uid && r.GrantedAt != null, ct);

    private bool Expired(AdReward reward) => clock.UtcNow - reward.CreatedAt > Options.RewardLifetime;

    private Task<int> GrantedOnAsync(string uid, DateOnly day, CancellationToken ct) =>
        db.AdRewards.CountAsync(r => r.AccountUid == uid && r.Day == day && r.GrantedAt != null, ct);

    private async Task<bool> GrantAsync(AdReward reward, string grantedBy, string? transactionId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        reward.Grant(grantedBy, transactionId, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO usage_days ("AccountUid", "Day", "OcrPages", "WatermarkExports", "BonusOcrPages")
            VALUES ({reward.AccountUid}, {reward.Day}, 0, 0, {reward.Pages})
            ON CONFLICT ("AccountUid", "Day") DO UPDATE SET "BonusOcrPages" = usage_days."BonusOcrPages" + {reward.Pages}
            """, ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}
