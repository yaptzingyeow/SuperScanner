namespace SuperScanner.Domain.Uploads;

public enum UploadIntentState
{
    AwaitingUpload,
    PendingValidation
}

public sealed class UploadIntent
{
    private UploadIntent()
    {
    }

    public Guid Id { get; private set; }
    public string OwnerFirebaseUid { get; private set; } = string.Empty;
    public Guid DocumentId { get; private set; }
    public Guid PageId { get; private set; }
    public string QuarantineObjectKey { get; private set; } = string.Empty;
    public string DeclaredMediaType { get; private set; } = string.Empty;
    public long DeclaredSizeBytes { get; private set; }
    public string DeclaredSha256Hex { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public UploadIntentState State { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;

    public static UploadIntent Create(
        Guid id,
        string ownerFirebaseUid,
        Guid documentId,
        Guid pageId,
        string quarantineObjectKey,
        string declaredMediaType,
        long declaredSizeBytes,
        string declaredSha256Hex,
        DateTimeOffset expiresAt) =>
        new()
        {
            Id = id,
            OwnerFirebaseUid = ownerFirebaseUid,
            DocumentId = documentId,
            PageId = pageId,
            QuarantineObjectKey = quarantineObjectKey,
            DeclaredMediaType = declaredMediaType,
            DeclaredSizeBytes = declaredSizeBytes,
            DeclaredSha256Hex = declaredSha256Hex,
            ExpiresAt = expiresAt,
            State = UploadIntentState.AwaitingUpload,
            IdempotencyKey = $"upload:{id}:validate"
        };

    public bool TryMarkPendingValidation(DateTimeOffset now)
    {
        if (State == UploadIntentState.PendingValidation)
        {
            return false;
        }

        if (now >= ExpiresAt)
        {
            throw new InvalidOperationException("The upload intent has expired.");
        }

        State = UploadIntentState.PendingValidation;
        return true;
    }
}
