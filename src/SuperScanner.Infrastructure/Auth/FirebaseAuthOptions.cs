namespace SuperScanner.Infrastructure.Auth;

public sealed class FirebaseAuthOptions
{
    public const string SectionName = "Firebase";
    public const string DefaultAppCheckJwksEndpoint = "https://firebaseappcheck.googleapis.com/v1/jwks";

    public string ProjectId { get; init; } = string.Empty;
    public string ProjectNumber { get; init; } = string.Empty;
    public string[] AllowedAppIds { get; init; } = [];
    public Uri AppCheckJwksEndpoint { get; init; } = new(DefaultAppCheckJwksEndpoint);
}
