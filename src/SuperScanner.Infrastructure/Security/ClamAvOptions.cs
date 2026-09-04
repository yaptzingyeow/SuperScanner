namespace SuperScanner.Infrastructure.Security;

public sealed class ClamAvOptions
{
    public const string SectionName = "ClamAv";

    public string Host { get; init; } = "clamav";
    public int Port { get; init; } = 3310;
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
