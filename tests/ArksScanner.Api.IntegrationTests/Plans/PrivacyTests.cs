using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ArksScanner.Application.Plans;

namespace ArksScanner.Api.IntegrationTests.Plans;

public sealed class PrivacyTests : IAsyncLifetime
{
    private readonly RecordingDeleter deleter = new();
    private PlansApiFixture fixture = null!;

    public async Task InitializeAsync() => fixture = await PlansApiFixture.StartAsync(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IIdentityAccountDeleter>();
            services.AddSingleton<IIdentityAccountDeleter>(deleter);
        }));

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private static async Task<Guid> CreateAsync(HttpClient client, string title) =>
        (await (await client.PostAsJsonAsync("/api/documents", new { title })).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

    [Fact]
    public async Task Export_contains_my_account_and_documents_but_nobody_elses()
    {
        using var member = fixture.Client("member");
        using var owner = fixture.Client("owner");
        await CreateAsync(member, "My lease");
        await CreateAsync(owner, "Owner secret");

        var response = await member.GetAsync("/api/me/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition?.ToString());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("member-uid", body.GetProperty("account").GetProperty("uid").GetString());
        Assert.Equal(["My lease"], body.GetProperty("documents").EnumerateArray().Select(d => d.GetProperty("title").GetString()));
        Assert.DoesNotContain("Owner secret", body.ToString());
    }

    [Fact]
    public async Task Consent_is_recorded_with_its_version_and_reported_back()
    {
        using var member = fixture.Client("member");
        var before = await member.GetFromJsonAsync<JsonElement>("/api/me/plan");
        Assert.Equal(JsonValueKind.Null, before.GetProperty("privacyConsentVersion").ValueKind);
        Assert.False(string.IsNullOrEmpty(before.GetProperty("currentPrivacyVersion").GetString()));

        var accept = await member.PostAsJsonAsync("/api/me/consent",
            new { version = before.GetProperty("currentPrivacyVersion").GetString() });

        Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);
        var after = await member.GetFromJsonAsync<JsonElement>("/api/me/plan");
        Assert.Equal(before.GetProperty("currentPrivacyVersion").GetString(), after.GetProperty("privacyConsentVersion").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PostAsJsonAsync("/api/me/consent", new { version = "1999-01" })).StatusCode);
    }

    [Fact]
    public async Task Deleting_my_account_needs_confirmation_then_removes_documents_account_and_sign_in()
    {
        using var member = fixture.Client("member");
        await CreateAsync(member, "Old lease");

        Assert.Equal(HttpStatusCode.BadRequest, (await member.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/me")
        { Content = JsonContent.Create(new { confirm = "yes" }) })).StatusCode);
        var deleted = await member.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/me")
        { Content = JsonContent.Create(new { confirm = "DELETE" }) });

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(["member-uid"], deleter.Deleted);
        await using var db = fixture.Db();
        Assert.False(await db.Accounts.AnyAsync(a => a.FirebaseUid == "member-uid"));
        Assert.False(await db.Documents.AnyAsync(d => d.OwnerFirebaseUid == "member-uid"));
        Assert.True(await db.Documents.IgnoreQueryFilters().AnyAsync(d => d.OwnerFirebaseUid == "member-uid" &&
            d.RemovedReason == "account_deleted"));
        Assert.True(await db.AuditEvents.AnyAsync(e => e.Action == "account.deleted" && e.ActorUid == "member-uid"));
    }

    [Fact]
    public async Task The_last_admin_cannot_delete_their_own_account()
    {
        using var owner = fixture.Client("owner");
        await owner.GetAsync("/api/me/plan");

        var response = await owner.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/me")
        { Content = JsonContent.Create(new { confirm = "DELETE" }) });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(deleter.Deleted);
    }

    private sealed class RecordingDeleter : IIdentityAccountDeleter
    {
        public List<string> Deleted { get; } = [];
        public Task DeleteAsync(string uid, CancellationToken ct) { Deleted.Add(uid); return Task.CompletedTask; }
    }
}
