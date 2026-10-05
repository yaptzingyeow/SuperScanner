using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace ArksScanner.Infrastructure.Auth;

public sealed record VerifiedFirebaseIdToken(string Uid, string? Email, bool IsAnonymous = false, string? SignInProvider = null, bool EmailVerified = false);

public interface IFirebaseIdTokenVerifier
{
    Task<VerifiedFirebaseIdToken> VerifyAsync(string token, CancellationToken cancellationToken);
}

public sealed class FirebaseAdminIdTokenVerifier : IFirebaseIdTokenVerifier, ArksScanner.Application.Plans.IIdentityAccountDeleter
{
    private readonly Lazy<FirebaseAuth> _firebaseAuth;

    public FirebaseAdminIdTokenVerifier(IOptions<FirebaseAuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.ProjectId);
        var projectId = options.Value.ProjectId;
        _firebaseAuth = new Lazy<FirebaseAuth>(
            () => CreateFirebaseAuth(projectId, options.Value.ServiceAccountJson),
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
        return new VerifiedFirebaseIdToken(identity.Uid, email, FirebaseClaims.IsAnonymous(identity.Claims),
            FirebaseClaims.SignInProvider(identity.Claims), FirebaseClaims.EmailVerified(identity.Claims));
    }

    public async Task DeleteAsync(string uid, CancellationToken ct)
    {
        try { await _firebaseAuth.Value.DeleteUserAsync(uid, ct); }
        catch (FirebaseAuthException exception) when (exception.AuthErrorCode == AuthErrorCode.UserNotFound) { }
    }

    private static FirebaseAuth CreateFirebaseAuth(string projectId, string? serviceAccountJson)
    {
        var app = FirebaseApp.Create(
            new AppOptions
            {
                Credential = string.IsNullOrWhiteSpace(serviceAccountJson)
                    ? GoogleCredential.GetApplicationDefault()
                    : GoogleCredential.FromServiceAccountCredential(
                        CredentialFactory.FromJson<ServiceAccountCredential>(serviceAccountJson)),
                ProjectId = projectId
            },
            $"ArksScanner-{Guid.NewGuid():N}");
        return FirebaseAuth.GetAuth(app);
    }
}
