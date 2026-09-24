using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace SuperScanner.Infrastructure.Processing;

public sealed class PdfSharpTextLayerWriter(
    string fontPath,
    PdfTextLayerLimits? limits = null) : IPdfTextLayerWriter
{
    private const string FamilyName = "SuperScanner Noto Sans";
    private readonly PdfTextLayerLimits limits = limits ?? PdfTextLayerLimits.Default;

    public void Write(PdfPage page, IReadOnlyList<PdfTextLayerWord> words)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(words);
        if (words.Count > limits.MaximumWordsPerPage)
            throw new PdfTextLayerWriteException("pdf_text_size_limit");

        ValidateWords(words);
        EnsureFontAvailable();
        if (words.Count == 0) return;

        try
        {
            using (var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
            {
                foreach (var word in words.OrderBy(item => item.ReadingOrder))
                    DrawWord(graphics, page, word);
            }

            MakeLastContentStreamInvisible(page);
        }
        catch (PdfTextLayerWriteException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new PdfTextLayerWriteException("pdf_text_write_failed");
        }
    }

    private void DrawWord(XGraphics graphics, PdfPage page, PdfTextLayerWord word)
    {
        var fontSize = Math.Clamp(word.Height * .85, 4, 96);
        var font = new XFont(
            FamilyName,
            fontSize,
            XFontStyleEx.Regular,
            new XPdfFontOptions(PdfFontEncoding.Unicode, PdfFontEmbedding.EmbedCompleteFontFile));
        var measured = graphics.MeasureString(word.Text, font).Width;
        if (!double.IsFinite(measured) || measured <= 0)
            throw new PdfTextLayerWriteException("pdf_text_glyph_unsupported");

        var minimumScale = limits.MinimumHorizontalScalePercent / 100;
        var maximumScale = limits.MaximumHorizontalScalePercent / 100;
        var scale = Math.Clamp(word.Width / measured, minimumScale, maximumScale);
        var baseline = new XPoint(word.X, page.Height.Point - word.Y);
        var state = graphics.Save();
        try
        {
            if (word.AngleDegrees != 0)
                graphics.RotateAtTransform(-word.AngleDegrees, baseline);
            if (Math.Abs(scale - 1) > .001)
                graphics.ScaleAtTransform(scale, 1, baseline);
            graphics.DrawString(word.Text, font, XBrushes.Black, baseline, XStringFormats.BaseLineLeft);
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    private void ValidateWords(IReadOnlyList<PdfTextLayerWord> words)
    {
        var characters = 0;
        foreach (var word in words)
        {
            if (word.Text.Any(char.IsSurrogate))
                throw new PdfTextLayerWriteException("pdf_text_glyph_unsupported");

            if (string.IsNullOrWhiteSpace(word.Text) ||
                word.Text.Length > limits.MaximumCharactersPerWord ||
                !Finite(word.X, word.Y, word.Width, word.Height, word.AngleDegrees) ||
                word.X < 0 || word.Y < 0 || word.Width <= 0 || word.Height <= 0 ||
                word.ReadingOrder < 0)
            {
                throw new PdfTextLayerWriteException("pdf_text_geometry_invalid");
            }

            characters += word.Text.Length;
            if (characters > limits.MaximumCharactersPerPage)
                throw new PdfTextLayerWriteException("pdf_text_size_limit");
        }
    }

    private void EnsureFontAvailable()
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(fontPath);
            if (bytes.Length < 12 || bytes.All(value => value == 0))
                throw new InvalidDataException();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PdfTextLayerWriteException("pdf_text_font_unavailable");
        }
        catch (InvalidDataException)
        {
            throw new PdfTextLayerWriteException("pdf_text_font_unavailable");
        }

        BundledFontResolver.Install(bytes);
    }

    private static void MakeLastContentStreamInvisible(PdfPage page)
    {
        var item = page.Contents.Elements.Last();
        var content = item is PdfReference reference
            ? (PdfDictionary)reference.Value
            : (PdfDictionary)item;
        var original = content.Stream?.Value ?? throw new PdfTextLayerWriteException("pdf_text_write_failed");
        var prefix = Encoding.ASCII.GetBytes("3 Tr\n");
        var updated = new byte[prefix.Length + original.Length];
        Buffer.BlockCopy(prefix, 0, updated, 0, prefix.Length);
        Buffer.BlockCopy(original, 0, updated, prefix.Length, original.Length);
        content.Stream.Value = updated;
    }

    private static bool Finite(params double[] values) => values.All(double.IsFinite);

    private sealed class BundledFontResolver(byte[] bytes) : IFontResolver
    {
        private const string FaceName = "superscanner-noto-sans-regular";
        private static readonly object Sync = new();
        private static BundledFontResolver? installed;
        private readonly byte[] bytes = bytes;

        public static void Install(byte[] bytes)
        {
            lock (Sync)
            {
                if (installed is not null) return;
                installed = new BundledFontResolver(bytes);
                GlobalFontSettings.FontResolver = installed;
            }
        }

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
            familyName.Equals(FamilyName, StringComparison.OrdinalIgnoreCase)
                ? new FontResolverInfo(FaceName, false, italic)
                : null;

        public byte[]? GetFont(string faceName) =>
            faceName == FaceName ? bytes : null;
    }
}
