namespace SuperScanner.Application.Abstractions;

public sealed record AuditWriteRequest(
    string ActorUid,
    string Action,
    string TargetType,
    Guid TargetId,
    string RegionJson,
    DateTimeOffset OccurredAt);

public interface IAuditWriter
{
    Task<Guid> AppendAsync(AuditWriteRequest request, CancellationToken cancellationToken);
}

public sealed record AuditVerificationResult(bool IsValid, long? FirstInvalidSequence);

public interface IAuditVerifier
{
    Task<AuditVerificationResult> VerifyDocumentChainAsync(
        Guid documentId,
        CancellationToken cancellationToken);
}
