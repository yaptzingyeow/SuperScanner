using System.Security.Cryptography;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Uploads;

namespace SuperScanner.Application.Uploads;

public sealed record UploadValidationPolicy(long MaxSizeBytes);

public enum UploadValidationOutcome
{
    Accepted,
    Rejected
}

public sealed record UploadValidationResult(UploadValidationOutcome Outcome, string? ErrorCode);

public sealed class ValidateUpload(
    IUploadValidationRepository repository,
    IObjectStore objectStore,
    IClock clock,
    UploadValidationPolicy policy)
{
    public async Task<UploadValidationResult> ValidateAsync(
        Guid uploadId,
        IMalwareScanner malwareScanner,
        CancellationToken cancellationToken)
    {
        var target = await repository.FindAsync(uploadId, cancellationToken)
            ?? throw new KeyNotFoundException();
        var upload = target.Upload;

        if (upload.State == UploadIntentState.Accepted)
        {
            return new UploadValidationResult(UploadValidationOutcome.Accepted, null);
        }

        if (upload.State == UploadIntentState.Rejected)
        {
            return new UploadValidationResult(UploadValidationOutcome.Rejected, upload.ValidationErrorCode);
        }

        if (upload.State != UploadIntentState.PendingValidation)
        {
            throw new InvalidOperationException("The upload is not pending validation.");
        }

        if (clock.UtcNow >= upload.ExpiresAt)
        {
            return await RejectAsync(target, "upload_expired", cancellationToken);
        }

        await using var source = await objectStore.OpenReadAsync(
            upload.QuarantineObjectKey,
            cancellationToken);
        await using var boundedFile = OpenBoundedTemporaryFile();
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var header = new byte[12];
        var headerLength = 0;
        var buffer = new byte[81920];
        long actualSize = 0;

        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            actualSize += bytesRead;
            if (actualSize > policy.MaxSizeBytes)
            {
                return await RejectAsync(target, "size_limit_exceeded", cancellationToken);
            }

            if (headerLength < header.Length)
            {
                var headerBytes = Math.Min(bytesRead, header.Length - headerLength);
                buffer.AsSpan(0, headerBytes).CopyTo(header.AsSpan(headerLength));
                headerLength += headerBytes;
            }

            hasher.AppendData(buffer, 0, bytesRead);
            await boundedFile.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        if (actualSize != upload.DeclaredSizeBytes)
        {
            return await RejectAsync(target, "size_mismatch", cancellationToken);
        }

        string detectedMediaType;
        try
        {
            detectedMediaType = UploadFileSignature.DetectMediaType(header.AsSpan(0, headerLength));
        }
        catch (InvalidDataException)
        {
            return await RejectAsync(target, "signature_mismatch", cancellationToken);
        }

        if (!string.Equals(detectedMediaType, upload.DeclaredMediaType, StringComparison.Ordinal))
        {
            return await RejectAsync(target, "signature_mismatch", cancellationToken);
        }

        var actualSha256 = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        if (!string.Equals(actualSha256, upload.DeclaredSha256Hex, StringComparison.Ordinal))
        {
            return await RejectAsync(target, "hash_mismatch", cancellationToken);
        }

        boundedFile.Position = 0;
        var scan = await malwareScanner.ScanAsync(boundedFile, cancellationToken);
        if (scan.Status == MalwareScanStatus.Infected)
        {
            return await RejectAsync(target, "malware_detected", cancellationToken);
        }

        var originalKey = $"originals/{upload.DocumentId:N}/{upload.PageId:N}/{actualSha256}";
        await objectStore.PromoteAsync(upload.QuarantineObjectKey, originalKey, cancellationToken);
        target.Page.AcceptOriginal(originalKey);
        upload.Accept();
        await repository.SaveChangesAsync(cancellationToken);
        return new UploadValidationResult(UploadValidationOutcome.Accepted, null);
    }

    private async Task<UploadValidationResult> RejectAsync(
        UploadValidationTarget target,
        string errorCode,
        CancellationToken cancellationToken)
    {
        await objectStore.DeleteAsync(target.Upload.QuarantineObjectKey, cancellationToken);
        target.Upload.Reject(errorCode);
        await repository.SaveChangesAsync(cancellationToken);
        return new UploadValidationResult(UploadValidationOutcome.Rejected, errorCode);
    }

    private static FileStream OpenBoundedTemporaryFile() =>
        new(
            Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()),
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                BufferSize = 81920,
                Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose
            });
}
