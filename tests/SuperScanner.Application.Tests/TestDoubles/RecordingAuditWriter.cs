using SuperScanner.Application.Abstractions;

namespace SuperScanner.Application.Tests.TestDoubles;

internal sealed class RecordingAuditWriter : IAuditWriter
{
    public List<AuditWriteRequest> Requests { get; } = [];

    public Task<Guid> AppendAsync(AuditWriteRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(Guid.NewGuid());
    }
}
