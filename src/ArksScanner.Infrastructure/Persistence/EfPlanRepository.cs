using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ArksScanner.Application.Abstractions;
using ArksScanner.Application.Plans;
using ArksScanner.Domain.Plans;

namespace ArksScanner.Infrastructure.Persistence;

/// <summary>Process-wide copy of the plan settings, refreshed every 30 seconds (register as a singleton).</summary>
public sealed class PlanSettingsCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);
    private readonly object gate = new();
    private (PlanSettings Settings, DateTimeOffset LoadedAt)? entry;

    public bool TryGet(DateTimeOffset now, out PlanSettings settings)
    {
        lock (gate)
        {
            if (entry is { } e && now - e.LoadedAt < Lifetime) { settings = e.Settings; return true; }
            settings = null!;
            return false;
        }
    }

    public void Set(PlanSettings settings, DateTimeOffset now) { lock (gate) entry = (settings, now); }
    public void Invalidate() { lock (gate) entry = null; }
}

/// <summary>Starting plan settings, used only to create the settings row the first time.</summary>
public sealed record PlanSeed(PlanSettingsValues Values)
{
    public static PlanSeed FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Plans");
        return new(new PlanSettingsValues(
            Enum.TryParse<PlanPhase>(section["Phase"], out var phase) ? phase : PlanPhase.Test,
            DateTimeOffset.TryParse(section["EnforceFromUtc"], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var from) ? from : null,
            Int(section["FreeOcrPagesPerDay"], 5),
            Int(section["FreeWatermarkExportsPerDay"], 3),
            Int(section["FreeMaxDocuments"], 30),
            Int(section["FreeRetentionDays"], 7),
            section["UsageTimeZone"] ?? "Asia/Kuala_Lumpur",
            decimal.TryParse(section["OcrCostPerThousandPages"], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var cost) ? cost : 1.50m));
    }

    private static int Int(string? value, int fallback) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : fallback;
}

public sealed class EfPlanRepository(AppDbContext db, PlanSettingsCache cache, IClock clock, PlanSeed seed)
    : IPlanRepository
{
    public async Task<PlanSettings> GetSettingsAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (cache.TryGet(now, out var cached)) return cached;
        var settings = await db.PlanSettings.AsNoTracking().SingleOrDefaultAsync(ct);
        if (settings is null)
        {
            db.PlanSettings.Add(PlanSettings.Seed(seed.Values, now));
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { /* another instance seeded it first */ }
            db.ChangeTracker.Clear();
            settings = await db.PlanSettings.AsNoTracking().SingleAsync(ct);
        }

        cache.Set(settings, now);
        return settings;
    }

    public async Task<IReadOnlyList<Subscription>> GetSubscriptionsAsync(string uid, CancellationToken ct) =>
        await db.Subscriptions.AsNoTracking()
            .Where(s => s.AccountUid == uid && s.Status == SubscriptionStatus.Active).ToListAsync(ct);

    public async Task<int?> TryConsumeAsync(string uid, DateOnly day, UsageKind kind, int amount, int? limit, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, 1);
        var ocr = kind == UsageKind.Ocr;
        // A brand-new day row has no bonus yet, so the first use only needs amount <= limit.
        if (limit is { } max && amount > max)
        {
            var existing = await GetUsageAsync(uid, day, ct);
            var current = ocr ? existing.OcrPages : existing.WatermarkExports;
            if (current + amount > max + (ocr ? existing.BonusOcrPages : 0)) return null;
        }

        var rows = ocr
            ? db.Database.SqlQuery<int>($"""
                INSERT INTO usage_days ("AccountUid", "Day", "OcrPages", "WatermarkExports", "BonusOcrPages")
                VALUES ({uid}, {day}, {amount}, 0, 0)
                ON CONFLICT ("AccountUid", "Day") DO UPDATE SET "OcrPages" = usage_days."OcrPages" + {amount}
                WHERE {limit}::integer IS NULL OR usage_days."OcrPages" + {amount} <= {limit}::integer + usage_days."BonusOcrPages"
                RETURNING "OcrPages" AS "Value"
                """)
            : db.Database.SqlQuery<int>($"""
                INSERT INTO usage_days ("AccountUid", "Day", "OcrPages", "WatermarkExports", "BonusOcrPages")
                VALUES ({uid}, {day}, 0, {amount}, 0)
                ON CONFLICT ("AccountUid", "Day") DO UPDATE SET "WatermarkExports" = usage_days."WatermarkExports" + {amount}
                WHERE {limit}::integer IS NULL OR usage_days."WatermarkExports" + {amount} <= {limit}::integer
                RETURNING "WatermarkExports" AS "Value"
                """);
        var result = await rows.ToListAsync(ct);
        return result.Count == 0 ? null : result[0];
    }

    public async Task<PlanUsageCounts> GetUsageAsync(string uid, DateOnly day, CancellationToken ct)
    {
        var row = await db.UsageDays.AsNoTracking().SingleOrDefaultAsync(u => u.AccountUid == uid && u.Day == day, ct);
        return row is null ? new(0, 0, 0) : new(row.OcrPages, row.BonusOcrPages, row.WatermarkExports);
    }

    public Task<int> CountActiveDocumentsAsync(string uid, CancellationToken ct) =>
        db.Documents.CountAsync(d => d.OwnerFirebaseUid == uid && d.RemovedAt == null, ct);
}
