namespace ArksScanner.Infrastructure.Ads;

/// <summary>
/// Ad switches (Railway variables Ads__*). Ads are only for Free accounts; Pro never sees them.
/// Everything is off by default.
/// </summary>
public sealed class AdsOptions
{
    public const string SectionName = "Ads";

    /// <summary>Website: Google AdSense anchor bar for Free accounts (needs an approved AdSense site).</summary>
    public bool WebAnchorEnabled { get; init; }

    /// <summary>AdSense publisher id, e.g. ca-pub-1234567890123456.</summary>
    public string? AdSenseClientId { get; init; }

    /// <summary>Mobile: AdMob rewarded ads that unlock bonus OCR pages when a Free account hits its daily limit.</summary>
    public bool RewardedEnabled { get; init; }

    public int RewardedOcrPages { get; init; } = 5;

    public int MaxRewardsPerDay { get; init; } = 3;

    /// <summary>
    /// Test only: lets the app claim a reward itself after the ad's reward event. Google's test ad units never
    /// call our verification URL, so this is the only way to try the flow with them. Never enable in production:
    /// a modified app could then claim rewards without watching ads.
    /// </summary>
    public bool AllowUnverifiedRewards { get; init; }

    /// <summary>An issued reward expires if no verified callback arrives in this time.</summary>
    public TimeSpan RewardLifetime { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Google's public keys for AdMob server-side verification signatures.</summary>
    public string AdMobVerifierKeysUrl { get; init; } = "https://www.gstatic.com/admob/reward/verifier-keys.json";
}
