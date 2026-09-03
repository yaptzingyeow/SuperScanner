using SuperScanner.Application.Abstractions;

namespace SuperScanner.Infrastructure.Auth;

public sealed class FirebaseRequestIdentityVerifier(
    IFirebaseIdTokenVerifier idTokenVerifier,
    IFirebaseAppCheckTokenVerifier appCheckTokenVerifier) : IRequestIdentityVerifier
{
    public async Task<VerifiedRequestIdentity> VerifyAsync(
        string idToken,
        string appCheckToken,
        CancellationToken cancellationToken)
    {
        try
        {
            await appCheckTokenVerifier.VerifyAsync(appCheckToken, cancellationToken);
            var identity = await idTokenVerifier.VerifyAsync(idToken, cancellationToken);

            if (string.IsNullOrWhiteSpace(identity.Uid))
            {
                throw new UnauthorizedAccessException("Firebase identity token verification failed.");
            }

            return new VerifiedRequestIdentity(identity.Uid, identity.Email);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or FirebaseAdmin.Auth.FirebaseAuthException or UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException("Firebase request verification failed.", exception);
        }
    }
}
