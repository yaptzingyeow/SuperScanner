using ArksScanner.Domain.Plans;

namespace ArksScanner.Domain.Tests.Plans;

public sealed class AccountRegionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Records_a_valid_time_zone_and_locale_and_ignores_junk()
    {
        var account = Account.Create("u1", null, "password", false, Now);

        Assert.True(account.SetRegion("Europe/London", "en-GB", Now));
        Assert.Equal("Europe/London", account.TimeZone);
        Assert.Equal("en-GB", account.Locale);

        Assert.False(account.SetRegion("Not/AZone", "x;drop", Now.AddDays(2)));
        Assert.Equal("Europe/London", account.TimeZone);
        Assert.Equal("en-GB", account.Locale);
    }

    [Fact]
    public void A_time_zone_change_counts_at_most_once_a_day_so_quota_cannot_be_reset_by_hopping_zones()
    {
        var account = Account.Create("u1", null, "password", false, Now);
        account.SetRegion("Asia/Kuala_Lumpur", "ms-MY", Now);

        account.SetRegion("Pacific/Kiritimati", "ms-MY", Now.AddHours(2));
        Assert.Equal("Asia/Kuala_Lumpur", account.TimeZone);

        account.SetRegion("Pacific/Kiritimati", "ms-MY", Now.AddHours(25));
        Assert.Equal("Pacific/Kiritimati", account.TimeZone);
    }

    [Fact]
    public void Locale_can_change_any_time()
    {
        var account = Account.Create("u1", null, "password", false, Now);
        account.SetRegion("Asia/Tokyo", "ja", Now);
        account.SetRegion("Asia/Tokyo", "zh-Hant-TW", Now.AddMinutes(5));
        Assert.Equal("zh-Hant-TW", account.Locale);
    }
}
