using System.Text.RegularExpressions;

namespace ArksScanner.Application.Ocr;

/// <summary>OCR language: "auto" (the provider detects it) or a BCP-47 style code such as "zh-Hant".</summary>
public static partial class OcrLanguage
{
    public const string Auto = "auto";

    public static bool IsValid(string? language) =>
        language == Auto || (language is { Length: > 0 and <= 8 } && Code().IsMatch(language));

    /// <summary>Hints for the provider: none when detecting automatically.</summary>
    public static IReadOnlyList<string> Hints(string language) => language == Auto ? [] : [language];

    [GeneratedRegex("^[a-z]{2,3}(-[A-Za-z0-9]{2,4})?$")]
    private static partial Regex Code();
}
