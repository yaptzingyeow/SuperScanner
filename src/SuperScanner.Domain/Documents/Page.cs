namespace SuperScanner.Domain.Documents;

public sealed class Page
{
    private Page()
    {
    }

    public Guid Id { get; private set; }

    public Guid DocumentId { get; private set; }

    public int PageNumber { get; private set; }

    public string? OriginalObjectKey { get; private set; }
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

    public DateTimeOffset CreatedAt { get; private set; }

    internal static Page Create(Guid id, Guid documentId, int pageNumber, DateTimeOffset now) =>
        new()
        {
            Id = id,
            DocumentId = documentId,
            PageNumber = pageNumber,
            CreatedAt = now
        };

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
