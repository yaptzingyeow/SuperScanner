using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ArksScanner.Application.Abstractions;
using ArksScanner.Domain.Documents;
using ArksScanner.Infrastructure.Persistence;

namespace ArksScanner.Infrastructure.Signatures;

public sealed class SignatureAssetCleanup(AppDbContext db, IObjectStore store, IClock clock)
{
    public async Task RunAsync(CancellationToken ct)
    {
        await CleanupWriteIntentsAsync(ct);
        var eligible = db.PageSignatures.AsNoTracking().Where(s => s.AssetPurgedAt == null &&
            (s.DeletedAt != null || db.Pages.Any(p => p.Id == s.PageId && p.RemovedAt != null)));
        var count = await eligible.CountAsync(ct);
        if (count == 0) return;
        // Rotate bounded hourly batches so retained assets cannot starve later candidates.
        var offset = (int)((clock.UtcNow.ToUnixTimeSeconds() / 3600) % ((count + 99L) / 100)) * 100;
        var candidates = await eligible.OrderBy(s => s.Id).Skip(offset)
            .Select(s => new { s.Id, s.DocumentId }).Take(100).ToListAsync(ct);
        foreach (var candidate in candidates)
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Same document lock as export creation and signature mutations: no new reference
            // can race the liveness check and private object deletion.
            var document = await db.Documents.FromSqlInterpolated(
                $"SELECT * FROM documents WHERE \"Id\" = {candidate.DocumentId} FOR UPDATE").SingleOrDefaultAsync(ct);
            if (document is null) continue;
            var signature = await db.PageSignatures.SingleOrDefaultAsync(s => s.Id == candidate.Id, ct);
            if (signature is null || signature.AssetPurgedAt is not null) continue;
            if (signature.DeletedAt is null)
            {
                if (!await db.Pages.AnyAsync(p => p.Id == signature.PageId && p.RemovedAt != null, ct)) continue;
                signature.Delete(signature.Revision, clock.UtcNow);
                await db.SaveChangesAsync(ct);
            }
            if (await db.PageSignatures.AnyAsync(s => s.AssetKey == signature.AssetKey && s.DeletedAt == null, ct)) continue;
            var exports = await db.DocumentExports.AsNoTracking().Where(e => e.DocumentId == document.Id &&
                (e.ExpiresAt > clock.UtcNow || e.State == DocumentExportState.Queued || e.State == DocumentExportState.Processing))
                .Select(e => e.SnapshotJson).ToListAsync(ct);
            // Malformed snapshots throw before deletion: fail closed, never guess.
            if (exports.Any(json => (JsonSerializer.Deserialize<DocumentExportSnapshotEntry[]>(json)
                ?? throw new InvalidDataException("Invalid export snapshot.")).Any(page =>
                    page.Signatures.Any(s => s.AssetKey == signature.AssetKey))))
            { await transaction.CommitAsync(ct); continue; }
            await store.DeleteAsync(signature.AssetKey, ct);
            signature.MarkAssetPurged(clock.UtcNow);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
    }

    private async Task CleanupWriteIntentsAsync(CancellationToken ct)
    {
        var intents = await db.SignatureAssetWriteIntents.AsNoTracking()
            .Where(i => i.CreatedAt < clock.UtcNow.AddDays(-1)).OrderBy(i => i.CreatedAt).Take(100).ToListAsync(ct);
        foreach (var candidate in intents)
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            _ = await db.Documents.FromSqlInterpolated(
                $"SELECT * FROM documents WHERE \"Id\" = {candidate.DocumentId} FOR UPDATE").SingleOrDefaultAsync(ct);
            var intent = await db.SignatureAssetWriteIntents.SingleOrDefaultAsync(i => i.Id == candidate.Id, ct);
            if (intent is null) continue;
            // A committed signature owns the asset, even if an acknowledgement was lost.
            if (!await db.PageSignatures.AnyAsync(s => s.AssetKey == intent.AssetKey, ct))
                await store.DeleteAsync(intent.AssetKey, ct);
            db.SignatureAssetWriteIntents.Remove(intent);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
    }
}
