using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ArksScanner.Application.Abstractions;
using ArksScanner.Application.Plans;
using ArksScanner.Domain.Plans;
using ArksScanner.Infrastructure.Persistence;

namespace ArksScanner.Infrastructure.Processing;

/// <summary>Removes Free-plan documents older than the retention window (only while plans are enforced).</summary>
public sealed class DocumentRetention(AppDbContext db, PlanService plans, IAuditWriter audit, IClock clock)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var removed = 0;
        var owners = await db.Accounts.AsNoTracking()
            .Where(a => db.Documents.Any(d => d.OwnerFirebaseUid == a.FirebaseUid))
            .Select(a => a.FirebaseUid).ToListAsync(ct);
        foreach (var uid in owners)
        {
            db.ChangeTracker.Clear();
            var now = clock.UtcNow;
            var entitlements = await plans.GetEntitlementsAsync(uid, ct);
            var account = await db.Accounts.SingleAsync(a => a.FirebaseUid == uid, ct);
            // Pro ending (or plans becoming enforced) starts a fresh retention window before anything is judged.
            if (account.RecordPlan(entitlements.Plan, entitlements.Phase, now)) await db.SaveChangesAsync(ct);
            if (entitlements.RetentionDays is not { } days) continue;

            var cutoff = now.AddDays(-days);
            if (account.RetentionGraceFrom is { } grace && grace > cutoff) continue;
            var expired = await db.Documents.AsNoTracking()
                .Where(d => d.OwnerFirebaseUid == uid && d.CreatedAt <= cutoff)
                .Select(d => d.Id).ToListAsync(ct);
            foreach (var id in expired)
                if (await RemoveAsync(uid, id, ct)) removed++;
        }

        return removed;
    }

    private async Task<bool> RemoveAsync(string uid, Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var documents = new EfDocumentRepository(db);
        await using var transaction = await documents.BeginTransactionAsync(ct);
        var document = await documents.FindOwnedForUpdateAsync(uid, id, ct);
        if (document is null || document.RemovedAt is not null) return false;
        // Re-check inside the lock: an upgrade that landed after the scan keeps the document.
        var now = clock.UtcNow;
        var entitlements = await plans.GetEntitlementsAsync(uid, ct);
        if (entitlements.RetentionDays is not { } days) return false;
        var grace = await db.Accounts.Where(a => a.FirebaseUid == uid).Select(a => a.RetentionGraceFrom).SingleAsync(ct);
        var start = grace is { } g && g > document.CreatedAt ? g : document.CreatedAt;
        if (start.AddDays(days) > now) return false;

        document.Remove("retention", now);
        await audit.AppendAsync(new AuditWriteRequest(uid, "document.expired_removed", "document", document.Id,
            JsonSerializer.Serialize(new { retentionDays = days }), now), ct);
        await documents.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}
