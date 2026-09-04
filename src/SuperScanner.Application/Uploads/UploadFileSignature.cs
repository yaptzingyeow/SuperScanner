namespace SuperScanner.Application.Uploads;

public static class UploadFileSignature
{
    public static string DetectMediaType(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith("%PDF-"u8))
        {
            return "application/pdf";
        }

        ReadOnlySpan<byte> pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (header.StartsWith(pngSignature))
        {
            return "image/png";
        }

        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (header.Length >= 12 &&
            header[4..8].SequenceEqual("ftyp"u8) &&
            (header[8..12].SequenceEqual("heic"u8) || header[8..12].SequenceEqual("heix"u8)))
        {
            return "image/heic";
        }

        throw new InvalidDataException("Unsupported file signature.");
    }
}
