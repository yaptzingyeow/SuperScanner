using System.Data;
using Microsoft.EntityFrameworkCore;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Processing;
using SuperScanner.Infrastructure.Persistence;

namespace SuperScanner.Infrastructure.Processing;

public sealed class PostgresJobQueue(AppDbContext db, IClock clock) : IProcessingJobQueue
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30)
    ];

    public async Task EnqueueAsync(
        string type,
        string payload,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var now = clock.UtcNow;
        var queued = ProcessingJobStatus.Queued.ToString();
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO processing_jobs
                ("Id", "Type", "Payload", "IdempotencyKey", "Status", "CreatedAt",
                 "AvailableAt", "UpdatedAt", "AttemptCount", "WorkerId", "LeaseExpiresAt", "ErrorCode")
            VALUES
                ({id}, {type}, {payload}, {idempotencyKey}, {queued}, {now},
                 {now}, {now}, {0}, {null}, {null}, {null})
            ON CONFLICT ("IdempotencyKey") DO NOTHING
            """, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ProcessingJobLease?> TryLeaseAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var queued = ProcessingJobStatus.Queued.ToString();
        var leased = ProcessingJobStatus.Leased.ToString();
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var job = await db.ProcessingJobs
            .FromSqlInterpolated($"""
                SELECT *
                FROM processing_jobs
                WHERE ("Status" = {queued} AND "AvailableAt" <= {now})
                   OR ("Status" = {leased} AND "LeaseExpiresAt" <= {now})
                ORDER BY "CreatedAt", "Id"
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (job is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        job.Lease(workerId, now, leaseDuration);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ProcessingJobLease(
            job.Id,
            job.Type,
            job.Payload,
            job.AttemptCount,
            job.LeaseExpiresAt!.Value);
    }

    public async Task<bool> HeartbeatAsync(
        Guid jobId,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        var now = clock.UtcNow;
        var updated = await db.ProcessingJobs
            .Where(job =>
                job.Id == jobId &&
                job.Status == ProcessingJobStatus.Leased &&
                job.WorkerId == workerId &&
                job.LeaseExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.LeaseExpiresAt, now.Add(leaseDuration))
                    .SetProperty(job => job.UpdatedAt, now),
                cancellationToken);
        return updated == 1;
    }

    public Task CompleteAsync(Guid jobId, string workerId, CancellationToken cancellationToken) =>
        MutateOwnedLeaseAsync(
            jobId,
            workerId,
            job => job.Complete(workerId, clock.UtcNow),
            cancellationToken);

    public Task RescheduleAsync(
        Guid jobId,
        string workerId,
        string errorCode,
        CancellationToken cancellationToken) =>
        MutateOwnedLeaseAsync(
            jobId,
            workerId,
            job => job.Reschedule(workerId, clock.UtcNow, errorCode, RetryDelays),
            cancellationToken);

    private async Task MutateOwnedLeaseAsync(
        Guid jobId,
        string workerId,
        Action<ProcessingJob> mutate,
        CancellationToken cancellationToken)
    {
        var job = await db.ProcessingJobs.SingleOrDefaultAsync(
            candidate => candidate.Id == jobId &&
                         candidate.Status == ProcessingJobStatus.Leased &&
                         candidate.WorkerId == workerId,
            cancellationToken);
        if (job is null)
        {
            throw new InvalidOperationException("The processing job lease is not owned by this worker.");
        }

        mutate(job);
        await db.SaveChangesAsync(cancellationToken);
    }
}
