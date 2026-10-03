using ArksScanner.Application.Abstractions;
using ArksScanner.Domain.Plans;

namespace ArksScanner.Application.Plans;

public sealed record PlanUsageCounts(int OcrPages, int BonusOcrPages, int WatermarkExports);

public sealed record PlanUsage(DateOnly Day, int OcrPages, int BonusOcrPages, int WatermarkExports, DateTimeOffset ResetsAt);

public interface IPlanRepository
{
    /// <summary>The plan settings row (seeded from configuration when missing), cached briefly.</summary>
    Task<PlanSettings> GetSettingsAsync(CancellationToken ct);
    Task<IReadOnlyList<Subscription>> GetSubscriptionsAsync(string uid, CancellationToken ct);
    /// <summary>Atomically adds <paramref name="amount"/> when it stays within <paramref name="limit"/> (+ bonus pages for OCR;
    /// null = unlimited). Returns the new count, or null when refused.</summary>
    Task<int?> TryConsumeAsync(string uid, DateOnly day, UsageKind kind, int amount, int? limit, CancellationToken ct);
    Task<PlanUsageCounts> GetUsageAsync(string uid, DateOnly day, CancellationToken ct);
    Task<int> CountActiveDocumentsAsync(string uid, CancellationToken ct);
}

/// <summary>A plan limit was reached. <see cref="KindCode"/> is ocr, watermark or documents.</summary>
public sealed class PlanLimitExceededException(string kindCode, int limit, int used, DateTimeOffset? resetsAt)
    : Exception($"Plan limit reached: {kindCode}.")
{
    public string KindCode { get; } = kindCode;
    public int Limit { get; } = limit;
    public int Used { get; } = used;
    public DateTimeOffset? ResetsAt { get; } = resetsAt;
}

/// <summary>The one place that decides what an account may do, and counts what it uses.</summary>
public sealed class PlanService(IPlanRepository repository, IClock clock)
{
    public async Task<Entitlements> GetEntitlementsAsync(string uid, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);
        var settings = await repository.GetSettingsAsync(ct);
        var subscriptions = await repository.GetSubscriptionsAsync(uid, ct);
        return EntitlementPolicy.Evaluate(settings, subscriptions, clock.UtcNow);
    }

    public async Task ConsumeAsync(string uid, UsageKind kind, int amount, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, 1);
        var now = clock.UtcNow;
        var settings = await repository.GetSettingsAsync(ct);
        var entitlements = EntitlementPolicy.Evaluate(settings, await repository.GetSubscriptionsAsync(uid, ct), now);
        var limit = kind == UsageKind.Ocr ? entitlements.OcrPagesPerDay : entitlements.WatermarkExportsPerDay;
        var day = settings.UsageDay(now);
        if (await repository.TryConsumeAsync(uid, day, kind, amount, limit, ct) is not null) return;

        var usage = await repository.GetUsageAsync(uid, day, ct);
        var used = kind == UsageKind.Ocr ? usage.OcrPages : usage.WatermarkExports;
        throw new PlanLimitExceededException(kind == UsageKind.Ocr ? "ocr" : "watermark",
            (limit ?? 0) + (kind == UsageKind.Ocr ? usage.BonusOcrPages : 0), used, settings.NextReset(now));
    }

    public async Task EnsureCanCreateDocumentAsync(string uid, CancellationToken ct)
    {
        var entitlements = await GetEntitlementsAsync(uid, ct);
        if (entitlements.MaxDocuments is not { } max) return;
        var count = await repository.CountActiveDocumentsAsync(uid, ct);
        if (count >= max) throw new PlanLimitExceededException("documents", max, count, null);
    }

    public async Task<PlanUsage> GetUsageAsync(string uid, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var settings = await repository.GetSettingsAsync(ct);
        var day = settings.UsageDay(now);
        var counts = await repository.GetUsageAsync(uid, day, ct);
        return new(day, counts.OcrPages, counts.BonusOcrPages, counts.WatermarkExports, settings.NextReset(now));
    }

    public Task<int> CountActiveDocumentsAsync(string uid, CancellationToken ct) => repository.CountActiveDocumentsAsync(uid, ct);
}
