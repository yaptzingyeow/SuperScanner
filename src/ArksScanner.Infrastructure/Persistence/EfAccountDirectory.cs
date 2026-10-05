using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using ArksScanner.Application.Abstractions;
using ArksScanner.Application.Plans;
using ArksScanner.Domain.Plans;

namespace ArksScanner.Infrastructure.Persistence;

public sealed class EfAccountDirectory(AppDbContext db, IMemoryCache cache, IClock clock, IConfiguration configuration)
    : IAccountDirectory
{
    private static readonly TimeSpan AdminCacheTime = TimeSpan.FromSeconds(30);

    public async Task TouchAsync(string uid, string? email, string? provider, bool isGuest, CancellationToken ct,
        string? timeZone = null, string? locale = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        var key = $"account-touch:{uid}:{email}:{isGuest}:{timeZone}:{locale}";
        if (cache.TryGetValue(key, out _)) return;
        var now = clock.UtcNow;
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.FirebaseUid == uid, ct);
        if (account is null)
        {
            account = Account.Create(uid, email, provider ?? "unknown", isGuest, now);
            db.Accounts.Add(account);
        }
        else
        {
            account.Touch(email, provider ?? account.SignInProvider, isGuest, now);
        }
        account.SetRegion(timeZone, locale, now);

        // Seed even when the visit itself is not worth saving (seen < 10 minutes ago).
        await SeedOwnerAdminAsync(account, now, ct);
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
        cache.Set(key, true, Account.TouchInterval);
    }

    public async Task<bool> IsAdminAsync(string uid, CancellationToken ct) =>
        await cache.GetOrCreateAsync($"account-admin:{uid}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = AdminCacheTime;
            return await db.Admins.AnyAsync(a => a.AccountUid == uid, ct) &&
                await db.Accounts.AnyAsync(a => a.FirebaseUid == uid && !a.IsGuest, ct);
        });

    private async Task SeedOwnerAdminAsync(Account account, DateTimeOffset now, CancellationToken ct)
    {
        var owner = configuration["Admin:OwnerEmail"]?.Trim();
        if (string.IsNullOrEmpty(owner) || account.IsGuest ||
            !string.Equals(account.Email, owner, StringComparison.OrdinalIgnoreCase) ||
            await db.Admins.AnyAsync(ct))
            return;
        db.Admins.Add(AdminMember.Create(account.FirebaseUid, null, now));
        cache.Remove($"account-admin:{account.FirebaseUid}");
    }
}
