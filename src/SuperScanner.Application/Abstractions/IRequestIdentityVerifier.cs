namespace SuperScanner.Application.Abstractions;

/// <param name="IsGuest">Signed in anonymously (a Firebase guest), not with an account.</param>
public sealed record VerifiedRequestIdentity(string FirebaseUid, string? Email, bool IsGuest = false);

public interface IRequestIdentityVerifier
{
    Task<VerifiedRequestIdentity> VerifyAsync(
        string idToken,
        string appCheckToken,
        CancellationToken cancellationToken);
}
