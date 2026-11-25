using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ArksScanner.Infrastructure.Ads;

/// <summary>Google's AdMob server-side verification keys by key id.</summary>
public interface IAdMobKeySource
{
    Task<IReadOnlyDictionary<long, string>> GetPemKeysAsync(bool refresh, CancellationToken ct);
}

/// <summary>Downloads the keys from Google and caches them for a day (Google rotates them rarely).</summary>
public sealed class HttpAdMobKeySource(HttpClient http, IMemoryCache cache, IOptions<AdsOptions> options) : IAdMobKeySource
{
    private const string CacheKey = "admob-verifier-keys";

    public async Task<IReadOnlyDictionary<long, string>> GetPemKeysAsync(bool refresh, CancellationToken ct)
    {
        if (!refresh && cache.TryGetValue(CacheKey, out IReadOnlyDictionary<long, string>? cached) && cached is not null)
            return cached;
        await using var stream = await http.GetStreamAsync(options.Value.AdMobVerifierKeysUrl, ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var keys = new Dictionary<long, string>();
        foreach (var key in json.RootElement.GetProperty("keys").EnumerateArray())
            keys[key.GetProperty("keyId").GetInt64()] = key.GetProperty("pem").GetString() ?? string.Empty;
        cache.Set(CacheKey, (IReadOnlyDictionary<long, string>)keys, TimeSpan.FromHours(24));
        return keys;
    }
}

/// <summary>A verified AdMob reward callback.</summary>
public sealed record AdMobReward(string TransactionId, string CustomData, string? UserId);

/// <summary>
/// Checks an AdMob server-side verification callback: the query string up to "&amp;signature=" is signed with
/// ECDSA P-256 / SHA-256, signature and key_id are the last two parameters, and the signature is URL-safe base64.
/// See https://developers.google.com/admob/android/ssv.
/// </summary>
public sealed class AdMobRewardVerifier(IAdMobKeySource keys)
{
    public async Task<AdMobReward?> VerifyAsync(string rawQueryString, CancellationToken ct)
    {
        var query = rawQueryString.StartsWith('?') ? rawQueryString[1..] : rawQueryString;
        var signatureAt = query.IndexOf("&signature=", StringComparison.Ordinal);
        if (signatureAt <= 0) return null;
        var signed = Encoding.UTF8.GetBytes(query[..signatureAt]);
        var parameters = Parse(query);
        if (!parameters.TryGetValue("signature", out var signatureText) ||
            !parameters.TryGetValue("key_id", out var keyIdText) || !long.TryParse(keyIdText, out var keyId) ||
            !parameters.TryGetValue("transaction_id", out var transactionId) || string.IsNullOrEmpty(transactionId) ||
            !parameters.TryGetValue("custom_data", out var customData) || string.IsNullOrEmpty(customData))
            return null;

        byte[] signature;
        try { signature = Base64UrlDecode(signatureText); }
        catch (FormatException) { return null; }

        // A key id we don't know yet may be a freshly rotated key: refresh once.
        var pemKeys = await keys.GetPemKeysAsync(refresh: false, ct);
        if (!pemKeys.ContainsKey(keyId)) pemKeys = await keys.GetPemKeysAsync(refresh: true, ct);
        if (!pemKeys.TryGetValue(keyId, out var pem)) return null;

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(pem);
        if (!ecdsa.VerifyData(signed, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            return null;
        parameters.TryGetValue("user_id", out var userId);
        return new AdMobReward(transactionId, customData, userId);
    }

    private static Dictionary<string, string> Parse(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var name = Uri.UnescapeDataString(equals < 0 ? pair : pair[..equals]);
            var value = equals < 0 ? string.Empty : Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
            result.TryAdd(name, value);
        }
        return result;
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }
}
