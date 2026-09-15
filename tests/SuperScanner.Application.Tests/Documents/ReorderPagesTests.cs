using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SuperScanner.Application.Abstractions;
using SuperScanner.Application.Documents;
using SuperScanner.Domain.Documents;
using SuperScanner.Infrastructure.Auditing;
using SuperScanner.Infrastructure.Persistence;

namespace SuperScanner.Application.Tests.Documents;

public sealed class ReorderPagesTests
{
    [Fact]
    public async Task AuditWriter_StandaloneCommitsAndPreservesChainAcrossJoinedMutation()
    {
        await using var fixture = await PageMutationFixture.CreateAsync();
        await fixture.Audit.AppendAsync(new AuditWriteRequest("user-a", "document.created", "document",
            fixture.DocumentId, "{}", fixture.Clock.UtcNow), default);
        Assert.Null(fixture.Db.Database.CurrentTransaction);
        fixture.Db.ChangeTracker.Clear();
        Assert.Single(await fixture.Db.AuditEvents.AsNoTracking().ToListAsync());

        await fixture.Reorder.HandleAsync("user-a", fixture.DocumentId,
            new ReorderPagesRequest(1, fixture.PageIds.Reverse().ToArray()), default);
        await fixture.Audit.AppendAsync(new AuditWriteRequest("user-a", "document.viewed", "document",
            fixture.DocumentId, "{}", fixture.Clock.UtcNow), default);
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(new long[] { 1, 2, 3 }, await fixture.Db.AuditEvents.OrderBy(e => e.Sequence).Select(e => e.Sequence).ToArrayAsync());
        var verifier = new HmacAuditVerifier(fixture.Db, Options.Create(new AuditOptions
        {
            SigningKeyBase64 = Convert.ToBase64String(new byte[32]), SigningKeyId = "test"
        }));
        Assert.True((await verifier.VerifyDocumentChainAsync(fixture.DocumentId, default)).IsValid);
    }

    [Fact]
    public async Task Reorder_PersistsOrderRevisionsAndAtomicIdentifierOnlyAudit()
    {
        await using var fixture = await PageMutationFixture.CreateAsync();
        var ids = fixture.PageIds;
        var result = await fixture.Reorder.HandleAsync("user-a", fixture.DocumentId,
            new ReorderPagesRequest(1, [ids[2], ids[0], ids[1]]), default);
        Assert.Equal(new[] { ids[2], ids[0], ids[1] }, result.Pages.Select(p => p.Id));
        Assert.Equal(new[] { 1, 2, 3 }, result.Pages.Select(p => p.Position));
        Assert.Equal(2, result.Revision);
        Assert.Equal(2, result.PageOrderRevision);
        var document = await fixture.ReloadAsync();
        Assert.Equal(new[] { ids[2], ids[0], ids[1] }, document.ActivePages.Select(p => p.Id));
        var audit = await fixture.Db.AuditEvents.SingleAsync();
        Assert.Equal("document.pages_reordered", audit.Action);
        Assert.Equal(fixture.DocumentId, audit.TargetId);
        Assert.Equal("user-a", audit.ActorUid);
        Assert.DoesNotContain("Confidential", audit.RegionJson);
        Assert.Equal(1, fixture.SaveCounter.Calls);
    }

