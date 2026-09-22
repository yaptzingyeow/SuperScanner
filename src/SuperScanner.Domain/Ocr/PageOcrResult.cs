namespace SuperScanner.Domain.Ocr;

public sealed class PageOcrResult
{
    private readonly List<OcrElement> _elements = [];

    private PageOcrResult()
    {
    }

    public Guid Id { get; private set; }
    public Guid PageId { get; private set; }
    public string SourceObjectKey { get; private set; } = string.Empty;
    public string SourceFingerprint { get; private set; } = string.Empty;
    public OcrResultState State { get; private set; }
    public string Language { get; private set; } = "en";
    public string FullText { get; private set; } = string.Empty;
    public string? ProviderName { get; private set; }
    public string? ProviderModelVersion { get; private set; }
    public string? FailureCode { get; private set; }
    public bool FailureRetryable { get; private set; }
    public int AttemptCount { get; private set; }
    public int ElementCount { get; private set; }
    public double? AggregateConfidence { get; private set; }
    public DateTimeOffset QueuedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public IReadOnlyCollection<OcrElement> Elements => _elements;

    public static PageOcrResult Queue(
        Guid id,
        Guid pageId,
        string sourceObjectKey,
        string sourceFingerprint,
        string language,
        DateTimeOffset now)
    {
        if (id == Guid.Empty) throw new ArgumentException("An OCR result ID is required.", nameof(id));
        if (pageId == Guid.Empty) throw new ArgumentException("A page ID is required.", nameof(pageId));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceObjectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFingerprint);
        if (sourceFingerprint.Length != 64 ||
            sourceFingerprint.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException("A SHA-256 source fingerprint is required.", nameof(sourceFingerprint));
        }

        if (!string.Equals(language, "en", StringComparison.Ordinal))
            throw new ArgumentException("Phase 3A supports English only.", nameof(language));

        return new PageOcrResult
        {
            Id = id,
            PageId = pageId,
            SourceObjectKey = sourceObjectKey,
            SourceFingerprint = sourceFingerprint.ToLowerInvariant(),
            Language = language,
            State = OcrResultState.Queued,
            QueuedAt = now
        };
    }

    public void BeginAttempt(int attemptNumber, DateTimeOffset now)
    {
        if (State is not (OcrResultState.Queued or OcrResultState.Processing))
            throw new InvalidOperationException("OCR is not pending.");
        if (attemptNumber < 1 || attemptNumber < AttemptCount)
            throw new ArgumentOutOfRangeException(nameof(attemptNumber));

        State = OcrResultState.Processing;
        StartedAt ??= now;
        AttemptCount = attemptNumber;
    }

    public void Complete(
        string provider,
        string modelVersion,
        string fullText,
        IReadOnlyList<OcrElement> elements,
        DateTimeOffset now)
    {
        if (State != OcrResultState.Processing)
            throw new InvalidOperationException("OCR is not processing.");
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelVersion);
        ArgumentNullException.ThrowIfNull(fullText);
        ArgumentNullException.ThrowIfNull(elements);
        if (elements.Any(element => element.PageOcrResultId != Id))
            throw new ArgumentException("Every element must belong to this result.", nameof(elements));

        var ids = elements.Select(element => element.Id).ToHashSet();
        if (ids.Count != elements.Count)
            throw new ArgumentException("OCR element IDs must be unique.", nameof(elements));
        if (elements.Any(element => element.ParentElementId is Guid parentId && !ids.Contains(parentId)))
            throw new ArgumentException("Every parent must belong to this result.", nameof(elements));
        if (elements.GroupBy(element => new { element.ParentElementId, element.ReadingOrder })
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Reading order must be unique within a parent.", nameof(elements));
        }

        EnsureAcyclic(elements);

        _elements.Clear();
        _elements.AddRange(elements);
        ProviderName = provider;
        ProviderModelVersion = modelVersion;
        FullText = fullText;
        ElementCount = elements.Count;
        AggregateConfidence = elements.Count == 0 ? null : elements.Average(element => element.Confidence);
        State = OcrResultState.Ready;
        CompletedAt = now;
        FailureCode = null;
        FailureRetryable = false;
    }

    public void Fail(string safeCode, bool retryable, DateTimeOffset now)
    {
        if (State is not (OcrResultState.Queued or OcrResultState.Processing))
            throw new InvalidOperationException("OCR is not pending.");
        ArgumentException.ThrowIfNullOrWhiteSpace(safeCode);
        if (safeCode.Length > 64) throw new ArgumentException("OCR failure code is too long.", nameof(safeCode));

        State = OcrResultState.Failed;
        FailureCode = safeCode;
        FailureRetryable = retryable;
        CompletedAt = now;
    }

    public void Retry(DateTimeOffset now)
    {
        if (State != OcrResultState.Failed || !FailureRetryable)
            throw new InvalidOperationException("OCR failure cannot be retried.");

        State = OcrResultState.Queued;
        QueuedAt = now;
        StartedAt = null;
        CompletedAt = null;
        AttemptCount = 0;
        FailureCode = null;
        FailureRetryable = false;
    }

    private static void EnsureAcyclic(IReadOnlyList<OcrElement> elements)
    {
        var byId = elements.ToDictionary(element => element.Id);
        foreach (var element in elements)
        {
            var visited = new HashSet<Guid> { element.Id };
            var parent = element.ParentElementId;
            while (parent is Guid parentId)
            {
                if (!visited.Add(parentId))
                    throw new ArgumentException("OCR hierarchy cannot contain a cycle.", nameof(elements));
                parent = byId[parentId].ParentElementId;
            }
        }
    }
}
