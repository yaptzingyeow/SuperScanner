namespace ArksScanner.Domain.Plans;

/// <summary>What an account may do right now. A null limit means unlimited.</summary>
public sealed record Entitlements(
    PlanKind Plan,
    PlanPhase Phase,
    bool BrandStamp,
    int? OcrPagesPerDay,
    int? WatermarkExportsPerDay,
    int? MaxDocuments,
    int? RetentionDays,
    DateTimeOffset? ProUntil,
    bool ProForever);

public static class EntitlementPolicy
{
    public static Entitlements Evaluate(PlanSettings settings, IReadOnlyCollection<Subscription> subscriptions, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(subscriptions);
        var phase = settings.EffectivePhase(now);
        var active = subscriptions.Where(s => s.IsActiveAt(now)).ToArray();
        if (active.Length > 0)
        {
            var forever = active.Any(s => s.EndsAt is null);
            return new(PlanKind.Pro, phase, false, null, null, null, null,
                forever ? null : active.Max(s => s.EndsAt), forever);
        }

        return phase == PlanPhase.Test
            ? new(PlanKind.Free, phase, true, null, null, null, null, null, false)
            : new(PlanKind.Free, phase, true, settings.FreeOcrPagesPerDay, settings.FreeWatermarkExportsPerDay,
                settings.FreeMaxDocuments, settings.FreeRetentionDays, null, false);
    }
}
