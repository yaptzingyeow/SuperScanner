using Microsoft.EntityFrameworkCore;
using ArksScanner.Application.Abstractions;
using ArksScanner.Infrastructure.Persistence;

namespace ArksScanner.Infrastructure.Processing;

public sealed class PageRepairAssetCleanup(AppDbContext db, IObjectStore store, IClock clock)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var cutoff = clock.UtcNow.AddHours(-24);
        var candidates = await (from repair in db.PageRepairOperations.AsNoTracking()
            join page in db.Pages.AsNoTracking() on repair.PageId equals page.Id
            where repair.Kind == "Repair" && repair.CreatedAt < cutoff &&
                (repair.State == "Ready" || repair.State == "Failed")
            orderby repair.CreatedAt
            select new { repair.Id, page.DocumentId }).Take(100).ToListAsync(ct);
        foreach (var candidate in candidates)
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Apply locks the document first. This lock prevents an approved preview
            // from becoming a page revision while its private asset is being retired.
            _ = await db.Documents.FromSqlInterpolated(
                $"SELECT * FROM documents WHERE \"Id\" = {candidate.DocumentId} FOR UPDATE")
                .SingleOrDefaultAsync(ct);
            var operation = await db.PageRepairOperations.SingleOrDefaultAsync(x => x.Id == candidate.Id, ct);
            if (operation is null || operation.Kind != "Repair" || operation.CreatedAt >= cutoff ||
                operation.State is not ("Ready" or "Failed") || operation.AppliedRevisionId is not null)
                continue;
            // Even a failed save may have already uploaded this deterministic key.
            var key = operation.PreviewObjectKey ?? $"repairs/{operation.PageId:N}/{operation.Id:N}.png";
            await store.DeleteAsync(key, ct);
            operation.Expire();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
    }
}
