namespace SuperScanner.Infrastructure.Processing;

public sealed record PdfInspection(int PageCount, bool IsEncrypted, long EstimatedDecodedPixels);

public sealed class PdfImportException(string code, Exception? innerException = null)
    : Exception(code, innerException)
{
    public string Code { get; } = code;
}

public interface IPdfImportTool
{
    Task<PdfInspection> InspectAsync(string sourcePath, CancellationToken ct);
    Task RenderPageAsync(string sourcePath, int pageIndex, string outputPngPath, CancellationToken ct);
}
