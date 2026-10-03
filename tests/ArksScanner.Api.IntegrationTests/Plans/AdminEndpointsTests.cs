using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace ArksScanner.Api.IntegrationTests.Plans;

public sealed class AdminEndpointsTests : IAsyncLifetime
{
    private PlansApiFixture fixture = null!;
    public async Task InitializeAsync() => fixture = await PlansApiFixture.StartAsync(builder =>
        builder.UseSetting("Plans:Phase", "Enforced"));
    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private async Task<HttpClient> SignedInAsync(string user)
    {
        var client = fixture.Client(user);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me/plan")).StatusCode); // creates the account
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string> PlanOf(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/me/plan")).GetProperty("plan").GetString()!;

    [Fact]
    public async Task Non_admin_gets_404_on_every_admin_route()
    {
        using var member = await SignedInAsync("member");
        using var guest = await SignedInAsync("guest");
        foreach (var client in new[] { member, guest })
        {
            foreach (var path in new[] { "dashboard", "users", "users/owner-uid", "subscriptions", "payments", "settings", "admins", "audit" })
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/admin/{path}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/admin/users/member-uid/subscriptions", new { duration = "forever" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/admin/admins", new { email = "member@example.test" })).StatusCode);
        }
        Assert.Equal("Free", await PlanOf(member));
    }

    [Fact]
    public async Task Dashboard_counts_users_subscribers_and_ocr_cost()
    {
        using var owner = await SignedInAsync("owner");
        using var member = await SignedInAsync("member");
        using var guest = await SignedInAsync("guest");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur")).DateTime);
        await using (var db = fixture.Db())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO usage_days (\"AccountUid\",\"Day\",\"OcrPages\",\"WatermarkExports\",\"BonusOcrPages\") VALUES ({"member-uid"}, {today}, 1000, 2, 0)");
        await Json(await owner.PostAsJsonAsync("/api/admin/users/member-uid/subscriptions", new { duration = "1m", note = "friend" }));

        var body = await Json(await owner.GetAsync("/api/admin/dashboard"));

        Assert.Equal(3, body.GetProperty("users").GetProperty("total").GetInt32());
        Assert.Equal(1, body.GetProperty("users").GetProperty("guests").GetInt32());
        Assert.Equal(2, body.GetProperty("users").GetProperty("signedIn").GetInt32());
        Assert.Equal(3, body.GetProperty("users").GetProperty("new").GetProperty("today").GetInt32());
        Assert.Equal(1, body.GetProperty("subscribers").GetProperty("total").GetInt32());
        Assert.Equal(1, body.GetProperty("subscribers").GetProperty("manual").GetInt32());
        Assert.Equal(1000, body.GetProperty("ocr").GetProperty("today").GetInt32());
        Assert.Equal(1.5m, body.GetProperty("ocr").GetProperty("estimatedCostMonth").GetDecimal());
        Assert.Equal(2, body.GetProperty("watermarkExports").GetProperty("today").GetInt32());
        Assert.Equal(30, body.GetProperty("series").GetArrayLength());
        Assert.Equal("Enforced", body.GetProperty("effectivePhase").GetString());
    }

    [Fact]
    public async Task Grant_extend_and_revoke_pro_change_entitlements_and_are_audited()
    {
        using var owner = await SignedInAsync("owner");
        using var member = await SignedInAsync("member");

        var grant = await Json(await owner.PostAsJsonAsync("/api/admin/users/member-uid/subscriptions", new { duration = "3m", note = "friend" }));
        var id = grant.GetProperty("id").GetGuid();
        Assert.Equal("Pro", await PlanOf(member));
        var user = await Json(await owner.GetAsync("/api/admin/users/member-uid"));
        Assert.Equal("Pro", user.GetProperty("plan").GetString());
        Assert.Single(user.GetProperty("subscriptions").EnumerateArray());

        await Json(await owner.PostAsJsonAsync($"/api/admin/subscriptions/{id}/extend", new { duration = "forever" }));
        Assert.True((await member.GetFromJsonAsync<JsonElement>("/api/me/plan")).GetProperty("proForever").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/admin/subscriptions/{id}/revoke", null)).StatusCode);
        Assert.Equal("Free", await PlanOf(member));

        var audit = await Json(await owner.GetAsync("/api/admin/audit?limit=10"));
        var actions = audit.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToArray();
        Assert.Contains("admin.subscription_granted", actions);
        Assert.Contains("admin.subscription_extended", actions);
        Assert.Contains("admin.subscription_revoked", actions);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/admin/users/member-uid/subscriptions", new { duration = "2w" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync("/api/admin/users/nobody/subscriptions", new { duration = "1m" })).StatusCode);
    }

    [Fact]
    public async Task Settings_update_validates_and_takes_effect()
    {
        using var owner = await SignedInAsync("owner");
        using var member = await SignedInAsync("member");
        var settings = await Json(await owner.GetAsync("/api/admin/settings"));
        Assert.Equal(5, settings.GetProperty("freeOcrPagesPerDay").GetInt32());

        object Body(int ocr) => new
        {
            phase = "Enforced", enforceFromUtc = (DateTimeOffset?)null, freeOcrPagesPerDay = ocr, freeWatermarkExportsPerDay = 3,
            freeMaxDocuments = 30, freeRetentionDays = 7, usageTimeZone = "Asia/Kuala_Lumpur", ocrCostPerThousandPages = 1.5m,
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/admin/settings", Body(-1))).StatusCode);
        await Json(await owner.PutAsJsonAsync("/api/admin/settings", Body(9)));

        var plan = await member.GetFromJsonAsync<JsonElement>("/api/me/plan");
        Assert.Equal(9, plan.GetProperty("limits").GetProperty("ocrPagesPerDay").GetInt32());
        var audit = await Json(await owner.GetAsync("/api/admin/audit"));
        Assert.True(audit.GetProperty("items").GetArrayLength() > 0, audit.ToString());
        Assert.Contains(audit.GetProperty("items").EnumerateArray(), e => e.GetProperty("action").GetString() == "admin.settings_updated");
    }

    [Fact]
    public async Task Cannot_remove_the_last_admin()
    {
        using var owner = await SignedInAsync("owner");

        var response = await owner.DeleteAsync("/api/admin/admins/owner-uid");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("last_admin", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Add_admin_requires_an_existing_account()
    {
        using var owner = await SignedInAsync("owner");
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync("/api/admin/admins", new { email = "member@example.test" })).StatusCode);
        using var member = await SignedInAsync("member");

        await Json(await owner.PostAsJsonAsync("/api/admin/admins", new { email = "Member@Example.test" }));

        var admins = await Json(await owner.GetAsync("/api/admin/admins"));
        Assert.Equal(2, admins.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/admin/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await member.DeleteAsync("/api/admin/admins/owner-uid")).StatusCode);
    }

    [Fact]
    public async Task Payments_list_is_empty_until_hitpay()
    {
        using var owner = await SignedInAsync("owner");

        var payments = await Json(await owner.GetAsync("/api/admin/payments"));

        Assert.Equal(0, payments.GetProperty("total").GetInt32());
        Assert.Empty(payments.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Users_list_searches_by_email_and_pages()
    {
        using var owner = await SignedInAsync("owner");
        using var member = await SignedInAsync("member");

        var found = await Json(await owner.GetAsync("/api/admin/users?query=MEMBER@"));

        var item = Assert.Single(found.GetProperty("items").EnumerateArray());
        Assert.Equal("member-uid", item.GetProperty("uid").GetString());
        Assert.Equal(50, found.GetProperty("pageSize").GetInt32());
    }
}
