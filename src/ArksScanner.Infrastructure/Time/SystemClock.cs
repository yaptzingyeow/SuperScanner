using ArksScanner.Application.Abstractions;

namespace ArksScanner.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
