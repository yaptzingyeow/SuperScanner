namespace SuperScanner.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
