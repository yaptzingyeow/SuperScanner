using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SuperScanner.Infrastructure.Processing;

public static class DocumentBoundaryRollout
{
    public static bool ShouldUseAi(Guid pageId, int percentage)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(percentage);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percentage, 100);
        if (percentage == 0) return false;
        if (percentage == 100) return true;

        Span<byte> idBytes = stackalloc byte[16];
        pageId.TryWriteBytes(idBytes, bigEndian: true, out _);
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(idBytes, digest);
        return BinaryPrimitives.ReadUInt32BigEndian(digest) % 100 < percentage;
    }
}
