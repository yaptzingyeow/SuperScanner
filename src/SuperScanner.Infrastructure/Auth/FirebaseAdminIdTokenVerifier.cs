using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace SuperScanner.Infrastructure.Auth;

public sealed record VerifiedFirebaseIdToken(string Uid, string? Email);

public interface IFirebaseIdTokenVerifier
{
    Task<VerifiedFirebaseIdToken> VerifyAsync(string token, CancellationToken cancellationToken);
}

public sealed class FirebaseAdminIdTokenVerifier : IFirebaseIdTokenVerifier
{
    private readonly Lazy<FirebaseAuth> _firebaseAuth;

    public FirebaseAdminIdTokenVerifier(IOptions<FirebaseAuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ProjectId);
        var projectId = options.Value.ProjectId;
        _firebaseAuth = new Lazy<FirebaseAuth>(
            () => CreateFirebaseAuth(projectId),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<VerifiedFirebaseIdToken> VerifyAsync(
        string token,
        CancellationToken cancellationToken)
    {
        var identity = await _firebaseAuth.Value.VerifyIdTokenAsync(
            token,
            checkRevoked: true,
            cancellationToken);
        var email = identity.Claims.TryGetValue("email", out var emailClaim)
            ? emailClaim as string
            : null;
        return new VerifiedFirebaseIdToken(identity.Uid, email);
    }

    private static FirebaseAuth CreateFirebaseAuth(string projectId)
    {
        var app = FirebaseApp.Create(
            new AppOptions
            {
                Credential = GoogleCredential.GetApplicationDefault(),
                ProjectId = projectId
            },
            $"SuperScanner-{Guid.NewGuid():N}");
        return FirebaseAuth.GetAuth(app);
    }
}
