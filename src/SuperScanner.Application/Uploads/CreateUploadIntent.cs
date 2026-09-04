using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Uploads;
using System.Text.Json;

namespace SuperScanner.Application.Uploads;

public sealed record UploadPolicy(int MaxPages, long MaxSizeBytes);

public sealed record CreateUploadRequest(
    string FileName,
    string MediaType,
    long SizeBytes,
    string Sha256Hex);

public sealed record UploadIntentDto(
    Guid UploadId,
    Guid PageId,
    Uri PutUrl,
    DateTimeOffset ExpiresAt);

public sealed class CreateUploadIntent(
    IUploadIntentRepository repository,
    IObjectStore objectStore,
    IClock clock,
    UploadPolicy policy,
    IAuditWriter audit)
{
    private static readonly HashSet<string> SupportedMediaTypes = new(StringComparer.Ordinal)
    {
        "application/pdf",
        "image/jpeg",
        "image/png",
        "image/heic"
    };

    public async Task<UploadIntentDto> HandleAsync(
        string ownerFirebaseUid,
        Guid documentId,
        CreateUploadRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var document = await repository.FindOwnedDocumentAsync(
            ownerFirebaseUid,
            documentId,
            cancellationToken);
        if (document is null)
        {
            throw new KeyNotFoundException();
        }

        var uploadId = Guid.NewGuid();
        var pageId = Guid.NewGuid();
        var expiresAt = clock.UtcNow.AddMinutes(5);
        var quarantineKey = $"quarantine/{documentId:N}/{uploadId:N}";
        var page = document.AddPage(pageId, policy.MaxPages, clock.UtcNow);

        var uploadIntent = UploadIntent.Create(
            uploadId,
            ownerFirebaseUid,
            documentId,
            pageId,
            quarantineKey,
            request.MediaType,
            request.SizeBytes,
            request.Sha256Hex,
            expiresAt);
        var putUrl = await objectStore.CreatePutUrlAsync(
            new PutObjectRequest(quarantineKey, request.MediaType, request.SizeBytes, expiresAt),
            cancellationToken);

        await repository.AddAsync(uploadIntent, page, cancellationToken);
        await audit.AppendAsync(
            new AuditWriteRequest(
                ownerFirebaseUid,
                "upload.intent_created",
                "document",
                documentId,
                JsonSerializer.Serialize(new { uploadId, pageId }),
                clock.UtcNow),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return new UploadIntentDto(uploadId, pageId, putUrl, expiresAt);
    }

    private void ValidateRequest(CreateUploadRequest request)
    {
        if (!SupportedMediaTypes.Contains(request.MediaType))
        {
            throw new ArgumentException("The declared media type is not supported.", nameof(request));
        }

        if (request.SizeBytes <= 0 || request.SizeBytes > policy.MaxSizeBytes)
        {
            throw new ArgumentException("The declared file size is outside the allowed range.", nameof(request));
        }

        if (request.Sha256Hex.Length != 64 ||
            request.Sha256Hex.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("The SHA-256 value must be 64 lowercase hexadecimal characters.", nameof(request));
        }
    }
}
