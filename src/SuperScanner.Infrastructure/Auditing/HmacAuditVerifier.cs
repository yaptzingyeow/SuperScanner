using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SuperScanner.Application.Abstractions;
using SuperScanner.Infrastructure.Persistence;

namespace SuperScanner.Infrastructure.Auditing;

public sealed class HmacAuditVerifier : IAuditVerifier
{
    private static readonly byte[] GenesisHash = new byte[32];
    private readonly AppDbContext _db;
    private readonly byte[] _signingKey;
    private readonly string _signingKeyId;

    public HmacAuditVerifier(AppDbContext db, IOptions<AuditOptions> options)
    {
        _db = db;
        ArgumentNullException.ThrowIfNull(options);
        _signingKey = Convert.FromBase64String(options.Value.SigningKeyBase64);
        if (_signingKey.Length < 32)
        {
            throw new ArgumentException("The audit signing key must contain at least 32 bytes.", nameof(options));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.SigningKeyId);
        _signingKeyId = options.Value.SigningKeyId;
    }

    public async Task<AuditVerificationResult> VerifyDocumentChainAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var events = await _db.AuditEvents
            .AsNoTracking()
            .Where(auditEvent => auditEvent.TargetId == documentId)
            .OrderBy(auditEvent => auditEvent.Sequence)
            .ToListAsync(cancellationToken);
        var previousHash = GenesisHash;
        long expectedSequence = 1;
        foreach (var auditEvent in events)
        {
            var request = new AuditWriteRequest(
                auditEvent.ActorUid,
                auditEvent.Action,
                auditEvent.TargetType,
                auditEvent.TargetId,
                auditEvent.RegionJson,
                auditEvent.OccurredAt);
            var payload = CanonicalAuditPayload.Serialize(request, auditEvent.Sequence, auditEvent.SigningKeyId);
            var expectedHash = HmacAuditWriter.ComputeEventHash(previousHash, payload);
            var expectedSignature = HMACSHA256.HashData(_signingKey, expectedHash);
            if (auditEvent.Sequence != expectedSequence ||
                !string.Equals(auditEvent.SigningKeyId, _signingKeyId, StringComparison.Ordinal) ||
                !CryptographicOperations.FixedTimeEquals(auditEvent.PreviousHash, previousHash) ||
                !CryptographicOperations.FixedTimeEquals(auditEvent.EventHash, expectedHash) ||
                !CryptographicOperations.FixedTimeEquals(auditEvent.Signature, expectedSignature))
            {
                return new AuditVerificationResult(false, expectedSequence);
            }

            previousHash = auditEvent.EventHash;
            expectedSequence++;
        }

        return new AuditVerificationResult(true, null);
    }
}
