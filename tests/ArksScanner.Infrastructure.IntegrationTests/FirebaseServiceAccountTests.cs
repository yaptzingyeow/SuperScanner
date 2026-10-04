using ArksScanner.Infrastructure.Auth;
using Microsoft.Extensions.Options;

namespace ArksScanner.Infrastructure.IntegrationTests;

public class FirebaseServiceAccountTests
{
    [Fact]
    public async Task Explicit_credentials_reject_user_credentials_instead_of_using_local_ADC()
    {
        var verifier = new FirebaseAdminIdTokenVerifier(Options.Create(new FirebaseAuthOptions
        {
            ProjectId = "test-project",
            ServiceAccountJson = "{\"type\":\"authorized_user\",\"client_id\":\"test\",\"client_secret\":\"test\",\"refresh_token\":\"test\"}"
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => verifier.VerifyAsync("test", CancellationToken.None));
    }
}
