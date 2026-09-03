namespace SuperScanner.Application.Abstractions;

public sealed record VerifiedRequestIdentity(string FirebaseUid, string? Email);

public interface IRequestIdentityVerifier
{
    Task<VerifiedRequestIdentity> VerifyAsync(
        string idToken,
        string appCheckToken,
        CancellationToken cancellationToken);
}
