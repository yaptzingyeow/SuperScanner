using System.Text.Json;

namespace SuperScanner.Domain.Documents;

public sealed class DocumentExport
{
    private DocumentExport()
    {
    }

    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public string OwnerFirebaseUid { get; private set; } = string.Empty;
    public DocumentExportState State { get; private set; }
    public long DocumentRevision { get; private set; }
    public string SnapshotJson { get; private set; } = string.Empty;
    public int ReadyPageCount { get; private set; }
    public int ExcludedPageCount { get; private set; }
    public int SearchablePageCount { get; private set; }
    public string Searchability => SearchablePageCount == 0
        ? "ImageOnly"
        : SearchablePageCount == ReadyPageCount
            ? "Searchable"
            : "PartiallySearchable";
    public string? OutputObjectKey { get; private set; }
    public string? FailureCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public static DocumentExport Create(
        Guid id,
        Document document,
        string ownerUid,
        DateTimeOffset now,
        TimeSpan retention,
        IReadOnlyDictionary<Guid, DocumentExportOcrSnapshot>? ocrByPage = null,
        IReadOnlyDictionary<Guid, IReadOnlyList<SignatureOverlaySnapshot>>? signaturesByPage = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUid);
        if (id == Guid.Empty) throw new ArgumentException("An export ID is required.", nameof(id));
        if (ownerUid != document.OwnerFirebaseUid)
        {
            throw new ArgumentException("Export owner must match document owner.", nameof(ownerUid));
        }

        if (retention <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retention));

        var activePages = document.ActivePages;
        var readyPages = activePages.Where(page => page.State == PageState.Ready).ToArray();
        if (readyPages.Length == 0)
        {
            throw new InvalidOperationException("Document has no ready pages to export.");
        }

        var snapshot = readyPages.Select(page =>
        {
            var processedObjectKey = page.GetExportObjectKey();
            DocumentExportOcrSnapshot? ocr = null;
            var hasMatchingOcr = ocrByPage is not null &&
                ocrByPage.TryGetValue(page.Id, out ocr) &&
                string.Equals(ocr.SourceObjectKey, processedObjectKey, StringComparison.Ordinal);
            return new DocumentExportSnapshotEntry(
                page.Id,
                page.Position,
                page.AppliedCropRevision,
                page.AppliedFilter,
                processedObjectKey,
                hasMatchingOcr ? ocr!.ResultId : null,
                hasMatchingOcr ? ocr!.SourceObjectKey : null,
                hasMatchingOcr ? ocr!.SourceFingerprint : null,
                signaturesByPage is not null && signaturesByPage.TryGetValue(page.Id, out var signatures)
                    ? signatures.ToArray() : []);
        });

        return new DocumentExport
        {
            Id = id,
            DocumentId = document.Id,
            OwnerFirebaseUid = ownerUid,
            State = DocumentExportState.Queued,
            DocumentRevision = document.Revision,
            SnapshotJson = JsonSerializer.Serialize(snapshot),
            ReadyPageCount = readyPages.Length,
            ExcludedPageCount = activePages.Count - readyPages.Length,
            CreatedAt = now,
            ExpiresAt = now.Add(retention)
        };
    }

    public void Queue()
    {
        if (State != DocumentExportState.Failed)
        {
            throw new InvalidOperationException("Only failed exports can be queued again.");
        }

        State = DocumentExportState.Queued;
        FailureCode = null;
        CompletedAt = null;
        OutputObjectKey = null;
        SearchablePageCount = 0;
    }

    public void Start(DateTimeOffset now)
    {
        if (State != DocumentExportState.Queued)
        {
            throw new InvalidOperationException("Only queued exports can start processing.");
        }

        State = DocumentExportState.Processing;
    }

    public void Complete(string outputObjectKey, DateTimeOffset now, int searchablePageCount = 0)
    {
        if (State != DocumentExportState.Processing)
        {
            throw new InvalidOperationException("Only processing exports can complete.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(outputObjectKey);
        if (searchablePageCount < 0 || searchablePageCount > ReadyPageCount)
            throw new ArgumentOutOfRangeException(nameof(searchablePageCount));
        State = DocumentExportState.Ready;
        OutputObjectKey = outputObjectKey;
        SearchablePageCount = searchablePageCount;
        FailureCode = null;
        CompletedAt = now;
    }

    public void Fail(string failureCode, DateTimeOffset now)
    {
        if (State is not (DocumentExportState.Queued or DocumentExportState.Processing))
        {
            throw new InvalidOperationException("Only queued or processing exports can fail.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        State = DocumentExportState.Failed;
        FailureCode = failureCode;
        CompletedAt = now;
    }
}

public sealed record DocumentExportSnapshotEntry(
    Guid PageId,
    int Position,
    int AppliedCropRevision,
    string AppliedFilter,
    string ProcessedObjectKey,
    Guid? OcrResultId = null,
    string? OcrSourceObjectKey = null,
    string? OcrSourceFingerprint = null,
    IReadOnlyList<SignatureOverlaySnapshot>? SignatureOverlays = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<SignatureOverlaySnapshot> Signatures => SignatureOverlays ?? [];
}

public sealed record SignatureOverlaySnapshot(
    Guid SignatureId, string AssetKey, SignatureBox Box, double ImageAspectRatio);

public sealed record DocumentExportOcrSnapshot
{
    public DocumentExportOcrSnapshot(Guid resultId, string sourceObjectKey, string sourceFingerprint)
    {
        if (resultId == Guid.Empty) throw new ArgumentException("An OCR result ID is required.", nameof(resultId));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceObjectKey);
        if (sourceFingerprint.Length != 64 || sourceFingerprint.Any(character => !char.IsAsciiHexDigit(character)))
            throw new ArgumentException("A SHA-256 source fingerprint is required.", nameof(sourceFingerprint));
        ResultId = resultId;
        SourceObjectKey = sourceObjectKey;
        SourceFingerprint = sourceFingerprint.ToLowerInvariant();
    }

    public Guid ResultId { get; }
    public string SourceObjectKey { get; }
    public string SourceFingerprint { get; }
}
