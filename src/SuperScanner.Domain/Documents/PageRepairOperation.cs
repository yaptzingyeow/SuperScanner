namespace SuperScanner.Domain.Documents;

/// <summary>A private, immutable-source repair proposal awaiting explicit approval.</summary>
public sealed class PageRepairOperation
{
    private PageRepairOperation() { }

    public Guid Id { get; private set; }
    public Guid PageId { get; private set; }
    public Guid? SourceRevisionId { get; private set; }
    public string SourceObjectKey { get; private set; } = string.Empty;
    public string RectanglesJson { get; private set; } = string.Empty;
    public string StrokesJson { get; private set; } = "[]";
    public string Kind { get; private set; } = "Repair";
    public string? CandidatesJson { get; private set; }
    public string State { get; private set; } = "Queued";
    public string? PreviewObjectKey { get; private set; }
    public Guid? AppliedRevisionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static PageRepairOperation Create(Guid id, Guid pageId, Guid? sourceRevisionId,
        string sourceObjectKey, string rectanglesJson, DateTimeOffset now,
        string strokesJson = "[]")
    {
        if (id == Guid.Empty || pageId == Guid.Empty) throw new ArgumentException("IDs are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceObjectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(rectanglesJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(strokesJson);
        return new PageRepairOperation { Id = id, PageId = pageId, SourceRevisionId = sourceRevisionId,
            SourceObjectKey = sourceObjectKey, RectanglesJson = rectanglesJson,
            StrokesJson = strokesJson, CreatedAt = now };
    }

    public static PageRepairOperation CreateSuggestion(Guid id, Guid pageId,
        Guid? sourceRevisionId, string sourceObjectKey, DateTimeOffset now)
    {
        var operation = Create(id, pageId, sourceRevisionId, sourceObjectKey, "[]", now);
        operation.Kind = "Suggest";
        return operation;
    }

    public void CompleteSuggestions(string candidatesJson)
    {
        if (State != "Queued" || Kind != "Suggest")
            throw new InvalidOperationException("Suggestions are not queued.");
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatesJson);
        CandidatesJson = candidatesJson;
        State = "Ready";
    }

    public void CompletePreview(string key)
    {
        if (State != "Queued" || Kind != "Repair") throw new InvalidOperationException("Preview is not queued.");
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        PreviewObjectKey = key;
        State = "Ready";
    }

    public void Apply(Guid revisionId)
    {
        if (State != "Ready" || Kind != "Repair" || PreviewObjectKey is null || revisionId == Guid.Empty)
            throw new InvalidOperationException("Preview is not ready.");
        AppliedRevisionId = revisionId;
        State = "Applied";
    }

    public void Fail() { if (State == "Queued") State = "Failed"; }

    public void Expire()
    {
        if (Kind != "Repair" || State is not ("Ready" or "Failed"))
            throw new InvalidOperationException("Only unused repair previews can expire.");
        State = "Expired";
    }
}
