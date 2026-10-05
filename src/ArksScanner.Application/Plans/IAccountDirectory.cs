namespace ArksScanner.Application.Plans;

/// <summary>Accounts the app has seen, and which of them are admins.</summary>
public interface IAccountDirectory
{
    /// <summary>Creates or refreshes the account (throttled to one write per 10 minutes unless identity changed);
    /// makes the configured owner the first admin while there are no admins.</summary>
    Task TouchAsync(string uid, string? email, string? provider, bool isGuest, CancellationToken ct,
        string? timeZone = null, string? locale = null);

    /// <summary>True for a non-guest account listed in admins (cached for 30 seconds).</summary>
    Task<bool> IsAdminAsync(string uid, CancellationToken ct);
}
