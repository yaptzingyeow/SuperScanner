using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SuperScanner.Infrastructure.Auth;

public interface IFirebaseAppCheckTokenVerifier
{
    Task<string> VerifyAsync(string token, CancellationToken cancellationToken);
}

public sealed class FirebaseAppCheckTokenVerifier : IFirebaseAppCheckTokenVerifier
{
    private static readonly TimeSpan MaximumJwksCacheDuration = TimeSpan.FromHours(6);
    private readonly HttpClient _httpClient;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly HashSet<string> _allowedAppIds;
    private readonly Uri _jwksEndpoint;
    private readonly JsonWebTokenHandler _tokenHandler = new() { MapInboundClaims = false };
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private IReadOnlyList<SecurityKey> _cachedKeys = [];
    private DateTimeOffset _keysExpireAt;

    public FirebaseAppCheckTokenVerifier(HttpClient httpClient, IOptions<FirebaseAuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        var value = options.Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(value.ProjectNumber);
        if (value.AllowedAppIds.Length == 0 || value.AllowedAppIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one Firebase App ID must be configured.", nameof(options));
        }

        _httpClient = httpClient;
        _issuer = $"https://firebaseappcheck.googleapis.com/{value.ProjectNumber}";
        _audience = $"projects/{value.ProjectNumber}";
        _allowedAppIds = new HashSet<string>(value.AllowedAppIds, StringComparer.Ordinal);
        _jwksEndpoint = value.AppCheckJwksEndpoint;
    }

    public async Task<string> VerifyAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw Rejected();
        }

        try
        {
            var jwt = _tokenHandler.ReadJsonWebToken(token);
            if (!string.Equals(jwt.Alg, SecurityAlgorithms.RsaSha256, StringComparison.Ordinal) ||
                !string.Equals(jwt.Typ, "JWT", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(jwt.Kid))
            {
                throw Rejected();
            }

            var signingKeys = await GetSigningKeysAsync(cancellationToken);
            var matchingKeys = signingKeys.Where(key => string.Equals(key.KeyId, jwt.Kid, StringComparison.Ordinal)).ToArray();
            if (matchingKeys.Length == 0)
            {
                signingKeys = await GetSigningKeysAsync(cancellationToken, forceRefresh: true);
                matchingKeys = signingKeys.Where(key => string.Equals(key.KeyId, jwt.Kid, StringComparison.Ordinal)).ToArray();
            }

            if (matchingKeys.Length == 0)
            {
                throw Rejected();
            }

            var validation = await _tokenHandler.ValidateTokenAsync(token, new TokenValidationParameters
            {
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = matchingKeys,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            });

            var appId = validation.ClaimsIdentity?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? jwt.Subject;

            if (!validation.IsValid || string.IsNullOrWhiteSpace(appId) || !_allowedAppIds.Contains(appId))
            {
                throw Rejected();
            }

            return appId;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or HttpRequestException or SecurityTokenException)
        {
            throw Rejected(exception);
        }
    }

    private async Task<IReadOnlyList<SecurityKey>> GetSigningKeysAsync(
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        var now = DateTimeOffset.UtcNow;
        if (!forceRefresh && _cachedKeys.Count > 0 && now < _keysExpireAt)
        {
            return _cachedKeys;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (!forceRefresh && _cachedKeys.Count > 0 && now < _keysExpireAt)
            {
                return _cachedKeys;
            }

            using var response = await _httpClient.GetAsync(_jwksEndpoint, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var keys = new JsonWebKeySet(json).GetSigningKeys().ToArray();
            if (keys.Length == 0)
            {
                throw new SecurityTokenException("The Firebase App Check JWKS response contained no signing keys.");
            }

            var providerMaxAge = response.Headers.CacheControl?.MaxAge ?? MaximumJwksCacheDuration;
            var cacheDuration = providerMaxAge < MaximumJwksCacheDuration ? providerMaxAge : MaximumJwksCacheDuration;
            _cachedKeys = keys;
            _keysExpireAt = now.Add(cacheDuration);
            return _cachedKeys;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static UnauthorizedAccessException Rejected(Exception? innerException = null) =>
        new("Firebase App Check token verification failed.", innerException);
}
