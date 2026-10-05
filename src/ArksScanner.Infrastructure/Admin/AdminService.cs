using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ArksScanner.Application.Abstractions;
using ArksScanner.Application.Plans;
using ArksScanner.Domain.Plans;
using ArksScanner.Infrastructure.Persistence;

namespace ArksScanner.Infrastructure.Admin;

public sealed class AdminNotFoundException() : Exception("Not found.");
public sealed class LastAdminException() : Exception("At least one admin must remain.");

public sealed record AdminPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed record AdminUserRow(string Uid, string? Email, string Provider, bool IsGuest, DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt, string Plan);

public sealed record AdminSubscriptionRow(Guid Id, string AccountUid, string? Email, string Source, string Status,
    DateTimeOffset StartsAt, DateTimeOffset? EndsAt, string? Note, string? GrantedByUid, DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt, bool Active);

public sealed record AdminUserDetail(AdminUserRow User, bool IsAdmin, int DocumentCount, PlanUsage Usage,
    IReadOnlyList<AdminSubscriptionRow> Subscriptions);

public sealed record AdminRow(string Uid, string? Email, string? AddedByUid, DateTimeOffset AddedAt);

public sealed record AdminAuditRow(string Action, string ActorUid, string TargetType, Guid TargetId, string Details,
    DateTimeOffset OccurredAt);

