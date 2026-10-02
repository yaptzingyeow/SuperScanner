using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArksScanner.Domain.TextEditing;

public enum TextEditState
{
    Queued,
    Processing,
    Succeeded,
    Failed
}

public sealed class TextEditOperation
{
    public const int MaximumReplacementLength = 4_000;
    private static readonly Regex HashPattern =
        new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex SafeCodePattern =
        new("^[a-z0-9]+(?:_[a-z0-9]+)*$", RegexOptions.CultureInvariant);

    private TextEditOperation()
    {
    }

    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid PageId { get; private set; }
    public string ActorFirebaseUid { get; private set; } = string.Empty;
    public Guid SourceRevisionId { get; private set; }
    public Guid SourceOcrResultId { get; private set; }
    public string SelectedOcrElementIdsJson { get; private set; } = "[]";
    public string OriginalText { get; private set; } = string.Empty;
    public string ReplacementText { get; private set; } = string.Empty;
    public string ReplacementBoxJson { get; private set; } = "{}";
    public string StyleJson { get; private set; } = "{}";
    public string StyleProvenanceJson { get; private set; } = "{}";
    public long Sequence { get; private set; }
    public Guid? BranchParentEditId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string CanonicalRequestHash { get; private set; } = string.Empty;
    public string RendererVersion { get; private set; } = string.Empty;
    public string LayoutVersion { get; private set; } = string.Empty;
    public TextEditState State { get; private set; }
    public string? FailureCode { get; private set; }
    public Guid? ResultRevisionId { get; private set; }
    public DateTimeOffset QueuedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyList<Guid> SelectedOcrElementIds =>
        JsonSerializer.Deserialize<Guid[]>(SelectedOcrElementIdsJson) ?? [];
    public NormalizedBox ReplacementBox =>
        JsonSerializer.Deserialize<NormalizedBox>(ReplacementBoxJson)
        ?? throw new InvalidOperationException("Stored replacement box is invalid.");
    public TextEditStyle Style =>
        JsonSerializer.Deserialize<TextEditStyle>(StyleJson)
        ?? throw new InvalidOperationException("Stored text style is invalid.");

    public static TextEditOperation Queue(
        Guid id,
        Guid documentId,
        Guid pageId,
        string actorFirebaseUid,
        Guid sourceRevisionId,
        Guid sourceOcrResultId,
        IReadOnlyList<Guid> selectedOcrElementIds,
        string originalText,
        string replacementText,
        NormalizedBox replacementBox,
        TextEditStyle style,
        long sequence,
        Guid? branchParentEditId,
        string idempotencyKey,
        string canonicalRequestHash,
        string rendererVersion,
        string layoutVersion,
        DateTimeOffset queuedAt)
    {
        RequireId(id, nameof(id));
        RequireId(documentId, nameof(documentId));
        RequireId(pageId, nameof(pageId));
        RequireId(sourceRevisionId, nameof(sourceRevisionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(actorFirebaseUid);
        ArgumentNullException.ThrowIfNull(selectedOcrElementIds);
        if (sourceOcrResultId == Guid.Empty && selectedOcrElementIds.Count != 0)
            throw new ArgumentException("Selected words require an OCR result.", nameof(sourceOcrResultId));
        if ((selectedOcrElementIds.Count == 0 && sourceOcrResultId != Guid.Empty) ||
            selectedOcrElementIds.Any(elementId => elementId == Guid.Empty) ||
            selectedOcrElementIds.Distinct().Count() != selectedOcrElementIds.Count)
            throw new ArgumentException("A unique OCR word selection is required.", nameof(selectedOcrElementIds));
        if (selectedOcrElementIds.Count != 0) ArgumentException.ThrowIfNullOrWhiteSpace(originalText);
        if ((selectedOcrElementIds.Count == 0 && string.IsNullOrWhiteSpace(replacementText)) ||
            replacementText.Length > MaximumReplacementLength)
            throw new ArgumentException("Replacement text is outside the supported length.", nameof(replacementText));
        ArgumentNullException.ThrowIfNull(replacementBox);
        ArgumentNullException.ThrowIfNull(style);
        if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
        if (branchParentEditId == Guid.Empty)
            throw new ArgumentException("A branch parent edit ID cannot be empty.", nameof(branchParentEditId));
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        if (!HashPattern.IsMatch(canonicalRequestHash))
            throw new ArgumentException("A lowercase SHA-256 request hash is required.", nameof(canonicalRequestHash));
        TextEditStyle.ValidateIdentifier(rendererVersion, nameof(rendererVersion));
        TextEditStyle.ValidateIdentifier(layoutVersion, nameof(layoutVersion));

        return new TextEditOperation
        {
            Id = id,
            DocumentId = documentId,
            PageId = pageId,
            ActorFirebaseUid = actorFirebaseUid,
            SourceRevisionId = sourceRevisionId,
            SourceOcrResultId = sourceOcrResultId,
            SelectedOcrElementIdsJson = JsonSerializer.Serialize(selectedOcrElementIds),
            OriginalText = originalText,
            ReplacementText = replacementText,
            ReplacementBoxJson = JsonSerializer.Serialize(replacementBox),
            StyleJson = JsonSerializer.Serialize(style),
            Sequence = sequence,
            BranchParentEditId = branchParentEditId,
            IdempotencyKey = idempotencyKey,
            CanonicalRequestHash = canonicalRequestHash,
            RendererVersion = rendererVersion,
            LayoutVersion = layoutVersion,
            State = TextEditState.Queued,
            QueuedAt = queuedAt
        };
    }

    public void Start(DateTimeOffset startedAt)
    {
        if (State != TextEditState.Queued)
            throw new InvalidOperationException("Only a queued text edit can start.");
        State = TextEditState.Processing;
        StartedAt = startedAt;
        FailureCode = null;
    }

    public void Complete(Guid resultRevisionId, DateTimeOffset completedAt)
    {
        if (State != TextEditState.Processing)
            throw new InvalidOperationException("Only a processing text edit can complete.");
        RequireId(resultRevisionId, nameof(resultRevisionId));
        State = TextEditState.Succeeded;
        ResultRevisionId = resultRevisionId;
        CompletedAt = completedAt;
        FailureCode = null;
    }

    public void Fail(string failureCode, DateTimeOffset completedAt)
    {
        if (State is not (TextEditState.Queued or TextEditState.Processing))
            throw new InvalidOperationException("Only a pending text edit can fail.");
        if (string.IsNullOrWhiteSpace(failureCode) || failureCode.Length > 64 ||
            !SafeCodePattern.IsMatch(failureCode))
            throw new ArgumentException("A safe failure code is required.", nameof(failureCode));
        State = TextEditState.Failed;
        FailureCode = failureCode;
        CompletedAt = completedAt;
    }

    private static void RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty) throw new ArgumentException("A non-empty ID is required.", parameterName);
    }
}
