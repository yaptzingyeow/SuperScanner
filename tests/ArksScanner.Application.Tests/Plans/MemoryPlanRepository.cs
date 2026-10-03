using ArksScanner.Application.Plans;
using ArksScanner.Domain.Plans;

namespace ArksScanner.Application.Tests.Plans;

/// <summary>In-memory plan store for handler tests.</summary>
internal sealed class MemoryPlanRepository(PlanSettingsValues values) : IPlanRepository
{
    public static PlanSettingsValues Enforced(int ocr = 5, int watermark = 3, int documents = 30) =>
        new(PlanPhase.Enforced, null, ocr, watermark, documents, 7, "Asia/Kuala_Lumpur", 1.5m);

    public Dictionary<(string Uid, DateOnly Day), (int Ocr, int Watermark)> Usage { get; } = [];
    public List<Subscription> Subscriptions { get; } = [];
    public int ActiveDocuments { get; set; }

    public Task<PlanSettings> GetSettingsAsync(CancellationToken ct) =>
        Task.FromResult(PlanSettings.Seed(values, DateTimeOffset.UnixEpoch));

    public Task<IReadOnlyList<Subscription>> GetSubscriptionsAsync(string uid, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Subscription>>(Subscriptions.Where(s => s.AccountUid == uid).ToList());

    public Task<int?> TryConsumeAsync(string uid, DateOnly day, UsageKind kind, int amount, int? limit, CancellationToken ct)
    {
        var current = Usage.GetValueOrDefault((uid, day));
        var used = kind == UsageKind.Ocr ? current.Ocr : current.Watermark;
        if (limit is { } max && used + amount > max) return Task.FromResult<int?>(null);
        Usage[(uid, day)] = kind == UsageKind.Ocr ? (current.Ocr + amount, current.Watermark) : (current.Ocr, current.Watermark + amount);
        return Task.FromResult<int?>(used + amount);
    }

    public Task<PlanUsageCounts> GetUsageAsync(string uid, DateOnly day, CancellationToken ct)
    {
        var current = Usage.GetValueOrDefault((uid, day));
        return Task.FromResult(new PlanUsageCounts(current.Ocr, 0, current.Watermark));
    }

    public Task<int> CountActiveDocumentsAsync(string uid, CancellationToken ct) => Task.FromResult(ActiveDocuments);

    public Task<(DateTimeOffset? GraceFrom, PlanKind? LastPlan)> GetRetentionStateAsync(string uid, CancellationToken ct) =>
        Task.FromResult<(DateTimeOffset?, PlanKind?)>((null, null));

    public int Total(UsageKind kind) => Usage.Values.Sum(v => kind == UsageKind.Ocr ? v.Ocr : v.Watermark);
}
