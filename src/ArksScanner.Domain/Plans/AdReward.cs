namespace ArksScanner.Domain.Plans;

/// <summary>
/// One rewarded ad: issued to a Free account before the ad is shown, granted once when the ad network confirms
/// the view. Granting adds <see cref="Pages"/> bonus OCR pages to the account's usage day <see cref="Day"/>.
/// </summary>
public sealed class AdReward
{
    public const int MaximumTransactionIdLength = 200;

    private AdReward() { }

    public Guid Id { get; private set; }
    public string AccountUid { get; private set; } = string.Empty;
    public DateOnly Day { get; private set; }
    public int Pages { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? GrantedAt { get; private set; }

    /// <summary>The ad network's id for the view (AdMob transaction_id); unique, so a callback can't be replayed.</summary>
    public string? TransactionId { get; private set; }

    /// <summary>"admob" for a signature-verified callback, "unverified" for a test-only claim from the app.</summary>
    public string? GrantedBy { get; private set; }

    public bool IsGranted => GrantedAt is not null;

    public static AdReward Issue(string accountUid, DateOnly day, int pages, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountUid);
        ArgumentOutOfRangeException.ThrowIfLessThan(pages, 1);
        return new AdReward { Id = Guid.NewGuid(), AccountUid = accountUid, Day = day, Pages = pages, CreatedAt = now };
    }

    public void Grant(string grantedBy, string? transactionId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grantedBy);
        if (IsGranted) throw new InvalidOperationException("Reward already granted.");
        if (transactionId is { Length: > MaximumTransactionIdLength })
            throw new ArgumentOutOfRangeException(nameof(transactionId));
        GrantedAt = now;
        GrantedBy = grantedBy;
        TransactionId = transactionId;
    }
}
