using SuperScanner.Application.Abstractions;

namespace SuperScanner.Application.Uploads;

public sealed record UploadStatusDto(
    Guid UploadId,
    string State,
    int DiscoveredPageCount,
    int CreatedPageCount,
    int FailedPageCount,
    string? ErrorCode);

public sealed class GetUploadStatus(IUploadIntentRepository repository)
{
    public async Task<UploadStatusDto> HandleAsync(
        string ownerFirebaseUid,
        Guid documentId,
        Guid uploadId,
        CancellationToken cancellationToken)
    {
        var upload = await repository.FindOwnedUploadAsync(
            ownerFirebaseUid,
            documentId,
            uploadId,
            cancellationToken) ?? throw new KeyNotFoundException();

        return new UploadStatusDto(
            upload.Id,
            upload.State.ToString(),
            upload.DiscoveredPageCount,
            upload.CreatedPageCount,
            upload.FailedPageCount,
            upload.ExpansionErrorCode ?? upload.ValidationErrorCode);
    }
}
