using ArksScanner.Application.TextEditing;

namespace ArksScanner.Application.Tests.TextEditing;

public sealed class TextScriptsTests
{
    [Theory]
    [InlineData("RM 1,500", "Latn")]
    [InlineData("1,500 - 2026/10/05", "")]
    [InlineData("租金 1,500", "Hani")]
    [InlineData("ひらがな カタカナ", "Hira Kana")]
    [InlineData("한국어", "Hang")]
    [InlineData("عقد إيجار", "Arab")]
    [InlineData("שלום", "Hebr")]
    [InlineData("สวัสดี", "Thai")]
    [InlineData("नमस्ते", "Deva")]
    [InlineData("வணக்கம்", "Taml")]
    [InlineData("নমস্কার", "Beng")]
    [InlineData("Café São Paulo", "Latn")]
    public void Detects_the_writing_systems_that_letters_need(string text, string expected)
    {
        Assert.Equal(expected.Split(' ', StringSplitOptions.RemoveEmptyEntries).Order(),
            TextScripts.Required(text).Order());
    }

    [Theory]
    [InlineData("租金 RM 1,500", new[] { "Hans", "Hani", "Latn" }, true)]
    [InlineData("租金", new[] { "Latn" }, false)]
    [InlineData("RM 1,500", new string[0], true)]
    [InlineData("عقد", new string[0], false)]
    [InlineData("12:30", new[] { "Arab" }, true)]
    public void A_font_covers_text_only_when_it_has_every_needed_script(string text, string[] fontScripts, bool expected)
    {
        Assert.Equal(expected, TextScripts.Covers(fontScripts, text));
    }
}
