using ArksScanner.Domain.Plans;

namespace ArksScanner.Domain.Tests.Plans;

public sealed class EntitlementPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 6, 0, 0, TimeSpan.Zero);

    internal static PlanSettings Settings(PlanPhase phase = PlanPhase.Enforced, DateTimeOffset? enforceFrom = null) =>
        PlanSettings.Seed(new PlanSettingsValues(phase, enforceFrom, 5, 3, 30, 7, "Asia/Kuala_Lumpur", 1.50m), Now);

    private static Subscription Grant(DateTimeOffset start, DateTimeOffset? end) =>
        Subscription.GrantManual(Guid.NewGuid(), "u1", start, end, "friend", "admin", start);

    [Fact]
    public void Test_phase_gives_everyone_unlimited_use_but_keeps_the_brand_stamp()
    {
        var e = EntitlementPolicy.Evaluate(Settings(PlanPhase.Test), [], Now);

        Assert.Equal(PlanKind.Free, e.Plan);
        Assert.Equal(PlanPhase.Test, e.Phase);
        Assert.Null(e.OcrPagesPerDay);
        Assert.Null(e.WatermarkExportsPerDay);
        Assert.Null(e.MaxDocuments);
        Assert.Null(e.RetentionDays);
        Assert.True(e.BrandStamp);
    }

    [Fact]
    public void Enforced_free_account_gets_the_configured_limits()
    {
        var e = EntitlementPolicy.Evaluate(Settings(), [], Now);

        Assert.Equal((5, 3, 30, 7), (e.OcrPagesPerDay!.Value, e.WatermarkExportsPerDay!.Value, e.MaxDocuments!.Value, e.RetentionDays!.Value));
        Assert.True(e.BrandStamp);
    }

    [Fact]
    public void Pro_is_unlimited_without_brand_stamp_or_retention()
    {
        var end = Now.AddMonths(1);
        var e = EntitlementPolicy.Evaluate(Settings(), [Grant(Now.AddDays(-1), end)], Now);

        Assert.Equal(PlanKind.Pro, e.Plan);
        Assert.Null(e.OcrPagesPerDay);
        Assert.Null(e.WatermarkExportsPerDay);
        Assert.Null(e.MaxDocuments);
        Assert.Null(e.RetentionDays);
        Assert.False(e.BrandStamp);
        Assert.Equal(end, e.ProUntil);
        Assert.False(e.ProForever);
    }

    [Fact]
    public void Forever_grant_reports_pro_forever()
    {
        var e = EntitlementPolicy.Evaluate(Settings(), [Grant(Now.AddDays(-1), null)], Now);
        Assert.True(e.ProForever);
        Assert.Null(e.ProUntil);
    }

    [Fact]
    public void Revoked_or_expired_subscription_is_free()
    {
        var revoked = Grant(Now.AddDays(-1), null);
        revoked.Revoke("admin", Now.AddHours(-1));
        var expired = Grant(Now.AddMonths(-2), Now.AddMonths(-1));

        Assert.Equal(PlanKind.Free, EntitlementPolicy.Evaluate(Settings(), [revoked, expired], Now).Plan);
    }

    [Fact]
    public void Subscription_starting_in_the_future_is_free_until_it_starts()
    {
        var future = Grant(Now.AddDays(1), null);
        Assert.Equal(PlanKind.Free, EntitlementPolicy.Evaluate(Settings(), [future], Now).Plan);
        Assert.Equal(PlanKind.Pro, EntitlementPolicy.Evaluate(Settings(), [future], Now.AddDays(2)).Plan);
    }

    [Fact]
    public void EnforceFromUtc_in_the_past_switches_a_test_phase_to_enforced()
    {
        var e = EntitlementPolicy.Evaluate(Settings(PlanPhase.Test, Now.AddMinutes(-1)), [], Now);
        Assert.Equal(PlanPhase.Enforced, e.Phase);
        Assert.Equal(5, e.OcrPagesPerDay);
        Assert.Equal(PlanPhase.Test, EntitlementPolicy.Evaluate(Settings(PlanPhase.Test, Now.AddMinutes(1)), [], Now).Phase);
    }
}

