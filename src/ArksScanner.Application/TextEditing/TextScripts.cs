using System.Globalization;
using System.Text;

namespace ArksScanner.Application.TextEditing;

/// <summary>Which writing systems (ISO 15924) a piece of text needs, and whether a font covers them.</summary>
public static class TextScripts
{
    private static readonly (int From, int To, string Script)[] Ranges =
    [
        (0x0041, 0x024F, "Latn"), (0x1E00, 0x1EFF, "Latn"),
        (0x0370, 0x03FF, "Grek"), (0x1F00, 0x1FFF, "Grek"),
        (0x0400, 0x052F, "Cyrl"),
        (0x0590, 0x05FF, "Hebr"),
        (0x0600, 0x06FF, "Arab"), (0x0750, 0x077F, "Arab"), (0x08A0, 0x08FF, "Arab"), (0xFB50, 0xFDFF, "Arab"), (0xFE70, 0xFEFF, "Arab"),
        (0x0900, 0x097F, "Deva"),
        (0x0980, 0x09FF, "Beng"),
        (0x0B80, 0x0BFF, "Taml"),
        (0x0E00, 0x0E7F, "Thai"),
        (0x1100, 0x11FF, "Hang"), (0x3130, 0x318F, "Hang"), (0xAC00, 0xD7AF, "Hang"),
        (0x3040, 0x309F, "Hira"),
        (0x30A0, 0x30FF, "Kana"), (0x31F0, 0x31FF, "Kana"),
        (0x3400, 0x4DBF, "Hani"), (0x4E00, 0x9FFF, "Hani"), (0xF900, 0xFAFF, "Hani"), (0x20000, 0x2FA1F, "Hani"),
    ];

    /// <summary>Scripts of the letters in <paramref name="text"/>; digits, spaces and punctuation need none.</summary>
    public static IReadOnlySet<string> Required(string text)
    {
        var scripts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rune in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is not (UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or
                UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or
                UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark))
                continue;
            foreach (var (from, to, script) in Ranges)
                if (rune.Value >= from && rune.Value <= to) { scripts.Add(script); break; }
        }
        return scripts;
    }

    /// <summary>True when a font listing <paramref name="fontScripts"/> (none = Latin) can draw every letter.</summary>
    public static bool Covers(IReadOnlyCollection<string> fontScripts, string text)
    {
        var available = fontScripts.Count == 0 ? ["Latn"] : fontScripts;
        return Required(text).All(available.Contains);
    }
}
