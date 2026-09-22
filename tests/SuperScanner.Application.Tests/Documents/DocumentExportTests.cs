using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SuperScanner.Application.Abstractions;
using SuperScanner.Application.Documents;
using SuperScanner.Domain.Documents;
using SuperScanner.Infrastructure.Persistence;
using SuperScanner.Infrastructure.Processing;

namespace SuperScanner.Application.Tests.Documents;

public sealed class DocumentExportTests
{
    [Fact]
    public async Task Create_SnapshotsOnlyActiveReadyPagesInVisibleOrder_AndCommitsAuditAndJob()
    {
        await using var fixture = await PageMutationFixture.CreateAsync(5);
        var document = await fixture.ReloadAsync();
        var pages = document.ActivePages.ToArray();
        foreach (var page in new[] { pages[0], pages[2], pages[4], document.Pages.Single(p => p.RemovedAt != null) })
        {
            page.SetPreview($"private/{page.Id}/revision-0", "private/thumb");
            page.MarkReady();
        }
        pages[1].MarkProcessing();
        pages[3].MarkFailed("crop_failed");
        fixture.Db.Entry(pages[2]).Property(p => p.AppliedCropRevision).CurrentValue = 3;
        fixture.Db.Entry(pages[2]).Property(p => p.AppliedFilter).CurrentValue = "Grayscale";
        await using (var transaction = await fixture.Repository.BeginTransactionAsync(default))
        {
            await fixture.Repository.FindOwnedForUpdateAsync("user-a", document.Id, default);
            document.ReorderPages([pages[4].Id, pages[1].Id, pages[2].Id, pages[3].Id, pages[0].Id], 1, fixture.Clock.UtcNow);
            await fixture.Repository.SaveChangesAsync(default);
            await transaction.CommitAsync(default);
        }
        fixture.Db.ChangeTracker.Clear();
        fixture.SaveCounter.Calls = 0;

        var result = await Create(fixture).HandleAsync("user-a", document.Id, default);

        Assert.Equal("Queued", result.State);
        Assert.Equal(3, result.ReadyPageCount);
        Assert.Equal(2, result.ExcludedPageCount);
        Assert.Equal(2, result.DocumentRevision);
        Assert.False(result.IsOutdated);
        Assert.Null(result.DownloadUrl);
        Assert.Equal($"/api/documents/{document.Id}/exports/{result.Id}", result.StatusUrl);
        fixture.Db.ChangeTracker.Clear();
        var export = await fixture.Db.DocumentExports.SingleAsync();
        var snapshot = JsonSerializer.Deserialize<DocumentExportSnapshotEntry[]>(export.SnapshotJson)!;
        Assert.Equal(new[] { pages[4].Id, pages[2].Id, pages[0].Id }, snapshot.Select(p => p.PageId));
        Assert.Equal(new[] { 1, 3, 5 }, snapshot.Select(p => p.Position));
        Assert.Equal(new[] { 0, 3, 0 }, snapshot.Select(p => p.AppliedCropRevision));
        Assert.Equal(new[] { ScanFilter.Default, "Grayscale", ScanFilter.Default }, snapshot.Select(p => p.AppliedFilter));
        Assert.Equal(new[] { $"private/{pages[4].Id}/revision-0", $"private/{pages[2].Id}/revision-0", $"private/{pages[0].Id}/revision-0" }, snapshot.Select(p => p.ProcessedObjectKey));
        var job = await fixture.Db.ProcessingJobs.SingleAsync();
        Assert.Equal("BuildDocumentPdf", job.Type);
        Assert.Equal(export.Id.ToString(), job.Payload);
        Assert.Equal($"export:{export.Id}:build:v1", job.IdempotencyKey);
        var audit = await fixture.Db.AuditEvents.SingleAsync();
        Assert.Equal("document.export_created", audit.Action);
        Assert.Equal(document.Id, audit.TargetId);
        Assert.Equal("user-a", audit.ActorUid);
        Assert.DoesNotContain("private/", audit.RegionJson);
        Assert.Equal(1, fixture.SaveCounter.Calls);
        Assert.Null(fixture.Db.Database.CurrentTransaction);

        var savedSnapshot = export.SnapshotJson;
        await fixture.Reorder.HandleAsync("user-a", document.Id,
            new ReorderPagesRequest(2, pages.Select(p => p.Id).ToArray()), default);
        var status = await Get(fixture).HandleAsync("user-a", document.Id, export.Id, default);
        Assert.True(status.IsOutdated);
        Assert.Equal(2, status.DocumentRevision);
        Assert.Equal(savedSnapshot, (await fixture.Db.DocumentExports.AsNoTracking().SingleAsync()).SnapshotJson);
    }