    [Fact]
    public async Task StaleRevision_ReportsCurrentRevisionAndLeavesStateUnchanged()
    {
        await using var fixture = await PageMutationFixture.CreateAsync();
        var exception = await Assert.ThrowsAsync<PageOrderConflictException>(() => fixture.Reorder.HandleAsync(
            "user-a", fixture.DocumentId, new ReorderPagesRequest(0, fixture.PageIds.Reverse().ToArray()), default));
        Assert.Equal(1, exception.PageOrderRevision);
        await fixture.AssertUnchangedAsync();
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("removed")]
    [InlineData("null")]
    [InlineData("missing-revision")]
    [InlineData("negative-revision")]
    public async Task InvalidOrder_IsRejectedWithoutChangingPagesOrAudit(string invalid)
    {
        await using var fixture = await PageMutationFixture.CreateAsync();
        var ids = fixture.PageIds;
        IReadOnlyList<Guid>? requested = invalid switch
        {
            "duplicate" => [ids[0], ids[0], ids[2]],
            "missing" => [ids[0], ids[1]],
            "foreign" => [ids[0], ids[1], fixture.ForeignPageId],
            "removed" => [ids[0], ids[1], fixture.RemovedPageId],
            "null" => null,
            _ => ids
        };
        long? revision = invalid == "missing-revision" ? null : invalid == "negative-revision" ? -1 : 1;
        await Assert.ThrowsAnyAsync<ArgumentException>(() => fixture.Reorder.HandleAsync(
            "user-a", fixture.DocumentId, new ReorderPagesRequest(revision, requested), default));
        await fixture.AssertUnchangedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrUnownedDocument_IsHidden(bool missing)
    {
        await using var fixture = await PageMutationFixture.CreateAsync();
        await Assert.ThrowsAsync<PageManagementNotFoundException>(() => fixture.Reorder.HandleAsync(
            "user-b", missing ? Guid.NewGuid() : fixture.DocumentId,
            new ReorderPagesRequest(1, fixture.PageIds), default));
        await fixture.AssertUnchangedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_RollsBackPagesRevisionsAndAudit(bool failDuringSave)
    {
        await using var fixture = await PageMutationFixture.CreateAsync();
        fixture.SaveCounter.Fail = failDuringSave;
        var handler = failDuringSave ? fixture.Reorder : new ReorderPages(fixture.Repository,
            fixture.Clock, new FailingAuditWriter(fixture.Audit));
        await Assert.ThrowsAsync<IOException>(() => handler.HandleAsync("user-a", fixture.DocumentId,
            new ReorderPagesRequest(1, fixture.PageIds.Reverse().ToArray()), default));
        await fixture.AssertUnchangedAsync();
    }
}

internal sealed class PageMutationFixture : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    public AppDbContext Db { get; }
    public SaveCounter SaveCounter { get; } = new();
    public EfDocumentRepository Repository { get; }
    public IClock Clock { get; } = new MutationClock();
    public HmacAuditWriter Audit { get; }
    public ReorderPages Reorder => new(Repository, Clock, Audit);
    public RemovePage Remove => new(Repository, Clock, Audit);
    public Guid DocumentId { get; private set; }
    public Guid[] PageIds { get; private set; } = [];
    public Guid ForeignPageId { get; private set; }
    public Guid RemovedPageId { get; private set; }

    private PageMutationFixture(SqliteConnection connection)
    {
        this.connection = connection;
        Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection)
            .AddInterceptors(new SqliteLockTranslation(), SaveCounter).Options);
        Repository = new EfDocumentRepository(Db);
        Audit = new HmacAuditWriter(Db, Options.Create(new AuditOptions
        {
            SigningKeyBase64 = Convert.ToBase64String(new byte[32]), SigningKeyId = "test"
        }));
    }

    public static async Task<PageMutationFixture> CreateAsync(int pageCount = 3)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var fixture = new PageMutationFixture(connection);
        await fixture.Db.Database.EnsureCreatedAsync();
        var document = Document.Create(Guid.NewGuid(), "user-a", "Confidential scan", fixture.Clock.UtcNow);
        var pages = document.AppendImportedPages(Guid.NewGuid(), Enumerable.Range(1, pageCount + 1).ToArray(),
            50, fixture.Clock.UtcNow);
        fixture.PageIds = pages.Take(pageCount).Select(p => p.Id).ToArray();
        // Seed a removed historical page without changing the active document revision.
        var removed = pages.Last();
        removed.SoftRemove("user-a", fixture.Clock.UtcNow);
        fixture.RemovedPageId = removed.Id;
        var foreign = Document.Create(Guid.NewGuid(), "user-b", "Other", fixture.Clock.UtcNow);
        fixture.ForeignPageId = foreign.AddPage(Guid.NewGuid(), 50, fixture.Clock.UtcNow).Id;
        fixture.DocumentId = document.Id;
        fixture.Db.AddRange(document, removed, foreign);
        await fixture.Db.SaveChangesAsync();
        fixture.SaveCounter.Calls = 0;
        fixture.Db.ChangeTracker.Clear();
        return fixture;
    }

    public async Task<Document> ReloadAsync()
    {
        Db.ChangeTracker.Clear();
        return await Db.Documents.Include(d => d.Pages).SingleAsync(d => d.Id == DocumentId);
    }

    public async Task AssertUnchangedAsync()
    {
        var document = await ReloadAsync();
        Assert.Equal(PageIds, document.ActivePages.Select(p => p.Id));
        Assert.Equal(Enumerable.Range(1, PageIds.Length), document.ActivePages.Select(p => p.Position));
        Assert.Equal(1, document.Revision);
        Assert.Equal(1, document.PageOrderRevision);
        Assert.Empty(await Db.AuditEvents.ToListAsync());
        Assert.Null(Db.Database.CurrentTransaction);
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await connection.DisposeAsync();
    }
}

internal sealed class MutationClock : IClock
{
    public DateTimeOffset UtcNow => new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);
}

internal sealed class SaveCounter : SaveChangesInterceptor
{
    public int Calls { get; set; }
    public bool Fail { get; set; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Calls++;
        return ValueTask.FromResult(result);
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
        int result, CancellationToken cancellationToken = default)
    {
        // Fail after SQL writes succeeded, proving the outer transaction rolls all of them back.
        if (Fail) throw new IOException("Injected failure after database save");
        return ValueTask.FromResult(result);
    }
}

// SQLite exercises relational atomicity and immediate unique constraints, not PostgreSQL locking.
internal sealed class SqliteLockTranslation : DbCommandInterceptor
{
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        command.CommandText = command.CommandText.Replace(" FOR UPDATE", "", StringComparison.Ordinal);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(command.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal)
            ? InterceptionResult<int>.SuppressWithResult(0) : result);
}

internal sealed class FailingAuditWriter(IAuditWriter inner) : IAuditWriter
{
    public async Task<Guid> AppendAsync(AuditWriteRequest request, CancellationToken ct)
    {
        await inner.AppendAsync(request, ct);
        throw new IOException("Injected audit failure");
    }
}
