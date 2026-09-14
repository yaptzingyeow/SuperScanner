namespace SuperScanner.Infrastructure.Processing;

public sealed class DocumentBoundaryHealth
{
    private string? _unhealthyCode;

    public bool CanAttemptAi => Volatile.Read(ref _unhealthyCode) is null;

    public string? UnhealthyCode => Volatile.Read(ref _unhealthyCode);

    public void MarkUnhealthy(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Interlocked.CompareExchange(ref _unhealthyCode, code, null);
    }
}
