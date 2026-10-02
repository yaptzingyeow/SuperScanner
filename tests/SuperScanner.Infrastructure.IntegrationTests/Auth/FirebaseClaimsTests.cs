using Newtonsoft.Json.Linq;
using SuperScanner.Infrastructure.Auth;

namespace SuperScanner.Infrastructure.IntegrationTests.Auth;

public sealed class FirebaseClaimsTests
{
    [Fact]
    public void Anonymous_sign_in_provider_is_a_guest()
    {
        var claims = new Dictionary<string, object>
        {
            ["firebase"] = JObject.Parse("""{"sign_in_provider":"anonymous","identities":{}}"""),
        };

        Assert.True(FirebaseClaims.IsAnonymous(claims));
    }

    [Theory]
    [InlineData("google.com")]
    [InlineData("password")]
    public void Account_sign_in_providers_are_not_guests(string provider)
    {
        var claims = new Dictionary<string, object>
        {
            ["firebase"] = new Dictionary<string, object> { ["sign_in_provider"] = provider },
        };

        Assert.False(FirebaseClaims.IsAnonymous(claims));
    }

    [Fact]
    public void Missing_or_malformed_firebase_claim_is_not_a_guest()
    {
        Assert.False(FirebaseClaims.IsAnonymous(new Dictionary<string, object>()));
        Assert.False(FirebaseClaims.IsAnonymous(new Dictionary<string, object> { ["firebase"] = "not json" }));
    }
}
