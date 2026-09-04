using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SuperScanner.Application.Abstractions;
using SuperScanner.Infrastructure.Auditing;
using SuperScanner.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SuperScanner.Infrastructure.IntegrationTests.Auditing;

public sealed class AuditChainTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 4, 0, 0, TimeSpan.Zero);
    private static readonly Guid DocumentId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private DbContextOptions<AppDbContext> _dbOptions = null!;
    private IOptions<AuditOptions> _auditOptions = null!;

    [Fact]
    public async Task VerificationFailsAfterStoredEventMutation()
    {
        await using (var writeDb = CreateDb())
        {
            var writer = new HmacAuditWriter(writeDb, _auditOptions);
            await writer.AppendAsync(
                new AuditWriteRequest(
                    "user-a",
                    "document.created",
                    "document",
                    DocumentId,
                    "{}",
                    Now),
                CancellationToken.None);
            await writer.AppendAsync(
                new AuditWriteRequest(
                    "user-a",
                    "upload.accepted",
                    "document",
                    DocumentId,
                    "{\"pageId\":\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\"}",
                    Now.AddSeconds(1)),
                CancellationToken.None);
        }

        await using (var validDb = CreateDb())
        {
            var valid = await new HmacAuditVerifier(validDb, _auditOptions)
                .VerifyDocumentChainAsync(DocumentId, CancellationToken.None);
            Assert.True(valid.IsValid);
        }

        await using (var tamperDb = CreateDb())
        {
            await tamperDb.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE audit_events
                SET "Action" = {"upload.replaced"}
                WHERE "TargetId" = {DocumentId} AND "Sequence" = {2L}
                """);
        }

        await using var verifyDb = CreateDb();
        var verifier = new HmacAuditVerifier(verifyDb, _auditOptions);
        var result = await verifier.VerifyDocumentChainAsync(DocumentId, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.FirstInvalidSequence);
    }

    [Fact]
    public async Task ConcurrentAppends_ReceiveDistinctOrderedSequences()
    {
        async Task AppendAsync(string actorUid)
        {
            await using var db = CreateDb();
            await new HmacAuditWriter(db, _auditOptions).AppendAsync(
                new AuditWriteRequest(
                    actorUid,
                    "upload.completed",
                    "document",
                    DocumentId,
                    "{}",
                    Now),
                CancellationToken.None);
        }

        await Task.WhenAll(AppendAsync("user-a"), AppendAsync("user-b"));

        await using var verifyDb = CreateDb();
        var sequences = await verifyDb.AuditEvents
            .AsNoTracking()
            .OrderBy(auditEvent => auditEvent.Sequence)
            .Select(auditEvent => auditEvent.Sequence)
            .ToListAsync();
        var verification = await new HmacAuditVerifier(verifyDb, _auditOptions)
            .VerifyDocumentChainAsync(DocumentId, CancellationToken.None);

        Assert.Equal(new long[] { 1, 2 }, sequences);
        Assert.True(verification.IsValid);
    }

    [Fact]
    public void VerifierRejectsSigningKeysShorterThan256Bits()
    {
        using var db = CreateDb();
        var weakOptions = Options.Create(new AuditOptions
        {
            SigningKeyBase64 = Convert.ToBase64String(new byte[16]),
            SigningKeyId = "weak-key"
        });

        Assert.Throws<ArgumentException>(() => new HmacAuditVerifier(db, weakOptions));
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        _auditOptions = Options.Create(new AuditOptions
        {
            SigningKeyBase64 = Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray()),
            SigningKeyId = "test-key-1"
        });
        await using var db = CreateDb();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private AppDbContext CreateDb() => new(_dbOptions);
}
