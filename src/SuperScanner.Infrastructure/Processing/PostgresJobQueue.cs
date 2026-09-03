using Microsoft.EntityFrameworkCore;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Processing;
using SuperScanner.Infrastructure.Persistence;

namespace SuperScanner.Infrastructure.Processing;

public sealed class PostgresJobQueue(AppDbContext db, IClock clock) : IProcessingJobQueue
{
    public async Task EnqueueAsync(
        string type,
        string payload,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var alreadyTracked = db.ProcessingJobs.Local.Any(job => job.IdempotencyKey == idempotencyKey);
        var alreadyStored = await db.ProcessingJobs
            .AsNoTracking()
            .AnyAsync(job => job.IdempotencyKey == idempotencyKey, cancellationToken);
        if (alreadyTracked || alreadyStored)
        {
            return;
        }

        db.ProcessingJobs.Add(ProcessingJob.Create(
            Guid.NewGuid(),
            type,
            payload,
            idempotencyKey,
            clock.UtcNow));
    }
}
