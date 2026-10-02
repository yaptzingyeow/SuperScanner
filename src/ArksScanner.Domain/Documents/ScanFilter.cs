namespace ArksScanner.Domain.Documents;

public static class ScanFilter
{
    public const string Default = "Document";

    public static bool IsValid(string? value) =>
        value is "Magic" or "Original" or "Document" or "Bright" or "Grayscale" or "BlackAndWhite" or "RemoveShadows"
            or "CleanDocument" or "CleanDocumentGentle" or "CleanDocumentStrong" or "ContentClean";
}
