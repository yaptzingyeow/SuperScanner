namespace ArksScanner.Domain.Plans;

public enum PlanPhase { Test, Enforced }
public enum PlanKind { Free, Pro }
public enum UsageKind { Ocr, Watermark }

public sealed record PlanSettingsValues(
    PlanPhase Phase,
    DateTimeOffset? EnforceFromUtc,
    int FreeOcrPagesPerDay,
    int FreeWatermarkExportsPerDay,
    int FreeMaxDocuments,
    int FreeRetentionDays,
    string UsageTimeZone,
    decimal OcrCostPerThousandPages);

/// <summary>The single row of plan settings edited in the admin portal.</summary>
public sealed class PlanSettings
{
    public const int SingletonId = 1;

    private PlanSettings() { }

    public int Id { get; private set; } = SingletonId;
    public PlanPhase Phase { get; private set; }
    public DateTimeOffset? EnforceFromUtc { get; private set; }
    public int FreeOcrPagesPerDay { get; private set; }
    public int FreeWatermarkExportsPerDay { get; private set; }
    public int FreeMaxDocuments { get; private set; }
    public int FreeRetentionDays { get; private set; }
    public string UsageTimeZone { get; private set; } = "Asia/Kuala_Lumpur";
    public decimal OcrCostPerThousandPages { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? UpdatedByUid { get; private set; }

    public static PlanSettings Seed(PlanSettingsValues values, DateTimeOffset now)
    {
        var settings = new PlanSettings();
        settings.Apply(values, null, now);
        return settings;
    }

    public void Update(PlanSettingsValues values, string adminUid, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adminUid);
        Apply(values, adminUid, now);
    }

    public PlanSettingsValues Values => new(Phase, EnforceFromUtc, FreeOcrPagesPerDay, FreeWatermarkExportsPerDay,
        FreeMaxDocuments, FreeRetentionDays, UsageTimeZone, OcrCostPerThousandPages);

    public PlanPhase EffectivePhase(DateTimeOffset now) =>
        Phase == PlanPhase.Enforced || (EnforceFromUtc is { } from && now >= from) ? PlanPhase.Enforced : PlanPhase.Test;

    /// <summary>The usage day in the user's own time zone when known (else the admin default).</summary>
    public DateOnly UsageDay(DateTimeOffset now, string? userTimeZone = null) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Zone(userTimeZone)).DateTime);

    /// <summary>The next local midnight (when daily counters reset), as an instant.</summary>
    public DateTimeOffset NextReset(DateTimeOffset now, string? userTimeZone = null)
    {
        var zone = Zone(userTimeZone);
        var nextDay = UsageDay(now, userTimeZone).AddDays(1).ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(nextDay, zone.GetUtcOffset(nextDay)).ToUniversalTime();
    }

    private TimeZoneInfo Zone(string? userTimeZone = null) =>
        userTimeZone is not null && TimeZoneInfo.TryFindSystemTimeZoneById(userTimeZone, out var own)
            ? own : TimeZoneInfo.FindSystemTimeZoneById(UsageTimeZone);

    private void Apply(PlanSettingsValues v, string? adminUid, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(v);
        if (!Enum.IsDefined(v.Phase)) throw new ArgumentOutOfRangeException(nameof(v), "Unknown phase.");
        Range(v.FreeOcrPagesPerDay, 0, 10_000, nameof(v.FreeOcrPagesPerDay));
        Range(v.FreeWatermarkExportsPerDay, 0, 10_000, nameof(v.FreeWatermarkExportsPerDay));
        Range(v.FreeMaxDocuments, 1, 100_000, nameof(v.FreeMaxDocuments));
        Range(v.FreeRetentionDays, 1, 3650, nameof(v.FreeRetentionDays));
        if (v.OcrCostPerThousandPages is < 0 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(v), "OCR cost must be 0-1000.");
        if (string.IsNullOrWhiteSpace(v.UsageTimeZone))
            throw new ArgumentException("A time zone is required.", nameof(v));
        try { TimeZoneInfo.FindSystemTimeZoneById(v.UsageTimeZone); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw new ArgumentException("Unknown time zone.", nameof(v), e); }

        Phase = v.Phase;
        EnforceFromUtc = v.EnforceFromUtc;
        FreeOcrPagesPerDay = v.FreeOcrPagesPerDay;
        FreeWatermarkExportsPerDay = v.FreeWatermarkExportsPerDay;
        FreeMaxDocuments = v.FreeMaxDocuments;
        FreeRetentionDays = v.FreeRetentionDays;
        UsageTimeZone = v.UsageTimeZone;
        OcrCostPerThousandPages = v.OcrCostPerThousandPages;
        UpdatedAt = now;
        UpdatedByUid = adminUid;
    }

    private static void Range(int value, int min, int max, string name)
    {
        if (value < min || value > max) throw new ArgumentOutOfRangeException(name, $"{name} must be {min}-{max}.");
    }
}
