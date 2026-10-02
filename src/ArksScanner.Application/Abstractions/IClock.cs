namespace ArksScanner.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
