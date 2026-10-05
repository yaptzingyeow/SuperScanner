using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace ArksScanner.Api.IntegrationTests.Plans;

public sealed class MePlanTests : IAsyncLifetime
{
    private PlansApiFixture fixture = null!;
    public async Task InitializeAsync() => fixture = await PlansApiFixture.StartAsync(builder =>
    {
        builder.UseSetting("Plans:Phase", "Enforced");
        builder.UseSetting("Plans:FreeOcrPagesPerDay", "5");
    });
    public async Task DisposeAsync() => await fixture.DisposeAsync();

    [Fact]
    public async Task Me_plan_returns_limits_usage_and_reset()
    {
        using var client = fixture.Client("member");

        var body = await client.GetFromJsonAsync<JsonElement>("/api/me/plan");

        Assert.Equal("Free", body.GetProperty("plan").GetString());
        Assert.Equal("Enforced", body.GetProperty("phase").GetString());
        Assert.True(body.GetProperty("brandStamp").GetBoolean());
        Assert.Equal(5, body.GetProperty("limits").GetProperty("ocrPagesPerDay").GetInt32());
        Assert.Equal(3, body.GetProperty("limits").GetProperty("watermarkExportsPerDay").GetInt32());
        Assert.Equal(30, body.GetProperty("limits").GetProperty("maxDocuments").GetInt32());
        Assert.Equal(7, body.GetProperty("limits").GetProperty("retentionDays").GetInt32());
        Assert.Equal(0, body.GetProperty("usage").GetProperty("ocrPages").GetInt32());
        Assert.True(body.GetProperty("usage").GetProperty("resetsAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);
        Assert.Equal(0, body.GetProperty("documentCount").GetInt32());
    }

    [Fact]
    public async Task Me_plan_reports_whether_the_caller_is_an_admin()
    {
        using var owner = fixture.Client("owner");
        using var member = fixture.Client("member");

        Assert.True((await owner.GetFromJsonAsync<JsonElement>("/api/me/plan")).GetProperty("isAdmin").GetBoolean());
        Assert.False((await member.GetFromJsonAsync<JsonElement>("/api/me/plan")).GetProperty("isAdmin").GetBoolean());
    }

    [Fact]
    public async Task Daily_limits_reset_at_midnight_in_the_users_own_time_zone()
    {
        using var member = fixture.Client("member");
        member.DefaultRequestHeaders.Add("X-Time-Zone", "Pacific/Kiritimati");
        member.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ms-MY");
        await member.GetAsync("/api/me/plan"); // first visit records the region

        var body = await member.GetFromJsonAsync<JsonElement>("/api/me/plan");

        var reset = body.GetProperty("usage").GetProperty("resetsAt").GetDateTimeOffset();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Kiritimati");
        Assert.Equal(TimeSpan.Zero, TimeZoneInfo.ConvertTime(reset, zone).TimeOfDay);
        Assert.Equal("Pacific/Kiritimati", body.GetProperty("timeZone").GetString());
        Assert.Equal("ms-MY", body.GetProperty("locale").GetString());
        await using var db = fixture.Db();
        var account = await db.Accounts.SingleAsync(a => a.FirebaseUid == "member-uid");
        Assert.Equal("Pacific/Kiritimati", account.TimeZone);
    }
}
