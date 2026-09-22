using System.Security.Cryptography;
using System.Text;

namespace SuperScanner.Application.Ocr;

public static class OcrSourceFingerprint
{
    public static string Create(string sourceObjectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceObjectKey);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceObjectKey)))
            .ToLowerInvariant();
    }
}
