using System.Text.Json;

namespace ArksScanner.Infrastructure.Auth;

public static class FirebaseClaims
{
    /// <summary>
    /// True when the verified ID token was issued for an anonymous (guest) sign-in:
    /// its <c>firebase.sign_in_provider</c> claim is <c>anonymous</c>.
    /// </summary>
    public static bool IsAnonymous(IReadOnlyDictionary<string, object> claims) =>
        SignInProvider(claims) == "anonymous";

    /// <summary>True when the token's <c>email_verified</c> claim is true (Google sign-ins always are).</summary>
    public static bool EmailVerified(IReadOnlyDictionary<string, object> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        return claims.TryGetValue("email_verified", out var value) && value switch
        {
            bool flag => flag,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            _ => string.Equals(value?.ToString(), "true", StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>The token's <c>firebase.sign_in_provider</c> (google.com, password, anonymous…), or null.</summary>
    public static string? SignInProvider(IReadOnlyDictionary<string, object> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        if (!claims.TryGetValue("firebase", out var firebase) || firebase is null) return null;

        if (firebase is IDictionary<string, object> map)
            return map.TryGetValue("sign_in_provider", out var provider) ? provider?.ToString() : null;

        // The Admin SDK hands nested claims over as a JSON object; read it as JSON.
        try
        {
            using var json = JsonDocument.Parse(firebase.ToString() ?? string.Empty);
            return json.RootElement.ValueKind == JsonValueKind.Object &&
                json.RootElement.TryGetProperty("sign_in_provider", out var provider) &&
                provider.ValueKind == JsonValueKind.String
                ? provider.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
