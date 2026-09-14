namespace SuperScanner.Domain.Uploads;

public enum UploadIntentState
{
    AwaitingUpload,
    PendingValidation,
    Accepted,
    Rejected
}

public sealed class UploadIntent
{
    private UploadIntent()
    {
    }

    public Guid Id { get; private set; }
    public string OwnerFirebaseUid { get; private set; } = string.Empty;
    public Guid DocumentId { get; private set; }
    public Guid? PageId { get; private set; }
    public string QuarantineObjectKey { get; private set; } = string.Empty;
    public string? OriginalFileName { get; private set; }
    public string DeclaredMediaType { get; private set; } = string.Empty;
    public long DeclaredSizeBytes { get; private set; }
    public string DeclaredSha256Hex { get; private set; } = string.Empty;
    public string? AcceptedObjectKey { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public int DiscoveredPageCount { get; private set; }
    public int CreatedPageCount { get; private set; }
    public int FailedPageCount { get; private set; }
    public string? ExpansionErrorCode { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public UploadIntentState State { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string? ValidationErrorCode { get; private set; }

    public static UploadIntent Create(
        Guid id,
        string ownerFirebaseUid,
        Guid documentId,
        string quarantineObjectKey,
        string originalFileName,
        string declaredMediaType,
        long declaredSizeBytes,
        string declaredSha256Hex,
        DateTimeOffset expiresAt) =>
        new()
        {
            Id = id,
            OwnerFirebaseUid = ownerFirebaseUid,
            DocumentId = documentId,
            QuarantineObjectKey = quarantineObjectKey,
            OriginalFileName = originalFileName,
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

    public void Accept(string acceptedObjectKey, DateTimeOffset acceptedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(acceptedObjectKey);
        if (State != UploadIntentState.PendingValidation)
        {
            throw new InvalidOperationException("Only a pending upload can be accepted.");
        }

        State = UploadIntentState.Accepted;
        AcceptedObjectKey = acceptedObjectKey;
        AcceptedAt = acceptedAt;
        ValidationErrorCode = null;
    }

    public void BeginExpansion(int discoveredPageCount)
    {
        if (State != UploadIntentState.Accepted || AcceptedObjectKey is null)
        {
            throw new InvalidOperationException("Only an accepted upload can begin expansion.");
        }

        if (discoveredPageCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(discoveredPageCount));
        }

        DiscoveredPageCount = discoveredPageCount;
        CreatedPageCount = 0;
        FailedPageCount = 0;
        ExpansionErrorCode = null;
    }

    public void RecordExpansion(int createdPages, int failedPages, string? failureCode)
    {
        if (State != UploadIntentState.Accepted || AcceptedObjectKey is null)
        {
            throw new InvalidOperationException("Only an accepted upload can record expansion.");
        }

        if (createdPages < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(createdPages));
        }

        if (failedPages < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(failedPages));
        }

        if (createdPages + failedPages > DiscoveredPageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(createdPages));
        }

        if (failureCode is not null && string.IsNullOrWhiteSpace(failureCode))
        {
            throw new ArgumentException("The failure code must not be whitespace.", nameof(failureCode));
        }

        CreatedPageCount = createdPages;
        FailedPageCount = failedPages;
        ExpansionErrorCode = failureCode;
    }

    public void Reject(string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        if (State != UploadIntentState.PendingValidation)
        {
            throw new InvalidOperationException("Only a pending upload can be rejected.");
        }

        State = UploadIntentState.Rejected;
        ValidationErrorCode = errorCode;
    }
}
