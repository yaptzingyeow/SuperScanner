using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ArksScanner.Application.Abstractions;
using ArksScanner.Application.Plans;
using ArksScanner.Infrastructure.Persistence;

namespace ArksScanner.Infrastructure.Privacy;

public sealed class AccountDeletionBlockedException() : Exception("The last admin cannot delete their account.");

/// <summary>A person's own data: export it, delete it, and record privacy consent (GDPR/PDPA style rights).</summary>
public sealed class PrivacyService(AppDbContext db, IIdentityAccountDeleter identities, IAuditWriter audit,
    IMemoryCache cache, IClock clock)
{
    /// <summary>Everything stored about the person, as one JSON-friendly object.</summary>
    public async Task<object> ExportAsync(string uid, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.FirebaseUid == uid, ct);
        var documents = await db.Documents.AsNoTracking().Where(d => d.OwnerFirebaseUid == uid)
            .OrderBy(d => d.CreatedAt).Select(d => new { d.Id, d.Title, d.Status, d.CreatedAt, d.UpdatedAt }).ToListAsync(ct);
        var ids = documents.Select(d => d.Id).ToArray();
        var pages = await db.Pages.AsNoTracking().Where(p => ids.Contains(p.DocumentId) && p.RemovedAt == null)
            .OrderBy(p => p.Position).Select(p => new { p.Id, p.DocumentId, p.Position, p.CreatedAt }).ToListAsync(ct);
        var pageIds = pages.Select(p => p.Id).ToArray();
        var text = await db.PageOcrResults.AsNoTracking()
            .Where(r => pageIds.Contains(r.PageId) && r.FullText != null)
            .OrderByDescending(r => r.CompletedAt)
            .Select(r => new { r.PageId, r.Language, r.FullText }).ToListAsync(ct);
        return new
        {
            exportedAt = clock.UtcNow,
            account = account is null ? null : new
            {
                uid = account.FirebaseUid, account.Email, account.SignInProvider, account.IsGuest, account.CreatedAt,
                account.LastSeenAt, account.TimeZone, account.Locale, account.PrivacyConsentVersion, account.PrivacyConsentedAt,
            },
            subscriptions = await db.Subscriptions.AsNoTracking().Where(s => s.AccountUid == uid)
                .Select(s => new { s.Id, Source = s.Source.ToString(), Status = s.Status.ToString(), s.StartsAt, s.EndsAt, s.Note })
                .ToListAsync(ct),
            usage = await db.UsageDays.AsNoTracking().Where(u => u.AccountUid == uid).OrderBy(u => u.Day)
                .Select(u => new { u.Day, u.OcrPages, u.WatermarkExports }).ToListAsync(ct),
            documents = documents.Select(d => new
            {
                d.Id, d.Title, Status = d.Status.ToString(), d.CreatedAt, d.UpdatedAt,
                pages = pages.Where(p => p.DocumentId == d.Id).Select(p => new
                {
                    p.Position, p.CreatedAt,
                    recognizedText = text.FirstOrDefault(t => t.PageId == p.Id)?.FullText,
                    language = text.FirstOrDefault(t => t.PageId == p.Id)?.Language,
                }),
            }),
            activity = await db.AuditEvents.AsNoTracking().Where(e => e.ActorUid == uid).OrderBy(e => e.OccurredAt)
                .Select(e => new { e.Action, e.TargetType, e.OccurredAt }).ToListAsync(ct),
        };
    }

    public async Task RecordConsentAsync(string uid, string version, CancellationToken ct)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.FirebaseUid == uid, ct);
        if (account is null) return;
        account.RecordPrivacyConsent(version, clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Removes the person's documents, admin rights and account, then their sign-in.</summary>
    public async Task DeleteAccountAsync(string uid, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await using (var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct))
        {
            if (await db.Admins.AnyAsync(a => a.AccountUid == uid, ct) && await db.Admins.CountAsync(ct) <= 1)
                throw new AccountDeletionBlockedException();
            var documents = await db.Documents.Include(d => d.Pages).Where(d => d.OwnerFirebaseUid == uid).ToListAsync(ct);
            foreach (var document in documents) document.Remove("account_deleted", now);
            db.Admins.RemoveRange(await db.Admins.Where(a => a.AccountUid == uid).ToListAsync(ct));
            var account = await db.Accounts.SingleOrDefaultAsync(a => a.FirebaseUid == uid, ct);
            if (account is not null) db.Accounts.Remove(account);
            await db.SaveChangesAsync(ct);
            await audit.AppendAsync(new AuditWriteRequest(uid, "account.deleted", "account", TargetFor(uid),
                System.Text.Json.JsonSerializer.Serialize(new { documents = documents.Count }), now), ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        cache.Remove($"account-admin:{uid}");
        await identities.DeleteAsync(uid, ct);
    }

    private static Guid TargetFor(string uid) =>
        new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("account:" + uid)));
}
