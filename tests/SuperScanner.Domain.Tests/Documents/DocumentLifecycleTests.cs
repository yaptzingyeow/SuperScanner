using SuperScanner.Domain.Documents;

namespace SuperScanner.Domain.Tests.Documents;

public sealed class DocumentLifecycleTests
{
    [Fact]
    public void ProcessingAndReadyTransitionsUpdateStatusAndTimestamp()
    {
        var createdAt = DateTimeOffset.Parse("2026-09-02T00:00:00Z");
        var processingAt = createdAt.AddMinutes(1);
        var readyAt = createdAt.AddMinutes(2);
        var document = Document.Create(Guid.NewGuid(), "firebase-user-1", "Form", createdAt);

        document.MarkProcessing(processingAt);

        Assert.Equal(DocumentStatus.Processing, document.Status);
        Assert.Equal(processingAt, document.UpdatedAt);

        document.MarkReady(readyAt);

        Assert.Equal(DocumentStatus.Ready, document.Status);
        Assert.Equal(readyAt, document.UpdatedAt);
    }
}
