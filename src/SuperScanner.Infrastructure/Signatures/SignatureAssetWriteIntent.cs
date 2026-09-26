namespace SuperScanner.Infrastructure.Signatures;

// Durable pre-write journal, committed separately before an object upload begins.
public sealed class SignatureAssetWriteIntent
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string AssetKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
