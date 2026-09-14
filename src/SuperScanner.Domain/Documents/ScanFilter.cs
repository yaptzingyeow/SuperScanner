namespace SuperScanner.Domain.Documents;

public static class ScanFilter
{
    public const string Default = "Document";

    public static bool IsValid(string? value) =>
        value is "Original" or "Document" or "Bright" or "Grayscale" or "BlackAndWhite";
}
