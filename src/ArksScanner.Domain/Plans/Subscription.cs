namespace ArksScanner.Domain.Plans;

public enum SubscriptionSource { Manual, HitPay, AppStore, GooglePlay }
public enum SubscriptionStatus { Active, Revoked, Ended }

/// <summary>One Pro period for an account (manual grant now; paid sources later).</summary>
public sealed class Subscription
{
    public const int MaximumNoteLength = 200;

    private Subscription() { }

    public Guid Id { get; private set; }
    public string AccountUid { get; private set; } = string.Empty;
    public SubscriptionSource Source { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    /// <summary>Null means forever.</summary>
    public DateTimeOffset? EndsAt { get; private set; }
    public string? Note { get; private set; }
    public string? GrantedByUid { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedByUid { get; private set; }

    public static Subscription GrantManual(Guid id, string accountUid, DateTimeOffset startsAt, DateTimeOffset? endsAt,
        string? note, string grantedByUid, DateTimeOffset now)
    {
        if (id == Guid.Empty) throw new ArgumentException("An ID is required.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(accountUid);
        ArgumentException.ThrowIfNullOrWhiteSpace(grantedByUid);
        if (endsAt is { } end && end <= startsAt) throw new ArgumentException("The end must be after the start.", nameof(endsAt));
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed?.Length > MaximumNoteLength) throw new ArgumentException("The note is too long.", nameof(note));
        return new Subscription
        {
            Id = id, AccountUid = accountUid, Source = SubscriptionSource.Manual, Status = SubscriptionStatus.Active,
            StartsAt = startsAt, EndsAt = endsAt, Note = trimmed, GrantedByUid = grantedByUid, CreatedAt = now,
        };
    }

    public bool IsActiveAt(DateTimeOffset now) =>
        Status == SubscriptionStatus.Active && StartsAt <= now && (EndsAt is null || now < EndsAt);

    public void Revoke(string adminUid, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adminUid);
        if (Status != SubscriptionStatus.Active) throw new InvalidOperationException("Only an active subscription can be revoked.");
        Status = SubscriptionStatus.Revoked;
        RevokedAt = now;
        RevokedByUid = adminUid;
    }

    /// <summary>Moves the end later; null makes it forever.</summary>
    public void Extend(DateTimeOffset? newEndsAt, DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.Active) throw new InvalidOperationException("Only an active subscription can be extended.");
        if (EndsAt is null) throw new ArgumentException("This subscription is already forever.", nameof(newEndsAt));
        if (newEndsAt is { } end && end <= EndsAt) throw new ArgumentException("The new end must be later.", nameof(newEndsAt));
        EndsAt = newEndsAt;
    }
}
