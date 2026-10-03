using System.Net;
using Microsoft.EntityFrameworkCore;

namespace ArksScanner.Api.IntegrationTests.Plans;

public sealed class AccountAndAdminTests : IAsyncLifetime
{
    private PlansApiFixture fixture = null!;
    public async Task InitializeAsync() => fixture = await PlansApiFixture.StartAsync();
    public async Task DisposeAsync() => await fixture.DisposeAsync();

    [Fact]
    public async Task First_authenticated_request_creates_the_account_and_repeat_requests_within_10_minutes_do_not_write()
    {
        using var client = fixture.Client("member");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);
        DateTimeOffset firstSeen;
        await using (var db = fixture.Db())
        {
            var account = await db.Accounts.SingleAsync(a => a.FirebaseUid == "member-uid");
            Assert.Equal("member@example.test", account.Email);
            Assert.Equal("password", account.SignInProvider);
            Assert.False(account.IsGuest);
            firstSeen = account.LastSeenAt;
        }

        await client.GetAsync("/api/me");

        await using var verify = fixture.Db();
        Assert.Equal(firstSeen, (await verify.Accounts.SingleAsync(a => a.FirebaseUid == "member-uid")).LastSeenAt);
    }

    [Fact]
    public async Task Guests_are_recorded_as_guests()
    {
        using var client = fixture.Client("guest");
        await client.GetAsync("/api/me");
        await using var db = fixture.Db();
        Assert.True((await db.Accounts.SingleAsync(a => a.FirebaseUid == "guest-uid")).IsGuest);
    }

    [Fact]
    public async Task Owner_email_becomes_the_first_admin()
    {
        using var client = fixture.Client("owner");
        await client.GetAsync("/api/me");
        await using var db = fixture.Db();
        Assert.Equal("owner-uid", (await db.Admins.SingleAsync()).AccountUid);
    }

    [Theory]
    [InlineData("member")]
    [InlineData("guest")]
    public async Task Admin_endpoints_return_404_for_non_admins_and_guests(string user)
    {
        using var owner = fixture.Client("owner");
        await owner.GetAsync("/api/me");
        using var client = fixture.Client(user);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/admin/dashboard")).StatusCode);
    }

    [Fact]
    public async Task Admin_endpoints_allow_admins()
    {
        using var client = fixture.Client("owner");
        await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/dashboard")).StatusCode);
    }
}
