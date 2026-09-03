using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Uploads;

namespace SuperScanner.Application.Uploads;

public sealed record CompleteUploadResult(Guid UploadId, UploadIntentState State);

public sealed class CompleteUpload(
    IUploadIntentRepository repository,
    IObjectStore objectStore,
    IProcessingJobQueue jobs,
    IClock clock)
{
    public async Task<CompleteUploadResult> HandleAsync(
        string ownerFirebaseUid,
        Guid documentId,
        Guid uploadId,
        CancellationToken cancellationToken)
    {
        var upload = await repository.FindOwnedUploadAsync(
            ownerFirebaseUid,
            documentId,
            uploadId,
            cancellationToken);
        if (upload is null)
        {
            throw new KeyNotFoundException();
        }

        if (upload.State == UploadIntentState.PendingValidation)
        {
            return new CompleteUploadResult(upload.Id, upload.State);
        }

        if (clock.UtcNow >= upload.ExpiresAt)
        {
            throw new InvalidOperationException("The upload intent has expired.");
        }

        var storedObject = await objectStore.HeadAsync(upload.QuarantineObjectKey, cancellationToken);
        if (storedObject is null ||
            storedObject.SizeBytes != upload.DeclaredSizeBytes ||
            !string.Equals(storedObject.MediaType, upload.DeclaredMediaType, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The quarantined object does not match its declaration.");
        }

        upload.TryMarkPendingValidation(clock.UtcNow);
        await jobs.EnqueueAsync(
            "ValidateUpload",
            upload.Id.ToString(),
            upload.IdempotencyKey,
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return new CompleteUploadResult(upload.Id, upload.State);
    }
}
