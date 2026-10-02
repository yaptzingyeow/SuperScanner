using PdfSharp.Pdf;

namespace ArksScanner.Infrastructure.Processing;

public interface IPdfTextLayerWriter
{
    void Write(PdfPage page, IReadOnlyList<PdfTextLayerWord> words);
}

public sealed class PdfTextLayerWriteException(string code)
    : Exception(code)
{
    public string Code { get; } = code;
}