/// <summary>Everything the admin portal reads and changes. Callers have already been checked as admins.</summary>
public sealed class AdminService(AppDbContext db, IPlanRepository planRepository, PlanService plans, PlanSettingsCache settingsCache,
    IAuditWriter audit, IMemoryCache cache, IClock clock)
{
    public const int PageSize = 50;
    /// <summary>Audit target for the single plan-settings row.</summary>
    public static readonly Guid SettingsTargetId = new("5e771265-0000-4000-8000-000000000001");

    public static DateTimeOffset? EndFor(string duration, DateTimeOffset from) => duration switch
    {
        "1m" => from.AddMonths(1),
        "3m" => from.AddMonths(3),
        "1y" => from.AddYears(1),
        "forever" => null,
        _ => throw new ArgumentException("Duration must be 1m, 3m, 1y or forever.", nameof(duration)),
    };

    public async Task<object> DashboardAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var settings = await planRepository.GetSettingsAsync(ct);
        var today = settings.UsageDay(now);
        var todayStart = settings.NextReset(now).AddDays(-1);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var seriesStart = today.AddDays(-29);

        var total = await db.Accounts.CountAsync(ct);
        var guests = await db.Accounts.CountAsync(a => a.IsGuest, ct);
        var active = ActiveSubscriptions(now);
        var ocrMonth = await db.UsageDays.Where(u => u.Day >= monthStart).SumAsync(u => (long)u.OcrPages, ct);
        var usage = await db.UsageDays.Where(u => u.Day >= seriesStart)
            .GroupBy(u => u.Day).Select(g => new { Day = g.Key, Ocr = g.Sum(u => u.OcrPages), Watermark = g.Sum(u => u.WatermarkExports) })
            .ToListAsync(ct);
        var recentCreated = await db.Accounts.Where(a => a.CreatedAt >= todayStart.AddDays(-30)).Select(a => a.CreatedAt).ToListAsync(ct);
        var newByDay = recentCreated.GroupBy(created => settings.UsageDay(created)).ToDictionary(g => g.Key, g => g.Count());

        return new
        {
            users = new
            {
                total, signedIn = total - guests, guests,
                @new = new
                {
                    today = await db.Accounts.CountAsync(a => a.CreatedAt >= todayStart, ct),
                    d7 = await db.Accounts.CountAsync(a => a.CreatedAt >= now.AddDays(-7), ct),
                    d30 = await db.Accounts.CountAsync(a => a.CreatedAt >= now.AddDays(-30), ct),
                },
                active7d = await db.Accounts.CountAsync(a => a.LastSeenAt >= now.AddDays(-7), ct),
            },
            subscribers = new
            {
                total = await active.Select(s => s.AccountUid).Distinct().CountAsync(ct),
                manual = await active.Where(s => s.Source == SubscriptionSource.Manual).Select(s => s.AccountUid).Distinct().CountAsync(ct),
                paid = await active.Where(s => s.Source != SubscriptionSource.Manual).Select(s => s.AccountUid).Distinct().CountAsync(ct),
            },
            documents = new
            {
                total = await db.Documents.CountAsync(ct),
                today = await db.Documents.CountAsync(d => d.CreatedAt >= todayStart, ct),
            },
            ocr = new
            {
                today = usage.Where(u => u.Day == today).Sum(u => u.Ocr),
                month = ocrMonth,
                estimatedCostMonth = Math.Round(ocrMonth / 1000m * settings.OcrCostPerThousandPages, 2),
            },
            watermarkExports = new
            {
                today = usage.Where(u => u.Day == today).Sum(u => u.Watermark),
                month = await db.UsageDays.Where(u => u.Day >= monthStart).SumAsync(u => (long)u.WatermarkExports, ct),
            },
            series = Enumerable.Range(0, 30).Select(i => seriesStart.AddDays(i)).Select(day => new
            {
                day,
                newUsers = newByDay.GetValueOrDefault(day),
                ocrPages = usage.Where(u => u.Day == day).Sum(u => u.Ocr),
            }).ToArray(),
            phase = settings.Phase.ToString(),
            effectivePhase = settings.EffectivePhase(now).ToString(),
            enforceFromUtc = settings.EnforceFromUtc,
        };
    }

    public async Task<AdminPage<AdminUserRow>> UsersAsync(string? query, int page, CancellationToken ct)
    {
        page = Math.Max(1, page);
        var accounts = db.Accounts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim().ToLowerInvariant();
            accounts = accounts.Where(a => (a.Email != null && a.Email.Contains(q)) || a.FirebaseUid == query.Trim());
        }

        var total = await accounts.CountAsync(ct);
        var rows = await accounts.OrderByDescending(a => a.CreatedAt).Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(ct);
        var plansByUid = await PlansAsync(rows.Select(a => a.FirebaseUid).ToArray(), ct);
        return new(rows.Select(a => Row(a, plansByUid[a.FirebaseUid])).ToArray(), page, PageSize, total);
    }

    public async Task<AdminUserDetail> UserAsync(string uid, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.FirebaseUid == uid, ct)
            ?? throw new AdminNotFoundException();
        var plan = (await PlansAsync([uid], ct))[uid];
        var subscriptions = await SubscriptionRows(db.Subscriptions.Where(s => s.AccountUid == uid), ct);
        return new(Row(account, plan), await db.Admins.AnyAsync(a => a.AccountUid == uid, ct),
            await plans.CountActiveDocumentsAsync(uid, ct), await plans.GetUsageAsync(uid, ct), subscriptions);
    }

    public async Task<AdminSubscriptionRow> GrantAsync(string adminUid, string uid, string duration, string? note, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var endsAt = EndFor(duration, now);
        if (!await db.Accounts.AnyAsync(a => a.FirebaseUid == uid, ct)) throw new AdminNotFoundException();
        var subscription = Subscription.GrantManual(Guid.NewGuid(), uid, now, endsAt, note, adminUid, now);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync(ct);
        await AuditAsync(adminUid, "admin.subscription_granted", "subscription", subscription.Id,
            new { accountUid = uid, subscriptionId = subscription.Id, duration }, now, ct);
        await transaction.CommitAsync(ct);
        return (await SubscriptionRows(db.Subscriptions.Where(s => s.Id == subscription.Id), ct)).Single();
    }

    public async Task<AdminSubscriptionRow> ExtendAsync(string adminUid, Guid id, string duration, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var subscription = await db.Subscriptions.SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw new AdminNotFoundException();
        var from = subscription.EndsAt is { } end && end > now ? end : now;
        subscription.Extend(EndFor(duration, from), now);
        await db.SaveChangesAsync(ct);
        await AuditAsync(adminUid, "admin.subscription_extended", "subscription", id,
            new { accountUid = subscription.AccountUid, subscriptionId = id, duration }, now, ct);
        await transaction.CommitAsync(ct);
        return (await SubscriptionRows(db.Subscriptions.Where(s => s.Id == id), ct)).Single();
    }

    public async Task RevokeAsync(string adminUid, Guid id, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var subscription = await db.Subscriptions.SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw new AdminNotFoundException();
        subscription.Revoke(adminUid, now);
        await db.SaveChangesAsync(ct);
        await AuditAsync(adminUid, "admin.subscription_revoked", "subscription", id,
            new { accountUid = subscription.AccountUid, subscriptionId = id, duration = (string?)null }, now, ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<AdminPage<AdminSubscriptionRow>> SubscriptionsAsync(int page, CancellationToken ct)
    {
        page = Math.Max(1, page);
        var total = await db.Subscriptions.CountAsync(ct);
        var rows = await SubscriptionRows(db.Subscriptions.OrderByDescending(s => s.CreatedAt).Skip((page - 1) * PageSize).Take(PageSize), ct);
        return new(rows, page, PageSize, total);
    }

    public async Task<AdminPage<object>> PaymentsAsync(int page, CancellationToken ct)
    {
        page = Math.Max(1, page);
        var total = await db.Payments.CountAsync(ct);
        var rows = await db.Payments.AsNoTracking().OrderByDescending(p => p.CreatedAt).Skip((page - 1) * PageSize).Take(PageSize)
            .Select(p => new { p.Id, p.AccountUid, p.Provider, p.ProviderReference, p.Amount, p.Currency, p.Status, p.CreatedAt, p.SubscriptionId })
            .ToListAsync(ct);
        return new(rows.Cast<object>().ToArray(), page, PageSize, total);
    }

    public async Task<PlanSettingsValues> SettingsAsync(CancellationToken ct) => (await planRepository.GetSettingsAsync(ct)).Values;

    public async Task<PlanSettingsValues> UpdateSettingsAsync(string adminUid, PlanSettingsValues values, CancellationToken ct)
    {
        await planRepository.GetSettingsAsync(ct); // seeds the row when missing
        var now = clock.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var settings = await db.PlanSettings.SingleAsync(ct);
        var old = settings.Values;
        settings.Update(values, adminUid, now);
        await db.SaveChangesAsync(ct);
        await AuditAsync(adminUid, "admin.settings_updated", "plan_settings", SettingsTargetId, new { old, @new = settings.Values }, now, ct);
        await transaction.CommitAsync(ct);
        settingsCache.Invalidate();
        return settings.Values;
    }

    public async Task<AdminPage<AdminRow>> AdminsAsync(CancellationToken ct)
    {
        var rows = await (from admin in db.Admins.AsNoTracking()
            join account in db.Accounts.AsNoTracking() on admin.AccountUid equals account.FirebaseUid into joined
            from account in joined.DefaultIfEmpty()
            orderby admin.AddedAt
            select new AdminRow(admin.AccountUid, account != null ? account.Email : null, admin.AddedByUid, admin.AddedAt)).ToListAsync(ct);
        return new(rows, 1, rows.Count, rows.Count);
    }

    public async Task<AdminRow> AddAdminAsync(string adminUid, string email, CancellationToken ct)
    {
        var normalized = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized)) throw new AdminNotFoundException();
        var matches = await db.Accounts.AsNoTracking().Where(a => a.Email == normalized && !a.IsGuest).Take(2).ToListAsync(ct);
        // Emails are only stored once verified; still refuse to guess between two accounts.
        if (matches.Count != 1) throw new AdminNotFoundException();
        var account = matches[0];
        var now = clock.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.Admins.AsNoTracking().SingleOrDefaultAsync(a => a.AccountUid == account.FirebaseUid, ct);
        if (existing is not null) return new(existing.AccountUid, account.Email, existing.AddedByUid, existing.AddedAt);
        var member = AdminMember.Create(account.FirebaseUid, adminUid, now);
        db.Admins.Add(member);
        await db.SaveChangesAsync(ct);
        await AuditAsync(adminUid, "admin.admin_added", "account", TargetFor(account.FirebaseUid), new { accountUid = account.FirebaseUid }, now, ct);
        await transaction.CommitAsync(ct);
        cache.Remove($"account-admin:{account.FirebaseUid}");
        return new(member.AccountUid, account.Email, member.AddedByUid, member.AddedAt);
    }

    public async Task RemoveAdminAsync(string adminUid, string uid, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var member = await db.Admins.SingleOrDefaultAsync(a => a.AccountUid == uid, ct) ?? throw new AdminNotFoundException();
        if (await db.Admins.CountAsync(ct) <= 1) throw new LastAdminException();
        db.Admins.Remove(member);
        await db.SaveChangesAsync(ct);
        await AuditAsync(adminUid, "admin.admin_removed", "account", TargetFor(uid), new { accountUid = uid }, now, ct);
        await transaction.CommitAsync(ct);
        cache.Remove($"account-admin:{uid}");
    }

    public async Task<IReadOnlyList<AdminAuditRow>> AuditAsync(int limit, CancellationToken ct) =>
        await db.AuditEvents.AsNoTracking().Where(e => e.Action.StartsWith("admin."))
            .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Sequence).Take(Math.Clamp(limit, 1, 100))
            .Select(e => new AdminAuditRow(e.Action, e.ActorUid, e.TargetType, e.TargetId, e.RegionJson, e.OccurredAt))
            .ToListAsync(ct);

    private IQueryable<Subscription> ActiveSubscriptions(DateTimeOffset now) => db.Subscriptions.AsNoTracking()
        .Where(s => s.Status == SubscriptionStatus.Active && s.StartsAt <= now && (s.EndsAt == null || s.EndsAt > now));

    private async Task<Dictionary<string, string>> PlansAsync(string[] uids, CancellationToken ct)
    {
        var settings = await planRepository.GetSettingsAsync(ct);
        var subscriptions = await db.Subscriptions.AsNoTracking().Where(s => uids.Contains(s.AccountUid)).ToListAsync(ct);
        var now = clock.UtcNow;
        return uids.Distinct().ToDictionary(uid => uid, uid =>
            EntitlementPolicy.Evaluate(settings, subscriptions.Where(s => s.AccountUid == uid).ToList(), now).Plan.ToString());
    }

    private static AdminUserRow Row(Account a, string plan) =>
        new(a.FirebaseUid, a.Email, a.SignInProvider, a.IsGuest, a.CreatedAt, a.LastSeenAt, plan);

    private async Task<IReadOnlyList<AdminSubscriptionRow>> SubscriptionRows(IQueryable<Subscription> query, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var rows = await (from s in query.AsNoTracking()
            join a in db.Accounts.AsNoTracking() on s.AccountUid equals a.FirebaseUid into joined
            from a in joined.DefaultIfEmpty()
            select new { s, Email = a != null ? a.Email : null }).ToListAsync(ct);
        return rows.OrderByDescending(r => r.s.CreatedAt).Select(r => new AdminSubscriptionRow(r.s.Id, r.s.AccountUid, r.Email,
            r.s.Source.ToString(), r.s.Status.ToString(), r.s.StartsAt, r.s.EndsAt, r.s.Note, r.s.GrantedByUid, r.s.CreatedAt,
            r.s.RevokedAt, r.s.IsActiveAt(now))).ToArray();
    }

    /// <summary>Appends the audit event inside the caller's transaction (the writer only stages it there).</summary>
    private async Task AuditAsync(string actor, string action, string targetType, Guid targetId, object details, DateTimeOffset now,
        CancellationToken ct)
    {
        await audit.AppendAsync(new AuditWriteRequest(actor, action, targetType, targetId, JsonSerializer.Serialize(details), now), ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Accounts are keyed by Firebase UID; the audit chain needs a GUID, so derive a stable one.</summary>
    private static Guid TargetFor(string uid) => new(MD5.HashData(Encoding.UTF8.GetBytes("account:" + uid)));
}