    [Fact]
    public async Task Create_RejectsZeroReadyPages_WithoutExportAuditOrJob()
    {
        await using var fixture = await PageMutationFixture.CreateAsync();
        await Assert.ThrowsAsync<DocumentExportNoReadyPagesException>(() =>
            Create(fixture).HandleAsync("user-a", fixture.DocumentId, default));
        await AssertEmptyAsync(fixture);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_HidesMissingAndUnownedDocuments(bool missing)
    {
        await using var fixture = await PageMutationFixture.CreateAsync();
        await Assert.ThrowsAsync<DocumentExportNotFoundException>(() => Create(fixture).HandleAsync(
            "user-b", missing ? Guid.NewGuid() : fixture.DocumentId, default));
        await AssertEmptyAsync(fixture);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("document")]
    [InlineData("export")]
    public async Task Status_HidesOtherOwnersAndMismatchedResources(string mismatch)
    {
        await using var fixture = await ReadyFixtureAsync();
        var export = await Create(fixture).HandleAsync("user-a", fixture.DocumentId, default);
        await Assert.ThrowsAsync<DocumentExportNotFoundException>(() => Get(fixture).HandleAsync(
            mismatch == "owner" ? "user-b" : "user-a",
            mismatch == "document" ? Guid.NewGuid() : fixture.DocumentId,
            mismatch == "export" ? Guid.NewGuid() : export.Id, default));
    }

    [Theory]
    [InlineData("Queued", false, false)]
    [InlineData("Processing", false, false)]
    [InlineData("Ready", false, true)]
    [InlineData("Ready", true, false)]
    [InlineData("Failed", false, false)]
    public async Task Status_ReturnsSafeMetadata_AndDownloadUrlOnlyForUnexpiredReadyExport(
        string state, bool expired, bool downloadable)
    {
        await using var fixture = await ReadyFixtureAsync();
        var created = await Create(fixture).HandleAsync("user-a", fixture.DocumentId, default);
        var export = await fixture.Db.DocumentExports.SingleAsync();
        if (state is "Processing" or "Ready") export.Start(fixture.Clock.UtcNow);
        if (state == "Ready") export.Complete("exports/private/document.pdf", fixture.Clock.UtcNow);
        if (state == "Failed") export.Fail("private exception / secret key", fixture.Clock.UtcNow);
        await fixture.Db.SaveChangesAsync();
        var clock = new ExportClock(expired ? export.ExpiresAt : fixture.Clock.UtcNow);
        var status = await new GetDocumentExport(new EfDocumentExportRepository(fixture.Db), clock)
            .HandleAsync("user-a", fixture.DocumentId, created.Id, default);
        Assert.Equal(state, status.State);
        Assert.Equal(downloadable ? $"{status.StatusUrl}/download" : null, status.DownloadUrl);
        Assert.Equal(state == "Failed" ? "export_build_failed" : null, status.FailureCode);
        var json = JsonSerializer.Serialize(status);
        Assert.DoesNotContain("private", json);
        Assert.DoesNotContain("Snapshot", json);
        Assert.DoesNotContain("ObjectKey", json);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_RollsBackSnapshotAuditAndEnqueuedJob(bool duringSave)
    {
        await using var fixture = await ReadyFixtureAsync();
        fixture.SaveCounter.Fail = duringSave;
        var queue = new PostgresJobQueue(fixture.Db, fixture.Clock);
        var handler = Create(fixture, duringSave ? queue : new FailAfterEnqueue(queue));
        await Assert.ThrowsAsync<IOException>(() => handler.HandleAsync("user-a", fixture.DocumentId, default));
        await AssertEmptyAsync(fixture);
    }

    [Fact]
    public async Task Queue_StandalonePreservesCommitAndIdempotency()
    {
        await using var fixture = await PageMutationFixture.CreateAsync();
        var queue = new PostgresJobQueue(fixture.Db, fixture.Clock);
        await queue.EnqueueAsync("BuildDocumentPdf", "export-id", "unique-export", default);
        await queue.EnqueueAsync("BuildDocumentPdf", "export-id", "unique-export", default);
        fixture.Db.ChangeTracker.Clear();
        Assert.Single(await fixture.Db.ProcessingJobs.ToListAsync());
        Assert.Null(fixture.Db.Database.CurrentTransaction);
    }

    private static CreateDocumentExport Create(PageMutationFixture fixture, IProcessingJobQueue? queue = null) =>
        new(fixture.Repository, new EfDocumentExportRepository(fixture.Db), fixture.Clock, fixture.Audit,
            queue ?? new PostgresJobQueue(fixture.Db, fixture.Clock), new DocumentExportPolicy(7));

    private static GetDocumentExport Get(PageMutationFixture fixture) =>
        new(new EfDocumentExportRepository(fixture.Db), fixture.Clock);

    private static async Task<PageMutationFixture> ReadyFixtureAsync()
    {
        var fixture = await PageMutationFixture.CreateAsync();
        var document = await fixture.ReloadAsync();
        var page = document.ActivePages.First();
        page.SetPreview("private/preview", "private/thumb");
        page.MarkReady();
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        fixture.SaveCounter.Calls = 0;
        return fixture;
    }

    private static async Task AssertEmptyAsync(PageMutationFixture fixture)
    {
        fixture.Db.ChangeTracker.Clear();
        Assert.Empty(await fixture.Db.DocumentExports.ToListAsync());
        Assert.Empty(await fixture.Db.AuditEvents.ToListAsync());
        Assert.Empty(await fixture.Db.ProcessingJobs.ToListAsync());
        Assert.Null(fixture.Db.Database.CurrentTransaction);
    }

    private sealed class ExportClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }

    private sealed class FailAfterEnqueue(IProcessingJobQueue inner) : IProcessingJobQueue
    {
        public async Task EnqueueAsync(string type, string payload, string key, CancellationToken ct)
        {
            await inner.EnqueueAsync(type, payload, key, ct);
            throw new IOException("Injected failure after SQL queue insert");
        }
        public Task<ProcessingJobLease?> TryLeaseAsync(string worker, TimeSpan lease, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> HeartbeatAsync(Guid id, string worker, TimeSpan lease, CancellationToken ct) => throw new NotSupportedException();
        public Task CompleteAsync(Guid id, string worker, CancellationToken ct) => throw new NotSupportedException();
        public Task RescheduleAsync(Guid id, string worker, string code, CancellationToken ct) => throw new NotSupportedException();
    }
}
