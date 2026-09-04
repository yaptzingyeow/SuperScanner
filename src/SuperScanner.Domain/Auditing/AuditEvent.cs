namespace SuperScanner.Domain.Auditing;

public sealed class AuditEvent
{
    private AuditEvent()
    {
    }

    public Guid Id { get; private set; }
    public Guid TargetId { get; private set; }
    public long Sequence { get; private set; }
    public string ActorUid { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string TargetType { get; private set; } = string.Empty;
    public string RegionJson { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public byte[] PreviousHash { get; private set; } = [];
    public byte[] EventHash { get; private set; } = [];
    public byte[] Signature { get; private set; } = [];
    public string SigningKeyId { get; private set; } = string.Empty;

    public static AuditEvent Create(
        Guid id,
        Guid targetId,
        long sequence,
        string actorUid,
        string action,
        string targetType,
        string regionJson,
        DateTimeOffset occurredAt,
        byte[] previousHash,
        byte[] eventHash,
        byte[] signature,
        string signingKeyId) =>
        new()
        {
            Id = id,
            TargetId = targetId,
            Sequence = sequence,
            ActorUid = actorUid,
            Action = action,
            TargetType = targetType,
            RegionJson = regionJson,
            OccurredAt = occurredAt,
            PreviousHash = previousHash.ToArray(),
            EventHash = eventHash.ToArray(),
            Signature = signature.ToArray(),
            SigningKeyId = signingKeyId
        };
}
