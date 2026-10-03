namespace ArksScanner.Domain.Plans;

/// <summary>A signed-in identity (Firebase UID) the app has seen; one per person across web and mobile.</summary>
public sealed class Account
{
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(10);

    private Account() { }

    public string FirebaseUid { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string SignInProvider { get; private set; } = "unknown";
    public bool IsGuest { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    /// <summary>Free-plan retention counts from this instant (set when Pro ends or plans become enforced).</summary>
    public DateTimeOffset? RetentionGraceFrom { get; private set; }
    /// <summary>The plan last computed for this account, used to detect Pro ending.</summary>
    public PlanKind? LastPlan { get; private set; }

    public static Account Create(string uid, string? email, string provider, bool isGuest, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        return new Account
        {
            FirebaseUid = uid, Email = Normalize(email), SignInProvider = string.IsNullOrWhiteSpace(provider) ? "unknown" : provider,
            IsGuest = isGuest, CreatedAt = now, LastSeenAt = now,
        };
    }

    /// <summary>Records a visit; true when something worth saving changed (identity, or 10+ minutes since the last save).</summary>
    public bool Touch(string? email, string provider, bool isGuest, DateTimeOffset now)
    {
        var normalized = Normalize(email);
        var changed = normalized != Email || isGuest != IsGuest ||
            (!string.IsNullOrWhiteSpace(provider) && provider != SignInProvider);
        if (!changed && now - LastSeenAt < TouchInterval) return false;
        Email = normalized;
        IsGuest = isGuest;
        if (!string.IsNullOrWhiteSpace(provider)) SignInProvider = provider;
        LastSeenAt = now;
        return true;
    }

    /// <summary>Remembers the plan; starts a retention grace when the account drops to Free.</summary>
    public bool RecordPlan(PlanKind plan, PlanPhase phase, DateTimeOffset now)
    {
        var becameLimited = phase == PlanPhase.Enforced && plan == PlanKind.Free && LastPlan != PlanKind.Free;
        if (LastPlan == plan && !becameLimited) return false;
        if (becameLimited) RetentionGraceFrom = now;
        LastPlan = phase == PlanPhase.Enforced ? plan : null;
        return true;
    }

    private static string? Normalize(string? email) => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
}

public sealed class AdminMember
{
    private AdminMember() { }

    public string AccountUid { get; private set; } = string.Empty;
    public string? AddedByUid { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }

    public static AdminMember Create(string accountUid, string? addedByUid, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountUid);
        return new AdminMember { AccountUid = accountUid, AddedByUid = addedByUid, AddedAt = now };
    }
}

public sealed class UsageDay
{
    private UsageDay() { }

    public string AccountUid { get; private set; } = string.Empty;
    public DateOnly Day { get; private set; }
    public int OcrPages { get; private set; }
    public int WatermarkExports { get; private set; }
    public int BonusOcrPages { get; private set; }
}

public sealed class Payment
{
    private Payment() { }

    public Guid Id { get; private set; }
    public string AccountUid { get; private set; } = string.Empty;
    public string Provider { get; private set; } = string.Empty;
    public string ProviderReference { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? SubscriptionId { get; private set; }
}
