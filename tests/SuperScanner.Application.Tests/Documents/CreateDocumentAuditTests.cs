using SuperScanner.Application.Abstractions;
using SuperScanner.Application.Documents;
using SuperScanner.Application.Tests.TestDoubles;
using SuperScanner.Domain.Documents;

namespace SuperScanner.Application.Tests.Documents;

public sealed class CreateDocumentAuditTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_RecordsIdentifierOnlyAuditEvent()
    {
        var repository = new Repository();
        var audit = new RecordingAuditWriter();
        var handler = new CreateDocument(repository, new FixedClock(Now), audit);

        var document = await handler.HandleAsync(
            "user-a",
            "Highly confidential title",
            CancellationToken.None);

        var request = Assert.Single(audit.Requests);
        Assert.Equal("user-a", request.ActorUid);
        Assert.Equal("document.created", request.Action);
        Assert.Equal("document", request.TargetType);
        Assert.Equal(document.Id, request.TargetId);
        Assert.Equal("{}", request.RegionJson);
        Assert.DoesNotContain("confidential", request.RegionJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Now, request.OccurredAt);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class Repository : IDocumentRepository
    {
        public Task AddAsync(Document document, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<Document>> ListByOwnerAsync(
            string ownerFirebaseUid,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
