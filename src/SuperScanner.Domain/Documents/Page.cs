namespace SuperScanner.Domain.Documents;

public sealed class Page
{
    private Page()
    {
    }

    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public int PageNumber { get; private set; }
    public int Position { get; private set; }
    public Guid SourceUploadId { get; private set; }
    public int SourcePageIndex { get; private set; }
    public PageState State { get; private set; } = PageState.Importing;
    public string? FailureCode { get; private set; }
    public string? OriginalObjectKey { get; private set; }
    public string OriginalMediaType { get; private set; } = string.Empty;
    public string? PreviewObjectKey { get; private set; }
    public string? ThumbnailObjectKey { get; private set; }
    public string? CropSourceObjectKey { get; private set; }
    public string? CropPointsJson { get; private set; }
    public string? CropStatus { get; private set; }
    public string? CropSource { get; private set; }
    public double? CropConfidence { get; private set; }
    public string? CropModelVersion { get; private set; }
    public string? CropDiagnosticsCode { get; private set; }
    public int CropRevision { get; private set; }
    public int AppliedCropRevision { get; private set; }
    public string Filter { get; private set; } = ScanFilter.Default;
    public string AppliedFilter { get; private set; } = ScanFilter.Default;
    public DateTimeOffset? RemovedAt { get; private set; }
    public string? RemovedByFirebaseUid { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void SetFilter(string filter)
    {
        if (!ScanFilter.IsValid(filter)) throw new ArgumentException("Unknown scan filter.", nameof(filter));
        Filter = filter;
    }

    public void InitializeCrop()
    {
        if (CropSourceObjectKey != null) return;
        if (PreviewObjectKey is null) throw new InvalidOperationException("Preview is not available.");
        CropSourceObjectKey = PreviewObjectKey;
        BeginCrop(true, null);
    }

    public void BeginCrop(bool detect, string? pointsJson)
    {
        if (CropSourceObjectKey is null) throw new InvalidOperationException("Crop source is not available.");
        CropRevision++;
        CropStatus = detect ? "Detecting" : "Processing";
        CropSource = detect ? "Automatic" : "Manual";
        CropConfidence = null;
        CropModelVersion = null;
        CropDiagnosticsCode = null;
        if (!detect) CropPointsJson = pointsJson;
    }

    public void SetPreview(string previewKey, string thumbnailKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previewKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailKey);
        PreviewObjectKey = previewKey;
        ThumbnailObjectKey = thumbnailKey;
    }

    public void MarkImportReady(string originalObjectKey, string originalMediaType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalObjectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalMediaType);
        if (OriginalObjectKey is not null)
        {
            throw new InvalidOperationException("Original asset is immutable.");
        }

        OriginalObjectKey = originalObjectKey;
        OriginalMediaType = originalMediaType;
        State = PageState.Processing;
        FailureCode = null;
    }

    public void MarkProcessing()
    {
        State = PageState.Processing;
        FailureCode = null;
    }

    public void MarkReady()
    {
        if (PreviewObjectKey is null)
        {
            throw new InvalidOperationException("Preview is not available.");
        }

        State = PageState.Ready;
        FailureCode = null;
    }

    public void MarkFailed(string failureCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        State = PageState.Failed;
        FailureCode = failureCode;
    }

    public void SoftRemove(string firebaseUid, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firebaseUid);
        if (RemovedAt is not null) return;

        RemovedAt = now;
        RemovedByFirebaseUid = firebaseUid;
    }

    public string GetExportObjectKey()
    {
        if (State != PageState.Ready || PreviewObjectKey is null)
        {
            throw new InvalidOperationException("Page is not ready for export.");
        }

        return PreviewObjectKey;
    }

    internal static Page Create(
        Guid id,
        Guid documentId,
        Guid sourceUploadId,
        int sourcePageIndex,
        int position,
        DateTimeOffset now)
    {
        if (sourcePageIndex < 1) throw new ArgumentOutOfRangeException(nameof(sourcePageIndex));
        if (position < 1) throw new ArgumentOutOfRangeException(nameof(position));

        return new Page
        {
            Id = id,
            DocumentId = documentId,
            PageNumber = position,
            Position = position,
            SourceUploadId = sourceUploadId,
            SourcePageIndex = sourcePageIndex,
            CreatedAt = now
        };
    }

    internal void SetPosition(int position)
    {
        if (position < 1) throw new ArgumentOutOfRangeException(nameof(position));
        Position = position;
        PageNumber = position;
    }

    public void AcceptOriginal(string objectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        if (OriginalObjectKey is not null)
        {
            throw new InvalidOperationException("Original asset is immutable.");
        }

        OriginalObjectKey = objectKey;
    }
}
