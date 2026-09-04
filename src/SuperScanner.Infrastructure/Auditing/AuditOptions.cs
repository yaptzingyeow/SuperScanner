namespace SuperScanner.Infrastructure.Auditing;

public sealed class AuditOptions
{
    public const string SectionName = "Audit";

    public string SigningKeyBase64 { get; init; } = string.Empty;
    public string SigningKeyId { get; init; } = string.Empty;
}
