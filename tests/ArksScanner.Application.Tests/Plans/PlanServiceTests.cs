using ArksScanner.Application.Abstractions;
using ArksScanner.Application.Plans;
using ArksScanner.Domain.Plans;

namespace ArksScanner.Application.Tests.Plans;

public sealed class PlanServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 6, 0, 0, TimeSpan.Zero); // 14:00 in Kuala Lumpur

    private static (PlanService Service, FakePlanRepository Repo) Create(PlanPhase phase = PlanPhase.Enforced)
    {
        var repo = new FakePlanRepository(PlanSettings.Seed(
            new PlanSettingsValues(phase, null, 5, 3, 30, 7, "Asia/Kuala_Lumpur", 1.5m), Now));
        return (new PlanService(repo, new Clock(Now)), repo);
    }

    [Fact]
    public async Task Free_user_can_use_exactly_five_ocr_pages_then_is_refused_with_reset_time()
    {
        var (service, _) = Create();
        for (var i = 0; i < 5; i++) await service.ConsumeAsync("u1", UsageKind.Ocr, 1, default);

        var refused = await Assert.ThrowsAsync<PlanLimitExceededException>(() => service.ConsumeAsync("u1", UsageKind.Ocr, 1, default));

        Assert.Equal("ocr", refused.KindCode);
        Assert.Equal(5, refused.Limit);
        Assert.Equal(5, refused.Used);
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 16, 0, 0, TimeSpan.Zero), refused.ResetsAt);
    }

    [Fact]
    public async Task Watermark_exports_have_their_own_limit()
    {
        var (service, _) = Create();
        for (var i = 0; i < 3; i++) await service.ConsumeAsync("u1", UsageKind.Watermark, 1, default);
        var refused = await Assert.ThrowsAsync<PlanLimitExceededException>(() => service.ConsumeAsync("u1", UsageKind.Watermark, 1, default));
        Assert.Equal("watermark", refused.KindCode);
        await service.ConsumeAsync("u1", UsageKind.Ocr, 1, default);
    }

    [Fact]
    public async Task Bonus_pages_raise_the_daily_allowance()
    {
        var (service, repo) = Create();
        repo.Bonus["u1"] = 3;
        for (var i = 0; i < 8; i++) await service.ConsumeAsync("u1", UsageKind.Ocr, 1, default);
        await Assert.ThrowsAsync<PlanLimitExceededException>(() => service.ConsumeAsync("u1", UsageKind.Ocr, 1, default));
    }

    [Fact]
    public async Task Test_phase_never_refuses_but_still_counts()
    {
        var (service, _) = Create(PlanPhase.Test);
        for (var i = 0; i < 20; i++) await service.ConsumeAsync("u1", UsageKind.Ocr, 1, default);
        Assert.Equal(20, (await service.GetUsageAsync("u1", default)).OcrPages);
    }

    [Fact]
    public async Task Pro_is_never_refused()
    {
        var (service, repo) = Create();
        repo.Subscriptions.Add(Subscription.GrantManual(Guid.NewGuid(), "u1", Now.AddDays(-1), null, null, "admin", Now));
        for (var i = 0; i < 50; i++) await service.ConsumeAsync("u1", UsageKind.Ocr, 1, default);
        Assert.False((await service.GetEntitlementsAsync("u1", default)).BrandStamp);
    }

    [Fact]
    public async Task Document_limit_counts_active_documents()
    {
        var (service, repo) = Create();
        repo.Documents["u1"] = 29;
        await service.EnsureCanCreateDocumentAsync("u1", default);
        repo.Documents["u1"] = 30;
        var refused = await Assert.ThrowsAsync<PlanLimitExceededException>(() => service.EnsureCanCreateDocumentAsync("u1", default));
        Assert.Equal("documents", refused.KindCode);
        Assert.Null(refused.ResetsAt);
    }

    private sealed record Clock(DateTimeOffset UtcNow) : IClock;

    private sealed class FakePlanRepository(PlanSettings settings) : IPlanRepository
    {
        public readonly List<Subscription> Subscriptions = [];
        public readonly Dictionary<string, int> Bonus = [];
        public readonly Dictionary<string, int> Documents = [];
        private readonly Dictionary<(string, DateOnly, UsageKind), int> used = [];

        public Task<PlanSettings> GetSettingsAsync(CancellationToken ct) => Task.FromResult(settings);
        public Task<IReadOnlyList<Subscription>> GetSubscriptionsAsync(string uid, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Subscription>>(Subscriptions.Where(s => s.AccountUid == uid).ToArray());
        public Task<int?> TryConsumeAsync(string uid, DateOnly day, UsageKind kind, int amount, int? limit, CancellationToken ct)
        {
            used.TryGetValue((uid, day, kind), out var current);
            var allowance = limit + (kind == UsageKind.Ocr ? Bonus.GetValueOrDefault(uid) : 0);
            if (allowance is { } max && current + amount > max) return Task.FromResult<int?>(null);
            used[(uid, day, kind)] = current + amount;
            return Task.FromResult<int?>(current + amount);
        }
        public Task<PlanUsageCounts> GetUsageAsync(string uid, DateOnly day, CancellationToken ct) => Task.FromResult(
            new PlanUsageCounts(used.GetValueOrDefault((uid, day, UsageKind.Ocr)), Bonus.GetValueOrDefault(uid),
                used.GetValueOrDefault((uid, day, UsageKind.Watermark))));
        public Task<int> CountActiveDocumentsAsync(string uid, CancellationToken ct) => Task.FromResult(Documents.GetValueOrDefault(uid));
    }
}
