namespace ArksScanner.Domain.Documents;

public sealed record SignatureBox
{
    public double X { get; private init; }
    public double Y { get; private init; }
    public double Width { get; private init; }
    public double Height { get; private init; }

    public SignatureBox(double x, double y, double width, double height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) ||
            !double.IsFinite(height) || x < 0 || y < 0 || width <= 0 || height <= 0 ||
            x + width > 1 || y + height > 1)
            throw new ArgumentOutOfRangeException(nameof(x), "Signature must fit inside the page.");
        X = x; Y = y; Width = width; Height = height;
    }
}

public sealed class PageSignature
{
    private PageSignature() { }
    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid PageId { get; private set; }
    public Guid ClientRequestId { get; private set; }
    public string AssetKey { get; private set; } = string.Empty;
    public double ImageAspectRatio { get; private set; }
    public SignatureBox Box { get; private set; } = null!;
    public long Revision { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset? AssetPurgedAt { get; private set; }

    public void MarkAssetPurged(DateTimeOffset now)
    {
        if (!DeletedAt.HasValue) throw new InvalidOperationException("Active signature assets cannot be purged.");
        AssetPurgedAt ??= now;
    }

    public static PageSignature Create(Guid id, Guid documentId, Guid pageId, Guid clientRequestId,
        string assetKey, double imageAspectRatio, SignatureBox box, DateTimeOffset now)
    {
        if (id == Guid.Empty || documentId == Guid.Empty || pageId == Guid.Empty || clientRequestId == Guid.Empty)
            throw new ArgumentException("Non-empty signature identifiers are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(assetKey);
        ArgumentNullException.ThrowIfNull(box);
        if (!double.IsFinite(imageAspectRatio) || imageAspectRatio <= 0)
            throw new ArgumentOutOfRangeException(nameof(imageAspectRatio));
        return new() { Id = id, DocumentId = documentId, PageId = pageId, ClientRequestId = clientRequestId,
            AssetKey = assetKey, ImageAspectRatio = imageAspectRatio, Box = box, CreatedAt = now, UpdatedAt = now };
    }

    public void MoveResize(SignatureBox box, long expectedRevision, DateTimeOffset now)
    {
        EnsureEditable(expectedRevision);
        ArgumentNullException.ThrowIfNull(box);
        Box = box; Revision++; UpdatedAt = now;
    }

    public void Delete(long expectedRevision, DateTimeOffset now)
    {
        EnsureEditable(expectedRevision);
        DeletedAt = now; UpdatedAt = now; Revision++;
    }

    private void EnsureEditable(long expectedRevision)
    {
        if (DeletedAt.HasValue || expectedRevision != Revision)
            throw new InvalidOperationException("Signature was deleted or its revision changed.");
    }
}