public sealed class PlanSettingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Usage_day_rolls_over_at_midnight_in_Kuala_Lumpur()
    {
        var settings = EntitlementPolicyTests.Settings();
        Assert.Equal(new DateOnly(2026, 10, 4), settings.UsageDay(new DateTimeOffset(2026, 10, 4, 15, 59, 59, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 10, 5), settings.UsageDay(new DateTimeOffset(2026, 10, 4, 16, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 16, 0, 0, TimeSpan.Zero),
            settings.NextReset(new DateTimeOffset(2026, 10, 4, 15, 0, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData(-1, 3, 30, 7, "Asia/Kuala_Lumpur", 1.5)]
    [InlineData(10001, 3, 30, 7, "Asia/Kuala_Lumpur", 1.5)]
    [InlineData(5, -1, 30, 7, "Asia/Kuala_Lumpur", 1.5)]
    [InlineData(5, 10001, 30, 7, "Asia/Kuala_Lumpur", 1.5)]
    [InlineData(5, 3, 0, 7, "Asia/Kuala_Lumpur", 1.5)]
    [InlineData(5, 3, 100001, 7, "Asia/Kuala_Lumpur", 1.5)]
    [InlineData(5, 3, 30, 0, "Asia/Kuala_Lumpur", 1.5)]
    [InlineData(5, 3, 30, 3651, "Asia/Kuala_Lumpur", 1.5)]
    [InlineData(5, 3, 30, 7, "Mars/Olympus", 1.5)]
    [InlineData(5, 3, 30, 7, "Asia/Kuala_Lumpur", -1)]
    [InlineData(5, 3, 30, 7, "Asia/Kuala_Lumpur", 1001)]
    public void Update_rejects_out_of_range_values_and_unknown_time_zones(int ocr, int watermark, int docs, int days, string zone, double cost)
    {
        var settings = EntitlementPolicyTests.Settings();
        Assert.ThrowsAny<ArgumentException>(() => settings.Update(
            new PlanSettingsValues(PlanPhase.Enforced, null, ocr, watermark, docs, days, zone, (decimal)cost), "admin", Now));
    }

    [Fact]
    public void Update_records_who_changed_it()
    {
        var settings = EntitlementPolicyTests.Settings();
        settings.Update(new PlanSettingsValues(PlanPhase.Test, null, 8, 3, 30, 7, "Asia/Kuala_Lumpur", 2m), "admin-1", Now);
        Assert.Equal(8, settings.FreeOcrPagesPerDay);
        Assert.Equal("admin-1", settings.UpdatedByUid);
    }
}

public sealed class SubscriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Extend_must_move_the_end_later_and_null_means_forever()
    {
        var sub = Subscription.GrantManual(Guid.NewGuid(), "u1", Now, Now.AddMonths(1), null, "admin", Now);
        Assert.ThrowsAny<ArgumentException>(() => sub.Extend(Now.AddDays(5), Now));
        sub.Extend(Now.AddMonths(3), Now);
        Assert.Equal(Now.AddMonths(3), sub.EndsAt);
        sub.Extend(null, Now);
        Assert.Null(sub.EndsAt);
        Assert.True(sub.IsActiveAt(Now.AddYears(10)));
    }

    [Fact]
    public void Grant_rejects_long_notes_and_ends_before_start()
    {
        Assert.ThrowsAny<ArgumentException>(() => Subscription.GrantManual(Guid.NewGuid(), "u1", Now, Now.AddDays(-1), null, "admin", Now));
        Assert.ThrowsAny<ArgumentException>(() => Subscription.GrantManual(Guid.NewGuid(), "u1", Now, null, new string('x', 201), "admin", Now));
    }
}
