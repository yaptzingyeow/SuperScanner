using SuperScanner.Application.Abstractions;

namespace SuperScanner.Application.Ocr;

public sealed class GetPageOcr(IOcrRepository repository)
{
    public async Task<PageOcrDto> HandleAsync(
        string ownerUid,
        Guid documentId,
        Guid pageId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUid);
        var source = await repository.FindOwnedSourceAsync(
            ownerUid, documentId, pageId, false, ct)
            ?? throw new OcrResourceNotFoundException();
        if (string.IsNullOrWhiteSpace(source.SourceObjectKey))
            return OcrDtoMapper.Map(null);

        var fingerprint = OcrSourceFingerprint.Create(source.SourceObjectKey);
        var result = await repository.FindBySourceAsync(pageId, fingerprint, false, ct);
        return OcrDtoMapper.Map(result);
    }
}
