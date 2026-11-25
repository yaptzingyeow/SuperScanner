using System.Security.Cryptography;
using System.Text;
using ArksScanner.Infrastructure.Ads;

namespace ArksScanner.Infrastructure.IntegrationTests.Ads;

/// <summary>Signs callbacks the way AdMob does (ECDSA P-256, SHA-256, DER, URL-safe base64) with a test key.</summary>
public sealed class AdMobRewardVerifierTests : IDisposable
{
    private const long KeyId = 3335741209;
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly FakeKeys keys;

    public AdMobRewardVerifierTests() => keys = new FakeKeys(new Dictionary<long, string> { [KeyId] = key.ExportSubjectPublicKeyInfoPem() });

    public void Dispose() => key.Dispose();

    [Fact]
    public async Task A_correctly_signed_callback_is_accepted_with_its_transaction_and_custom_data()
    {
        var reward = await new AdMobRewardVerifier(keys).VerifyAsync(Signed(Content("abc-123")), default);

        Assert.NotNull(reward);
        Assert.Equal("tx-42", reward.TransactionId);
        Assert.Equal("abc-123", reward.CustomData);
        Assert.Equal("user-1", reward.UserId);
    }

    [Fact]
    public async Task A_changed_parameter_breaks_the_signature()
    {
        var query = Signed(Content("abc-123")).Replace("reward_amount=1", "reward_amount=9", StringComparison.Ordinal);

        Assert.Null(await new AdMobRewardVerifier(keys).VerifyAsync(query, default));
    }

    [Fact]
    public async Task Unsigned_or_unknown_key_callbacks_are_rejected_after_one_key_refresh()
    {
        var verifier = new AdMobRewardVerifier(keys);
        Assert.Null(await verifier.VerifyAsync("?" + Content("abc-123"), default));
        Assert.Null(await verifier.VerifyAsync(Signed(Content("abc-123"), keyId: 1), default));
        Assert.Equal(1, keys.Refreshes);
    }

    [Fact]
    public async Task A_callback_without_custom_data_cannot_name_a_reward()
    {
        Assert.Null(await new AdMobRewardVerifier(keys).VerifyAsync(Signed(Content(customData: null)), default));
    }

    private static string Content(string? customData) =>
        "ad_network=5450213213286189855&ad_unit=1234567890" +
        (customData is null ? string.Empty : "&custom_data=" + Uri.EscapeDataString(customData)) +
        "&reward_amount=1&reward_item=Reward&timestamp=1764036000000&transaction_id=tx-42&user_id=user-1";

    private string Signed(string content, long keyId = KeyId)
    {
        var signature = key.SignData(Encoding.UTF8.GetBytes(content), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        var encoded = Convert.ToBase64String(signature).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"?{content}&signature={encoded}&key_id={keyId}";
    }

    private sealed class FakeKeys(IReadOnlyDictionary<long, string> pemKeys) : IAdMobKeySource
    {
        public int Refreshes { get; private set; }

        public Task<IReadOnlyDictionary<long, string>> GetPemKeysAsync(bool refresh, CancellationToken ct)
        {
            if (refresh) Refreshes++;
            return Task.FromResult(pemKeys);
        }
    }
}
