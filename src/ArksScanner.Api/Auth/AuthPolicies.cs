using Microsoft.AspNetCore.Authorization;

namespace ArksScanner.Api.Auth;

public static class AuthPolicies
{
    /// <summary>A signed-in account; guests must log in first (page text, signature and mark editing).</summary>
    public const string SignedInAccount = "SignedInAccount";

    public static void AddSignedInAccount(AuthorizationOptions options) =>
        options.AddPolicy(SignedInAccount, policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(context => !context.User.HasClaim(FirebaseAuthenticationHandler.GuestClaimType, "true")));
}
