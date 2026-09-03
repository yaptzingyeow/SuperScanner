using SuperScanner.Application.Abstractions;

namespace SuperScanner.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
