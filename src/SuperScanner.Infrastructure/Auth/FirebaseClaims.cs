using System.Text.Json;

namespace SuperScanner.Infrastructure.Auth;

public static class FirebaseClaims
{
    /// <summary>
    /// True when the verified ID token was issued for an anonymous (guest) sign-in:
    /// its <c>firebase.sign_in_provider</c> claim is <c>anonymous</c>.
    /// </summary>
    public static bool IsAnonymous(IReadOnlyDictionary<string, object> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        if (!claims.TryGetValue("firebase", out var firebase) || firebase is null)
        {
            return false;
        }

        if (firebase is IDictionary<string, object> map)
        {
            return map.TryGetValue("sign_in_provider", out var provider) && provider?.ToString() == "anonymous";
        }

        // The Admin SDK hands nested claims over as a JSON object; read it as JSON.
        try
        {
            using var json = JsonDocument.Parse(firebase.ToString() ?? string.Empty);
            return json.RootElement.ValueKind == JsonValueKind.Object &&
                json.RootElement.TryGetProperty("sign_in_provider", out var provider) &&
                provider.ValueKind == JsonValueKind.String &&
                provider.GetString() == "anonymous";
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
