using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Auditing;
using SuperScanner.Infrastructure.Persistence;

namespace SuperScanner.Infrastructure.Auditing;

public sealed class HmacAuditWriter : IAuditWriter
{
    private static readonly byte[] GenesisHash = new byte[32];
    private readonly AppDbContext _db;
    private readonly byte[] _signingKey;
    private readonly string _signingKeyId;

    public HmacAuditWriter(AppDbContext db, IOptions<AuditOptions> options)
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

    public async Task<Guid> AppendAsync(
        AuditWriteRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        // A command may own the transaction so its mutation and audit are saved atomically.
        await using var transaction = _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        if (string.Equals(request.TargetType, "document", StringComparison.Ordinal))
        {
            // Document mutations lock the document before appending an audit event. Keep the
            // standalone audit path in that same order before acquiring the chain lock.
            await _db.Documents.FromSqlInterpolated(
                    $"SELECT * FROM documents WHERE \"Id\" = {request.TargetId} FOR UPDATE")
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken);
        }
        var chainKey = request.TargetId.ToString("N");
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({chainKey}, 0))",
            cancellationToken);

        var previous = await _db.AuditEvents
            .Where(auditEvent => auditEvent.TargetId == request.TargetId)
            .OrderByDescending(auditEvent => auditEvent.Sequence)
            .FirstOrDefaultAsync(cancellationToken);
        var sequence = (previous?.Sequence ?? 0) + 1;
        var previousHash = previous?.EventHash ?? GenesisHash;
        var canonicalRegion = CanonicalAuditPayload.CanonicalizeRegionJson(request.RegionJson);
        var canonicalRequest = request with { RegionJson = canonicalRegion };
        var payload = CanonicalAuditPayload.Serialize(canonicalRequest, sequence, _signingKeyId);
        var eventHash = ComputeEventHash(previousHash, payload);
        var signature = HMACSHA256.HashData(_signingKey, eventHash);
        var eventId = Guid.NewGuid();

        _db.AuditEvents.Add(AuditEvent.Create(
            eventId,
            request.TargetId,
            sequence,
            request.ActorUid,
            request.Action,
            request.TargetType,
            canonicalRegion,
            request.OccurredAt.ToUniversalTime(),
            previousHash,
            eventHash,
            signature,
            _signingKeyId));
        if (transaction is not null)
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        return eventId;
    }

    internal static byte[] ComputeEventHash(byte[] previousHash, byte[] canonicalPayload)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(previousHash);
        hash.AppendData(canonicalPayload);
        return hash.GetHashAndReset();
    }

    private static void Validate(AuditWriteRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ActorUid);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Action);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetType);
        if (request.TargetId == Guid.Empty)
        {
            throw new ArgumentException("The audit target ID is required.", nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.RegionJson);
    }
}
