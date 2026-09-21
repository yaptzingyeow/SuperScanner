# OCR Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a provider-independent, asynchronous OCR foundation that stores normalized English printed/handwritten recognition data and exposes safe per-page status without requiring Google credentials.

**Architecture:** A new OCR aggregate owns immutable source-revision status and normalized elements. Application services request and read OCR through a repository and provider contract; the existing PostgreSQL job queue dispatches an infrastructure processor that streams private page images to the selected provider. Phase 3A uses a deterministic fake provider, while Angular displays OCR status and retry controls without rendering an editable overlay.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core 10, PostgreSQL 17/JSONB, existing PostgreSQL processing queue, Angular 22 signals, Vitest, xUnit, Testcontainers, Playwright.

**Spec:** `docs/superpowers/specs/2026-09-22-ocr-foundation-design.md`

## Global Constraints

- OCR language is exactly `en` in Phase 3A.
- Normalized polygon coordinates are finite values in the inclusive range 0–1, measured from the processed image's top-left.
- Element kinds are exactly Block, Line, and Word; text types are exactly Printed, Handwritten, and Unknown.
- OCR state is independent from `Page.State` and `Document.Status`; OCR failure must not affect scanning or image-PDF export.
- The source fingerprint is SHA-256 of the immutable processed-image object key and is unique with `PageId`.
- All OCR endpoints require the existing Firebase authentication and App Check pipeline and hide missing, removed, and unowned resources behind the same 404 response.
- Production must reject the fake provider and must never silently fall back to it.
- Logs, metrics, error codes, and job payloads must not contain OCR text, filenames, pixels, signed URLs, or provider credentials.
- Google Document AI integration, text overlays, editing, searchable PDF, and handwriting generation are outside Phase 3A.

## Review Focus

- Duplicate OCR requests for the same page artifact must return one result and create one active job; Task 4 pins this with a concurrent idempotency test.
- A crop/filter revision changing while OCR is running must prevent the old result from appearing as current; Tasks 6 and 7 pin the stale-completion path.
- Empty provider text must complete Ready with zero elements rather than fail or retry; Task 6 pins this case.
- Oversized or malformed provider output must fail safely without persisting partial elements or raw provider content; Tasks 3 and 6 pin validation and transaction rollback.
- Component destruction during polling must cancel the timer and prevent a late response from mutating UI state; Task 8 pins teardown behavior.

---

### Task 1: OCR domain aggregate and normalized geometry

**Files:**
- Create: `src/SuperScanner.Domain/Ocr/OcrResultState.cs`
- Create: `src/SuperScanner.Domain/Ocr/OcrElementKind.cs`
- Create: `src/SuperScanner.Domain/Ocr/OcrTextType.cs`
- Create: `src/SuperScanner.Domain/Ocr/OcrPoint.cs`
- Create: `src/SuperScanner.Domain/Ocr/OcrElement.cs`
- Create: `src/SuperScanner.Domain/Ocr/PageOcrResult.cs`
- Test: `tests/SuperScanner.Domain.Tests/Ocr/PageOcrResultTests.cs`

**Interfaces:**
- Consumes: `Guid`, `DateTimeOffset`, and immutable processed-image object keys from the existing Page aggregate.
- Produces: `PageOcrResult.Queue(...)`, `BeginAttempt(...)`, `Complete(...)`, `Fail(...)`, `Retry(...)`; `OcrElement.Create(...)`; enums shared by persistence, application DTOs, and the worker.

- [ ] **Step 1: Write failing lifecycle and geometry tests**

```csharp
[Fact]
public void Complete_PreservesOrderedHierarchyAndAllowsEmptyText()
{
    var result = PageOcrResult.Queue(Guid.NewGuid(), Guid.NewGuid(), "previews/d/p/crop-2.jpg",
        new string('a', 64), "en", Now);
    result.BeginAttempt(1, Now.AddSeconds(1));
    result.Complete("Fake", "fixture-v1", "", [], Now.AddSeconds(2));
    Assert.Equal(OcrResultState.Ready, result.State);
    Assert.Empty(result.Elements);
    Assert.Equal(string.Empty, result.FullText);
}

[Theory]
[InlineData(-0.01, 0.5)]
[InlineData(1.01, 0.5)]
[InlineData(double.NaN, 0.5)]
public void Element_RejectsCoordinatesOutsideNormalizedImage(double x, double y) =>
    Assert.Throws<ArgumentOutOfRangeException>(() => OcrElement.Create(
        Guid.NewGuid(), Guid.NewGuid(), null, OcrElementKind.Word, "Name", .95,
        OcrTextType.Printed, 0,
        [new(x, y), new(.9, .1), new(.9, .2), new(.1, .2)]));
```

Add tests for Queued → Processing → Ready, lease-reclaimed Processing → Processing attempt increments, Queued/Processing → Failed, Failed → Queued retry, illegal transitions, 64-character lowercase hexadecimal fingerprints, exactly four polygon points, confidence 0–1, non-negative reading order, cyclic parent rejection, and parent/result ownership validation during completion.

- [ ] **Step 2: Run the focused domain tests and verify they fail**

Run: `dotnet test tests/SuperScanner.Domain.Tests/SuperScanner.Domain.Tests.csproj --filter FullyQualifiedName~PageOcrResultTests`

Expected: FAIL because `SuperScanner.Domain.Ocr` types do not exist.

- [ ] **Step 3: Implement the minimal aggregate**

```csharp
public enum OcrResultState { Queued, Processing, Ready, Failed }
public enum OcrElementKind { Block, Line, Word }
public enum OcrTextType { Printed, Handwritten, Unknown }
public readonly record struct OcrPoint(double X, double Y);

public sealed class PageOcrResult
{
    private readonly List<OcrElement> _elements = [];
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

    public static PageOcrResult Queue(Guid id, Guid pageId, string sourceObjectKey,
        string sourceFingerprint, string language, DateTimeOffset now)
    {
        if (id == Guid.Empty || pageId == Guid.Empty) throw new ArgumentException("IDs are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceObjectKey);
        if (sourceFingerprint.Length != 64 || sourceFingerprint.Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("A SHA-256 source fingerprint is required.", nameof(sourceFingerprint));
        if (language != "en") throw new ArgumentException("Phase 3A supports English only.", nameof(language));
        return new PageOcrResult { Id = id, PageId = pageId, SourceObjectKey = sourceObjectKey,
            SourceFingerprint = sourceFingerprint.ToLowerInvariant(), Language = language,
            State = OcrResultState.Queued, QueuedAt = now };
    }

    public void BeginAttempt(int attemptNumber, DateTimeOffset now)
    {
        if (State is not (OcrResultState.Queued or OcrResultState.Processing))
            throw new InvalidOperationException("OCR is not pending.");
        if (attemptNumber < 1 || attemptNumber < AttemptCount)
            throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        State = OcrResultState.Processing; StartedAt ??= now; AttemptCount = attemptNumber;
    }

    public void Complete(string provider, string modelVersion, string fullText,
        IReadOnlyList<OcrElement> elements, DateTimeOffset now)
    {
        if (State != OcrResultState.Processing) throw new InvalidOperationException("OCR is not processing.");
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelVersion);
        ArgumentNullException.ThrowIfNull(fullText);
        ArgumentNullException.ThrowIfNull(elements);
        if (elements.Any(e => e.PageOcrResultId != Id))
            throw new ArgumentException("Every element must belong to this result.", nameof(elements));
        var ids = elements.Select(e => e.Id).ToHashSet();
        if (elements.Any(e => e.ParentElementId is Guid parent && !ids.Contains(parent)))
            throw new ArgumentException("Every parent must belong to this result.", nameof(elements));
        var byId = elements.ToDictionary(e => e.Id);
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
        _elements.Clear(); _elements.AddRange(elements);
        ProviderName = provider; ProviderModelVersion = modelVersion; FullText = fullText;
        ElementCount = elements.Count;
        AggregateConfidence = elements.Count == 0 ? null : elements.Average(e => e.Confidence);
        State = OcrResultState.Ready; CompletedAt = now; FailureCode = null; FailureRetryable = false;
    }

    public void Fail(string safeCode, bool retryable, DateTimeOffset now)
    {
        if (State is not (OcrResultState.Queued or OcrResultState.Processing))
            throw new InvalidOperationException("OCR is not pending.");
        ArgumentException.ThrowIfNullOrWhiteSpace(safeCode);
        State = OcrResultState.Failed; FailureCode = safeCode; FailureRetryable = retryable; CompletedAt = now;
    }

    public void Retry(DateTimeOffset now)
    {
        if (State != OcrResultState.Failed || !FailureRetryable)
            throw new InvalidOperationException("OCR failure cannot be retried.");
        State = OcrResultState.Queued; QueuedAt = now; StartedAt = null; CompletedAt = null;
        AttemptCount = 0; FailureCode = null; FailureRetryable = false;
    }
}
```

`OcrElement.Create` validates identifiers, kind, bounded text, confidence, reading order, and exactly four finite normalized points. Store polygon points as a private JSON string plus a deserialized `Polygon` getter so EF can map the string to JSONB in Task 2 without adding persistence dependencies to Domain.

- [ ] **Step 4: Run the domain project tests**

Run: `dotnet test tests/SuperScanner.Domain.Tests/SuperScanner.Domain.Tests.csproj`

Expected: PASS, including all new OCR transition and validation cases.

- [ ] **Step 5: Commit Task 1**

```powershell
git add src/SuperScanner.Domain/Ocr tests/SuperScanner.Domain.Tests/Ocr
git commit -m "feat: add OCR domain aggregate"
```

---

### Task 2: PostgreSQL OCR persistence and repository

**Files:**
- Create: `src/SuperScanner.Application/Abstractions/IOcrRepository.cs`
- Create: `src/SuperScanner.Infrastructure/Persistence/Configurations/PageOcrResultConfiguration.cs`
- Create: `src/SuperScanner.Infrastructure/Persistence/Configurations/OcrElementConfiguration.cs`
- Create: `src/SuperScanner.Infrastructure/Persistence/EfOcrRepository.cs`
- Modify: `src/SuperScanner.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/SuperScanner.Infrastructure/Persistence/Migrations/20260922013000_OcrFoundation.cs`
- Create: `src/SuperScanner.Infrastructure/Persistence/Migrations/20260922013000_OcrFoundation.Designer.cs`
- Modify: `src/SuperScanner.Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs`
- Test: `tests/SuperScanner.Infrastructure.IntegrationTests/Persistence/OcrPersistenceTests.cs`

**Interfaces:**
- Consumes: `PageOcrResult` and `OcrElement` from Task 1; existing Page → Document ownership relation.
- Produces: `IOcrRepository`, `OcrPageSource`, and EF implementation used by request/query services in Task 4.

- [ ] **Step 1: Write failing PostgreSQL persistence tests**

```csharp
[Fact]
public async Task RoundTrip_PersistsJsonbHierarchyAndEnforcesOneResultPerSource()
{
    var result = PageOcrResult.Queue(Guid.NewGuid(), page.Id, "previews/d/p/crop-1.jpg",
        new string('a', 64), "en", Now);
    db.PageOcrResults.Add(result);
    await db.SaveChangesAsync();
    db.PageOcrResults.Add(PageOcrResult.Queue(Guid.NewGuid(), page.Id,
        result.SourceObjectKey, result.SourceFingerprint, "en", Now));
    await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
}
```

Also assert cascade deletion of elements, JSONB column type, `(PageId, SourceFingerprint)` uniqueness, current-source lookup ignoring removed pages, and owner lookup returning null for a different Firebase UID.

- [ ] **Step 2: Run the persistence tests and verify they fail**

Run: `dotnet test tests/SuperScanner.Infrastructure.IntegrationTests/SuperScanner.Infrastructure.IntegrationTests.csproj --filter FullyQualifiedName~OcrPersistenceTests`

Expected: FAIL because OCR DbSets, mappings, and repository do not exist.

- [ ] **Step 3: Define repository contracts and EF mappings**

```csharp
public sealed record OcrPageSource(Guid PageId, string SourceObjectKey, string MediaType);

public interface IOcrRepository
{
    Task<IOcrTransaction> BeginTransactionAsync(CancellationToken ct);
    Task<OcrPageSource?> FindOwnedReadySourceAsync(string ownerUid, Guid documentId, Guid pageId, CancellationToken ct);
    Task<PageOcrResult?> FindBySourceAsync(Guid pageId, string sourceFingerprint, bool forUpdate, CancellationToken ct);
    Task<PageOcrResult?> FindCurrentOwnedAsync(string ownerUid, Guid documentId, Guid pageId, CancellationToken ct);
    Task AddAsync(PageOcrResult result, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public interface IOcrTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}
```

Map `page_ocr_results` and `ocr_elements`; map enum values as strings, `PolygonJson` as `jsonb`, bounded string lengths from the spec, cascade result → elements, and indexes `(PageId, SourceFingerprint)` unique plus `(PageId, State, QueuedAt)`.

- [ ] **Step 4: Generate the EF migration**

Run: `dotnet ef migrations add OcrFoundation --project src/SuperScanner.Infrastructure/SuperScanner.Infrastructure.csproj --startup-project src/SuperScanner.Api/SuperScanner.Api.csproj --context AppDbContext --output-dir Persistence/Migrations`

Expected: a migration adding only `page_ocr_results`, `ocr_elements`, their foreign keys, constraints, and indexes.

- [ ] **Step 5: Run persistence tests and migration verification**

Run: `dotnet test tests/SuperScanner.Infrastructure.IntegrationTests/SuperScanner.Infrastructure.IntegrationTests.csproj --filter "FullyQualifiedName~OcrPersistenceTests|FullyQualifiedName~DocumentPersistenceTests"`

Expected: PASS against PostgreSQL Testcontainers.

- [ ] **Step 6: Commit Task 2**

```powershell
git add src/SuperScanner.Application/Abstractions/IOcrRepository.cs src/SuperScanner.Infrastructure/Persistence tests/SuperScanner.Infrastructure.IntegrationTests/Persistence/OcrPersistenceTests.cs
git commit -m "feat: persist normalized OCR results"
```

---

### Task 3: Provider contract, result validation, options, and deterministic fake

**Files:**
- Create: `src/SuperScanner.Application/Ocr/OcrProviderContracts.cs`
- Create: `src/SuperScanner.Application/Ocr/OcrResultValidator.cs`
- Create: `src/SuperScanner.Infrastructure/Ocr/OcrOptions.cs`
- Create: `src/SuperScanner.Infrastructure/Ocr/FakeOcrProvider.cs`
- Test: `tests/SuperScanner.Application.Tests/Ocr/OcrResultValidatorTests.cs`
- Test: `tests/SuperScanner.Application.Tests/Ocr/FakeOcrProviderTests.cs`

**Interfaces:**
- Consumes: normalized enums and points from Task 1.
- Produces: `IOcrProvider.RecognizeAsync(OcrInput, CancellationToken)`, normalized provider records, `OcrProviderException`, `OcrOptions`, and deterministic fake output used by Task 6.

- [ ] **Step 1: Write failing provider-contract tests**

```csharp
[Fact]
public async Task FakeProvider_ReturnsSameHierarchyForAnySupportedImage()
{
    var provider = new FakeOcrProvider();
    await using var first = new MemoryStream([0xff, 0xd8, 0xff, 0xd9]);
    await using var second = new MemoryStream([0x89, 0x50, 0x4e, 0x47]);
    var firstResult = await provider.RecognizeAsync(new(first, "image/jpeg", "en"), default);
    var secondResult = await provider.RecognizeAsync(new(second, "image/png", "en"), default);
    Assert.Equal(firstResult.FullText, secondResult.FullText);
    Assert.Equal(firstResult.Elements, secondResult.Elements);
}

[Fact]
public void Validate_RejectsMoreThanConfiguredElements()
{
    var document = FixtureDocument.WithWordCount(3);
    var error = Assert.Throws<OcrProviderException>(() =>
        OcrResultValidator.Validate(document, new OcrLimits(2, 1_000)));
    Assert.Equal("ocr_invalid_response", error.SafeCode);
    Assert.False(error.Retryable);
}
```

Cover invalid hierarchy parents, duplicate reading order within a parent, non-finite coordinates, text exceeding `MaxRecognizedCharacters`, unsupported language/media type, and valid empty output.

- [ ] **Step 2: Run focused tests and verify they fail**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter "FullyQualifiedName~OcrResultValidatorTests|FullyQualifiedName~FakeOcrProviderTests"`

Expected: FAIL because provider contracts and fake provider do not exist.

- [ ] **Step 3: Implement the provider contract and validator**

```csharp
public sealed record OcrInput(Stream Content, string MediaType, string Language);
public sealed record OcrLimits(int MaxElements, int MaxRecognizedCharacters);
public sealed record NormalizedOcrDocument(string FullText, string ProviderName,
    string ModelVersion, IReadOnlyList<NormalizedOcrElement> Elements);
public sealed record NormalizedOcrElement(string ClientId, string? ParentClientId,
    OcrElementKind Kind, string Text, double Confidence, OcrTextType TextType,
    int ReadingOrder, IReadOnlyList<OcrPoint> Polygon);
public interface IOcrProvider
{
    Task<NormalizedOcrDocument> RecognizeAsync(OcrInput input, CancellationToken ct);
}
public sealed class OcrProviderException(string safeCode, bool retryable) : Exception
{
    public string SafeCode { get; } = safeCode;
    public bool Retryable { get; } = retryable;
}
```

`OcrResultValidator.Validate` returns the validated document and never includes provider text in exceptions. `FakeOcrProvider` verifies the stream is readable and returns one fixed Block → Line → Word hierarchy for any supported image. Give it a constructor-only `FakeOcrScenario` (`Normal`, `Empty`, `Timeout`, `Invalid`) so unit tests can exercise valid empty output and safe deterministic failures without deriving behavior from document bytes, filenames, or object keys. Production registration always uses `Normal`; tests instantiate the other scenarios directly.

- [ ] **Step 4: Implement strict configuration validation**

```csharp
public sealed class OcrOptions
{
    public const string SectionName = "Ocr";
    public bool Enabled { get; init; }
    public string Provider { get; init; } = "Disabled";
    public string Language { get; init; } = "en";
    public int MaxAttempts { get; init; } = 3;
    public int TimeoutSeconds { get; init; } = 30;
    public int MaxElements { get; init; } = 10_000;
    public int MaxRecognizedCharacters { get; init; } = 1_000_000;
}
```

Add `IsValid(environmentName)` so Phase 3A accepts only `Disabled` and `Fake`, `Fake` is rejected in Production, enabled configuration requires Fake, disabled configuration requires Disabled, language must equal `en`, and all numeric limits must be positive. `GoogleDocumentAi` remains invalid until Phase 3B registers its adapter.

- [ ] **Step 5: Run the provider tests**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter "FullyQualifiedName~OcrResultValidatorTests|FullyQualifiedName~FakeOcrProviderTests"`

Expected: PASS with no network calls or external credentials.

- [ ] **Step 6: Commit Task 3**

```powershell
git add src/SuperScanner.Application/Ocr src/SuperScanner.Infrastructure/Ocr tests/SuperScanner.Application.Tests/Ocr
git commit -m "feat: define provider-neutral OCR contract"
```

---

### Task 4: Idempotent OCR request and current-result query services

**Files:**
- Create: `src/SuperScanner.Application/Ocr/OcrDtos.cs`
- Create: `src/SuperScanner.Application/Ocr/OcrExceptions.cs`
- Create: `src/SuperScanner.Application/Ocr/OcrSourceFingerprint.cs`
- Create: `src/SuperScanner.Application/Ocr/RequestPageOcr.cs`
- Create: `src/SuperScanner.Application/Ocr/GetPageOcr.cs`
- Modify: `src/SuperScanner.Application/Abstractions/IProcessingJobQueue.cs`
- Modify: `src/SuperScanner.Infrastructure/Processing/PostgresJobQueue.cs`
- Modify: queue fakes implementing `IProcessingJobQueue` under `tests/`
- Test: `tests/SuperScanner.Application.Tests/Ocr/RequestPageOcrTests.cs`
- Test: `tests/SuperScanner.Application.Tests/Ocr/GetPageOcrTests.cs`
- Test: `tests/SuperScanner.Infrastructure.IntegrationTests/Persistence/OcrRequestConcurrencyTests.cs`

**Interfaces:**
- Consumes: `IOcrRepository`, `IProcessingJobQueue`, `IClock`, OCR aggregate, and normalized element types.
- Produces: `RequestPageOcr.HandleAsync(ownerUid, documentId, pageId, retryFailed, ct)` and `GetPageOcr.HandleAsync(ownerUid, documentId, pageId, ct)` returning `PageOcrDto`.

- [ ] **Step 1: Write failing command/query tests**

```csharp
[Fact]
public async Task TwoRequestsForSameSource_ReturnSameResultAndOneJob()
{
    var first = await command.HandleAsync("owner", documentId, pageId, false, default);
    var second = await command.HandleAsync("owner", documentId, pageId, false, default);
    Assert.Equal(first.ResultId, second.ResultId);
    Assert.Single(queue.Enqueued);
    Assert.Equal($"page:{pageId}:ocr:{first.SourceFingerprint}", queue.Enqueued[0].IdempotencyKey);
}
```

In `OcrRequestConcurrencyTests`, start two scoped `RequestPageOcr` calls simultaneously against PostgreSQL Testcontainers and assert one `page_ocr_results` row and one `processing_jobs` row. In the application tests, cover the Ready-page requirement, removed/unowned/missing not-found semantics, failed retry rules, reactivation of the existing failed job, and current query returning NotRequested when only a stale result exists.

- [ ] **Step 2: Run focused tests and verify they fail**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter "FullyQualifiedName~RequestPageOcrTests|FullyQualifiedName~GetPageOcrTests"`

Run: `dotnet test tests/SuperScanner.Infrastructure.IntegrationTests/SuperScanner.Infrastructure.IntegrationTests.csproj --filter FullyQualifiedName~OcrRequestConcurrencyTests`

Expected: FAIL because request/query services do not exist.

- [ ] **Step 3: Implement fingerprints and stable DTOs**

```csharp
public static class OcrSourceFingerprint
{
    public static string Create(string sourceObjectKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceObjectKey))).ToLowerInvariant();
}

public sealed record PageOcrDto(Guid? ResultId, string State, string? SourceFingerprint,
    string? FullText, double? AggregateConfidence, int ElementCount,
    string? FailureCode, bool CanRetry, DateTimeOffset? QueuedAt,
    DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
    IReadOnlyList<OcrElementDto> Elements);

public sealed record OcrElementDto(Guid Id, string Kind, string Text,
    double Confidence, string TextType, int ReadingOrder,
    IReadOnlyList<OcrPoint> Polygon, IReadOnlyList<OcrElementDto> Children);

public sealed class OcrResourceNotFoundException : Exception { }
public sealed class OcrPageNotReadyException : Exception { }
public sealed class OcrRetryNotAllowedException : Exception { }

public static class OcrDtoMapper
{
    public static PageOcrDto Map(PageOcrResult? result) => result is null
        ? new(null, "NotRequested", null, null, null, 0, null, false,
            null, null, null, [])
        : new(result.Id, result.State.ToString(), result.SourceFingerprint,
            result.State == OcrResultState.Ready ? result.FullText : null,
            result.State == OcrResultState.Ready ? result.AggregateConfidence : null,
            result.State == OcrResultState.Ready ? result.ElementCount : 0,
            result.State == OcrResultState.Failed ? result.FailureCode : null,
            result.State == OcrResultState.Failed && result.FailureRetryable,
            result.QueuedAt, result.StartedAt, result.CompletedAt,
            result.State == OcrResultState.Ready
                ? result.Elements.Where(x => x.ParentElementId is null)
                    .OrderBy(x => x.ReadingOrder)
                    .Select(x => MapElement(x, result.Elements)).ToArray()
                : []);

    private static OcrElementDto MapElement(OcrElement element,
        IReadOnlyCollection<OcrElement> all) => new(element.Id,
            element.Kind.ToString(), element.Text, element.Confidence,
            element.TextType.ToString(), element.ReadingOrder, element.Polygon,
            all.Where(x => x.ParentElementId == element.Id)
                .OrderBy(x => x.ReadingOrder)
                .Select(x => MapElement(x, all)).ToArray());
}
```

`GetPageOcr` returns `State = "NotRequested"` with empty elements when no result matches the current preview key. Map elements in parent/reading order and expose text only for Ready.

- [ ] **Step 4: Implement transactional request/retry behavior**

Within `RequestPageOcr`, begin the repository transaction, lock/find the owned Ready source, compute the fingerprint, reuse an existing result, or add a Queued result. Enqueue `RecognizePageText` with payload equal to the OCR result ID and idempotency key `page:{pageId}:ocr:{fingerprint}` before committing. A Failed result can be retried only when `retryFailed` and `FailureRetryable` are true.

```csharp
await using var transaction = await repository.BeginTransactionAsync(ct);
var source = await repository.FindOwnedReadySourceAsync(ownerUid, documentId, pageId, ct)
    ?? throw new OcrResourceNotFoundException();
var fingerprint = OcrSourceFingerprint.Create(source.SourceObjectKey);
var result = await repository.FindBySourceAsync(pageId, fingerprint, true, ct);
var reactivatedFailedJob = false;
if (result is null)
{
    result = PageOcrResult.Queue(Guid.NewGuid(), pageId, source.SourceObjectKey,
        fingerprint, "en", clock.UtcNow);
    await repository.AddAsync(result, ct);
}
else if (result.State == OcrResultState.Failed)
{
    if (!retryFailed || !result.FailureRetryable) throw new OcrRetryNotAllowedException();
    result.Retry(clock.UtcNow);
    await queue.RetryFailedAsync($"page:{pageId}:ocr:{fingerprint}", ct);
    reactivatedFailedJob = true;
}
if (!reactivatedFailedJob)
{
    await queue.EnqueueAsync("RecognizePageText", result.Id.ToString(),
        $"page:{pageId}:ocr:{fingerprint}", ct);
}
await repository.SaveChangesAsync(ct);
await transaction.CommitAsync(ct);
return OcrDtoMapper.Map(result);
```

Add `RetryFailedAsync(string idempotencyKey, CancellationToken ct)` to `IProcessingJobQueue`. `PostgresJobQueue` must load only a Failed row with that exact key, call the existing `ProcessingJob.Retry(clock.UtcNow)`, and save within the caller's current transaction. A missing or non-Failed key throws `InvalidOperationException` so the OCR result cannot claim Queued while its job remains terminal.

- [ ] **Step 5: Run command/query tests**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter "FullyQualifiedName~RequestPageOcrTests|FullyQualifiedName~GetPageOcrTests"`

Expected: PASS, including duplicate, concurrency, stale-source, and ownership cases.

- [ ] **Step 6: Commit Task 4**

```powershell
git add src/SuperScanner.Application/Ocr src/SuperScanner.Application/Abstractions/IProcessingJobQueue.cs src/SuperScanner.Infrastructure/Processing/PostgresJobQueue.cs tests
git commit -m "feat: request and query page OCR"
```

---

### Task 5: Owner-authorized OCR API

**Files:**
- Create: `src/SuperScanner.Api/Endpoints/OcrEndpoints.cs`
- Modify: `src/SuperScanner.Api/Program.cs`
- Test: `tests/SuperScanner.Api.IntegrationTests/Documents/OcrEndpointsTests.cs`

**Interfaces:**
- Consumes: `RequestPageOcr`, `GetPageOcr`, `ICurrentUser`, and existing authentication/App Check middleware.
- Produces: `POST` and `GET /api/documents/{documentId}/pages/{pageId}/ocr`.

- [ ] **Step 1: Write failing HTTP boundary tests**

```csharp
[Fact]
public async Task PostThenGet_ReturnsAcceptedAndCurrentStatus()
{
    var post = await client.PostAsJsonAsync($"/api/documents/{documentId}/pages/{pageId}/ocr",
        new { retryFailed = false });
    Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);
    Assert.Equal("Queued", (await post.Content.ReadFromJsonAsync<JsonElement>())
        .GetProperty("state").GetString());
    Assert.Equal(HttpStatusCode.OK,
        (await client.GetAsync($"/api/documents/{documentId}/pages/{pageId}/ocr")).StatusCode);
}
```

Test no-result GET as 200/NotRequested; unowned, removed, and missing pages as indistinguishable 404; disabled OCR POST as 503 without creating a result/job; non-Ready page as 409 with stable message; unauthenticated and missing/invalid App Check as 401; full text/elements absent until Ready; Failed returns only safe failure data.

- [ ] **Step 2: Run endpoint tests and verify they fail**

Run: `dotnet test tests/SuperScanner.Api.IntegrationTests/SuperScanner.Api.IntegrationTests.csproj --filter FullyQualifiedName~OcrEndpointsTests`

Expected: FAIL with 404 because routes are not mapped.

- [ ] **Step 3: Map the endpoint group and DI registrations**

```csharp
var group = endpoints
    .MapGroup("/api/documents/{documentId:guid}/pages/{pageId:guid}/ocr")
    .RequireAuthorization();
group.MapGet("/", GetAsync);
group.MapPost("/", RequestAsync);
```

Bind and validate `OcrOptions` in the API, register `IOcrRepository`, `RequestPageOcr`, and `GetPageOcr`, and call `OcrEndpoints.Map(app)`. POST returns `503 Service Unavailable` with `{ code = "ocr_disabled" }` before invoking the command when `Ocr:Enabled` is false. Translate `OcrResourceNotFoundException` to 404 and `OcrPageNotReadyException`/`OcrRetryNotAllowedException` to 409 without returning internal exception text.

- [ ] **Step 4: Run API tests**

Run: `dotnet test tests/SuperScanner.Api.IntegrationTests/SuperScanner.Api.IntegrationTests.csproj --filter "FullyQualifiedName~OcrEndpointsTests|FullyQualifiedName~AuthenticationBoundaryTests"`

Expected: PASS for status mapping, owner isolation, Firebase auth, and App Check.

- [ ] **Step 5: Commit Task 5**

```powershell
git add src/SuperScanner.Api/Endpoints/OcrEndpoints.cs src/SuperScanner.Api/Program.cs tests/SuperScanner.Api.IntegrationTests/Documents/OcrEndpointsTests.cs
git commit -m "feat: expose secure page OCR endpoints"
```

---

### Task 6: OCR worker execution, safe terminal failure, and stale completion

**Files:**
- Modify: `src/SuperScanner.Domain/Processing/ProcessingJob.cs`
- Modify: `src/SuperScanner.Application/Abstractions/IProcessingJobQueue.cs`
- Modify: `src/SuperScanner.Infrastructure/Processing/PostgresJobQueue.cs`
- Create: `src/SuperScanner.Infrastructure/Ocr/OcrProcessor.cs`
- Create: `src/SuperScanner.Infrastructure/Ocr/OcrMetrics.cs`
- Modify: `src/SuperScanner.Worker/UploadValidationJobRunner.cs`
- Modify: `src/SuperScanner.Worker/Program.cs`
- Modify: queue fakes implementing `IProcessingJobQueue` under `tests/`
- Create: `tests/SuperScanner.Application.Tests/Ocr/OcrWorkerTests.cs`
- Modify: `tests/SuperScanner.Infrastructure.IntegrationTests/Processing/JobLeaseTests.cs`

**Interfaces:**
- Consumes: OCR result ID job payload, `IOcrProvider`, `IObjectStore`, `OcrResultValidator`, `OcrOptions`, and Page current preview key.
- Produces: `OcrProcessor.RunAsync(resultId, attemptNumber, ct)`, `OcrProcessor.FailAsync(resultId, safeCode, retryable, ct)`, and `IProcessingJobQueue.FailAsync(...)` for non-retryable/terminal jobs.

- [ ] **Step 1: Write failing processor and queue tests**

```csharp
[Fact]
public async Task EmptyRecognition_CompletesReadyWithoutChangingReadyPage()
{
    await processor.RunAsync(result.Id, 1, default);
    Assert.Equal(OcrResultState.Ready, result.State);
    Assert.Empty(result.Elements);
    Assert.Equal(PageState.Ready, page.State);
}

[Fact]
public async Task LateOldRevisionCompletion_IsStoredButNotCurrent()
{
    page.SetPreview("previews/new.jpg", "thumbs/new.jpg");
    await processor.RunAsync(oldResult.Id, 1, default);
    Assert.Equal(OcrResultState.Ready, oldResult.State);
    Assert.Null(await repository.FindCurrentOwnedAsync("owner", document.Id, page.Id, default));
}
```

Add tests for private object streaming, timeout cancellation, invalid output rolling back all elements, retryable exception rescheduling, non-retryable exception calling queue `FailAsync`, max-attempt failure, invalid OCR payload, and telemetry containing numeric counts/timings and allow-listed tags but not recognized text or object keys.

- [ ] **Step 2: Run worker tests and verify they fail**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter FullyQualifiedName~OcrWorkerTests`

Expected: FAIL because the processor and queue terminal-failure operation do not exist.

- [ ] **Step 3: Add explicit queue terminal failure**

```csharp
public void Fail(string workerId, DateTimeOffset now, string errorCode)
{
    EnsureOwnedLease(workerId);
    ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
    Status = ProcessingJobStatus.Failed;
    WorkerId = null;
    LeaseExpiresAt = null;
    UpdatedAt = now;
    ErrorCode = errorCode;
}
```

Add `FailAsync` to `IProcessingJobQueue`, implement it through `MutateOwnedLeaseAsync`, and update all recording queue fakes to record the safe code.

- [ ] **Step 4: Implement transactional OCR processing**

`OcrProcessor.RunAsync` loads the Queued or Processing result and Page, calls `BeginAttempt(attemptNumber, now)`, opens `SourceObjectKey`, invokes the provider with `CancelAfter(OcrOptions.TimeoutSeconds)`, validates before mutation, creates domain elements with resolved parent IDs, then saves all elements and Ready state in one transaction. Replayed Ready/Failed results return without provider work. A source mismatch does not stop historical completion and never changes Page or Document state.

```csharp
var result = await db.PageOcrResults.Include(x => x.Elements)
    .SingleAsync(x => x.Id == resultId, ct);
if (result.State is OcrResultState.Ready or OcrResultState.Failed) return;
result.BeginAttempt(attemptNumber, clock.UtcNow);
await db.SaveChangesAsync(ct);
await using var content = await store.OpenReadAsync(result.SourceObjectKey, ct);
using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
NormalizedOcrDocument providerResult;
try
{
    providerResult = await provider.RecognizeAsync(
        new(content, "image/jpeg", options.Value.Language), timeout.Token);
}
catch (OperationCanceledException) when (!ct.IsCancellationRequested)
{
    throw new OcrProviderException("ocr_timeout", true);
}
var normalized = OcrResultValidator.Validate(providerResult,
    new(options.Value.MaxElements, options.Value.MaxRecognizedCharacters));
var elementIds = normalized.Elements.ToDictionary(x => x.ClientId, _ => Guid.NewGuid());
var elements = normalized.Elements.Select(x => OcrElement.Create(
    elementIds[x.ClientId], result.Id,
    x.ParentClientId is null ? null : elementIds[x.ParentClientId],
    x.Kind, x.Text, x.Confidence, x.TextType, x.ReadingOrder, x.Polygon)).ToArray();
result.Complete(normalized.ProviderName, normalized.ModelVersion,
    normalized.FullText, elements, clock.UtcNow);
await db.SaveChangesAsync(ct);
```

Record queue-to-start duration, provider latency, element count, aggregate confidence, outcome, retry count, stale-current status, and safe failure code through `System.Diagnostics.Metrics`. `OcrMetrics` must expose methods whose tag lists contain only `provider`, `outcome`, `failure_code`, and `stale`; page/result IDs remain structured log properties and no content-derived value becomes a metric tag.

```csharp
public sealed class OcrMetrics
{
    private readonly Meter meter = new("SuperScanner.Ocr");
    private readonly Counter<long> jobs;
    private readonly Histogram<double> providerMilliseconds;
    private readonly Histogram<long> elements;

    public OcrMetrics()
    {
        jobs = meter.CreateCounter<long>("superscanner.ocr.jobs");
        providerMilliseconds = meter.CreateHistogram<double>("superscanner.ocr.provider.duration", "ms");
        elements = meter.CreateHistogram<long>("superscanner.ocr.elements");
    }

    public void Completed(string provider, double elapsedMs, int elementCount, bool stale)
    {
        jobs.Add(1, new("provider", provider), new("outcome", "ready"), new("stale", stale));
        providerMilliseconds.Record(elapsedMs, new("provider", provider));
        elements.Record(elementCount, new("provider", provider));
    }
}
```

- [ ] **Step 5: Dispatch `RecognizePageText` with bounded failure behavior**

In `UploadValidationJobRunner`, parse OCR payload as exactly one GUID. Dispatch to `OcrProcessor`. Catch `OcrProviderException`: reschedule only when `Retryable` and `lease.AttemptCount < OcrOptions.MaxAttempts`; otherwise mark the OCR result Failed with the safe code and call queue `FailAsync`. Convert timeout to `ocr_timeout` and unknown exceptions to `ocr_failed`; never log exception messages that may contain provider content.

```csharp
try
{
    if (lease.Type == "RecognizePageText")
        await ocrProcessor.RunAsync(resourceId, lease.AttemptCount, workCancellation.Token);
}
catch (OcrProviderException failure)
{
    if (failure.Retryable && lease.AttemptCount < ocrOptions.MaxAttempts)
        await queue.RescheduleAsync(lease.Id, workerId, failure.SafeCode, cancellationToken);
    else
    {
        await ocrProcessor.FailAsync(resourceId, failure.SafeCode, failure.Retryable, cancellationToken);
        await queue.FailAsync(lease.Id, workerId, failure.SafeCode, cancellationToken);
    }
}
```

- [ ] **Step 6: Register validated options and provider in Worker**

Bind `OcrOptions`, validate on startup using the environment name, register `FakeOcrProvider` only when configured as Fake, and register `OcrProcessor`. Disabled OCR still lets the worker process every existing job type.

```csharp
builder.Services.AddOptions<OcrOptions>()
    .BindConfiguration(OcrOptions.SectionName)
    .Validate(options => options.IsValid(builder.Environment.EnvironmentName),
        "OCR configuration is invalid.")
    .ValidateOnStart();
if (builder.Configuration["Ocr:Provider"] == "Fake")
    builder.Services.AddSingleton<IOcrProvider, FakeOcrProvider>();
builder.Services.AddScoped<OcrProcessor>();
```

- [ ] **Step 7: Run worker and queue tests**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter "FullyQualifiedName~OcrWorkerTests|FullyQualifiedName~UploadValidationJobRunnerTests"`

Run: `dotnet test tests/SuperScanner.Infrastructure.IntegrationTests/SuperScanner.Infrastructure.IntegrationTests.csproj --filter FullyQualifiedName~JobLeaseTests`

Expected: PASS, including retry bounds, permanent failure, stale completion, empty OCR, and existing job dispatch.

- [ ] **Step 8: Commit Task 6**

```powershell
git add src/SuperScanner.Domain/Processing src/SuperScanner.Application/Abstractions/IProcessingJobQueue.cs src/SuperScanner.Infrastructure/Ocr src/SuperScanner.Infrastructure/Processing/PostgresJobQueue.cs src/SuperScanner.Worker tests
git commit -m "feat: process OCR jobs safely"
```

---

### Task 7: Automatic OCR scheduling after a Ready page revision

**Files:**
- Create: `src/SuperScanner.Infrastructure/Ocr/OcrJobScheduler.cs`
- Modify: `src/SuperScanner.Infrastructure/Processing/CropProcessor.cs`
- Modify: `src/SuperScanner.Worker/Program.cs`
- Modify: `src/SuperScanner.Worker/appsettings.json`
- Modify: `src/SuperScanner.Worker/appsettings.Development.json`
- Modify: `src/SuperScanner.Api/appsettings.json`
- Test: `tests/SuperScanner.Application.Tests/Ocr/OcrSchedulingTests.cs`

**Interfaces:**
- Consumes: page ID, newly written preview object key, `OcrOptions.Enabled`, current EF transaction, and `IProcessingJobQueue`.
- Produces: `OcrJobScheduler.EnsureQueuedAsync(pageId, sourceObjectKey, mediaType, ct)` used immediately after successful crop/filter completion.

- [ ] **Step 1: Write failing scheduling tests**

```csharp
[Fact]
public async Task SuccessfulCrop_WhenEnabled_QueuesExactNewPreviewOnce()
{
    await crop.CompletePerspectiveCropAsync(page.Id, revision, "previews/new.jpg", "thumbs/new.jpg", default);
    Assert.Single(await db.PageOcrResults.Where(x => x.PageId == page.Id).ToListAsync());
    Assert.Single(await db.ProcessingJobs.Where(x => x.Type == "RecognizePageText").ToListAsync());
}

[Fact]
public async Task SupersededCrop_DoesNotQueueOcrForRejectedRevision()
{
    Assert.False(await crop.CompletePerspectiveCropAsync(page.Id, revision - 1,
        "previews/old.jpg", "thumbs/old.jpg", default));
    Assert.Empty(db.PageOcrResults);
}
```

Also test disabled configuration creates no OCR row/job, repeated completion is idempotent, and a new filter/crop preview creates a second source result without making the first current.

- [ ] **Step 2: Run scheduling tests and verify they fail**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter FullyQualifiedName~OcrSchedulingTests`

Expected: FAIL because successful crop does not schedule OCR.

- [ ] **Step 3: Implement scheduler and atomic crop hook**

`OcrJobScheduler` returns immediately when disabled. Otherwise it hashes `sourceObjectKey`, ensures the `(pageId, fingerprint)` result exists, and enqueues the idempotent job using the current EF transaction. Inject it into `CropProcessor`; call it only inside the `updated == 1` branch of `CompletePerspectiveCropAsync`, after the Page update and before transaction commit.

```csharp
if (!options.Value.Enabled) return;
var fingerprint = OcrSourceFingerprint.Create(sourceObjectKey);
var result = await repository.FindBySourceAsync(pageId, fingerprint, true, ct);
if (result is null)
{
    result = PageOcrResult.Queue(Guid.NewGuid(), pageId, sourceObjectKey,
        fingerprint, options.Value.Language, clock.UtcNow);
    await repository.AddAsync(result, ct);
}
await queue.EnqueueAsync("RecognizePageText", result.Id.ToString(),
    $"page:{pageId}:ocr:{fingerprint}", ct);
await repository.SaveChangesAsync(ct);
```

- [ ] **Step 4: Add safe default configuration**

```json
"Ocr": {
  "Enabled": false,
  "Provider": "Disabled",
  "Language": "en",
  "MaxAttempts": 3,
  "TimeoutSeconds": 30,
  "MaxElements": 10000,
  "MaxRecognizedCharacters": 1000000
}
```

Keep the checked-in default disabled. Development may use Fake only when the developer explicitly changes both Enabled and Provider; do not commit credentials.

- [ ] **Step 5: Run scheduling and crop regression tests**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter "FullyQualifiedName~OcrSchedulingTests|FullyQualifiedName~CropDocumentStatusTests"`

Expected: PASS with OCR enabled and disabled cases, plus existing crop behavior.

- [ ] **Step 6: Commit Task 7**

```powershell
git add src/SuperScanner.Infrastructure/Ocr/OcrJobScheduler.cs src/SuperScanner.Infrastructure/Processing/CropProcessor.cs src/SuperScanner.Worker/Program.cs src/SuperScanner.Worker/appsettings*.json src/SuperScanner.Api/appsettings.json tests/SuperScanner.Application.Tests/Ocr/OcrSchedulingTests.cs
git commit -m "feat: schedule OCR for ready page revisions"
```

---

### Task 8: Angular OCR status card and polling lifecycle

**Files:**
- Modify: `apps/web/src/app/documents/document.models.ts`
- Modify: `apps/web/src/app/documents/documents-api.service.ts`
- Create: `apps/web/src/app/documents/ocr-status.component.ts`
- Create: `apps/web/src/app/documents/ocr-status.component.html`
- Create: `apps/web/src/app/documents/ocr-status.component.scss`
- Create: `apps/web/src/app/documents/ocr-status.component.spec.ts`
- Modify: `apps/web/src/app/documents/document-detail.component.ts`
- Modify: `apps/web/src/app/documents/document-detail.component.html`
- Modify: `apps/web/src/app/documents/document-detail.component.spec.ts`

**Interfaces:**
- Consumes: GET/POST OCR endpoints from Task 5 and existing document/page IDs.
- Produces: standalone `OcrStatusComponent` with Not requested, Queued, Processing, Ready, and Failed presentations; no overlay or editor.

- [ ] **Step 1: Write failing API-service and component tests**

```typescript
it('polls pending OCR and stops when Ready', async () => {
  api.getPageOcr.mockResolvedValueOnce(queued).mockResolvedValueOnce(ready);
  fixture.componentRef.setInput('documentId', 'd1');
  fixture.componentRef.setInput('pageId', 'p1');
  fixture.detectChanges();
  await vi.advanceTimersByTimeAsync(3000);
  expect(screen.textContent).toContain('12 words recognized');
  expect(api.getPageOcr).toHaveBeenCalledTimes(2);
});

it('does not mutate state after destroy while a request resolves', async () => {
  const pending = deferred<PageOcr>();
  api.getPageOcr.mockReturnValue(pending.promise);
  fixture.detectChanges();
  fixture.destroy();
  pending.resolve(ready);
  await pending.promise;
  expect(component.status()).toBeNull();
});
```

Test request action, retry action, no fabricated percentage, safe error copy, timer teardown, and only showing recognized counts/confidence/completion time in Ready.

- [ ] **Step 2: Run Angular tests and verify they fail**

Run: `npm test -- --watch=false --include src/app/documents/ocr-status.component.spec.ts` from `apps/web`.

Expected: FAIL because the component and client methods do not exist.

- [ ] **Step 3: Add typed client models and methods**

```typescript
export type OcrState = 'NotRequested' | 'Queued' | 'Processing' | 'Ready' | 'Failed';
export interface PageOcr {
  resultId?: string | null;
  state: OcrState;
  fullText?: string | null;
  aggregateConfidence?: number | null;
  elementCount: number;
  failureCode?: string | null;
  canRetry: boolean;
  completedAt?: string | null;
  elements: OcrElement[];
}
```

Add `getPageOcr(documentId, pageId)` and `requestPageOcr(documentId, pageId, retryFailed)` to `DocumentsApiService`.

- [ ] **Step 4: Implement accessible status UI and bounded polling**

The component loads once on input initialization, schedules a 3-second timeout only for Queued/Processing, clears it before every request and on destroy, and ignores late results after destruction. Buttons are labeled `Recognize text` and `Retry text recognition`. Render a status region with `aria-live="polite"`; expose no text overlay or editing control.

```typescript
private schedule(status: PageOcr): void {
  clearTimeout(this.timer);
  if (!this.destroyed && (status.state === 'Queued' || status.state === 'Processing')) {
    this.timer = setTimeout(() => void this.load(), 3000);
  }
}

ngOnDestroy(): void {
  this.destroyed = true;
  clearTimeout(this.timer);
}
```

- [ ] **Step 5: Mount one OCR card per Ready page**

Import `OcrStatusComponent` into `DocumentDetailComponent` and render it adjacent to each Ready page card using the document/page IDs. Do not alter page reordering, crop links, preview polling, or export eligibility.

```html
@if (page.state === 'Ready') {
  <app-ocr-status [documentId]="doc.id" [pageId]="page.id" />
}
```

- [ ] **Step 6: Run Angular unit suite and production build**

Run from `apps/web`: `npm test -- --watch=false`

Run from `apps/web`: `npm run build`

Expected: all tests PASS and production build completes without template/type errors.

- [ ] **Step 7: Commit Task 8**

```powershell
git add apps/web/src/app/documents
git commit -m "feat: show page OCR status"
```

---

### Task 9: Fake-provider end-to-end acceptance and operational documentation

**Files:**
- Modify: `src/SuperScanner.Api/appsettings.E2E.json`
- Create: `src/SuperScanner.Worker/appsettings.E2E.json`
- Modify: `apps/web/e2e/server.mjs`
- Create: `apps/web/e2e/ocr-foundation.spec.ts`
- Create: `docs/operations/ocr-foundation.md`

**Interfaces:**
- Consumes: complete backend and Angular OCR flow from Tasks 1–8.
- Produces: opt-in local E2E path using Fake, plus operator instructions for disabled/Fake modes and Phase 3B handoff.

- [ ] **Step 1: Write the failing E2E scenario**

```typescript
test('ready page can recognize text and surface a terminal result', async ({ page }) => {
  test.skip(process.env['E2E_OCR_READY'] !== '1',
    'Requires PostgreSQL, API, Worker, private object storage, and Fake OCR configuration.');
  await page.goto('/e2e-login?user=user-a');
  await page.getByLabel('Document title').fill('OCR acceptance');
  await page.getByLabel('Choose file').setInputFiles('e2e/fixtures/append-photo.jpg');
  await page.getByRole('button', { name: 'Upload securely' }).click();
  await page.getByRole('link', { name: 'Open document preview' }).click();
  await page.getByRole('link', { name: 'Crop & filters' }).click();
  await page.getByRole('button', { name: 'Save scan' }).click();
  await expect(page.getByText(/words recognized/)).toBeVisible({ timeout: 60_000 });
  await expect(page.getByRole('button', { name: 'Retry text recognition' })).toHaveCount(0);
});
```

Use the existing valid image upload fixture. The Fake provider returns its fixed normalized hierarchy for that private processed image; do not add a provider bypass route to production code.

- [ ] **Step 2: Run the E2E scenario and verify it fails before configuration**

Run from `apps/web`: `$env:E2E_OCR_READY='1'; npx playwright test e2e/ocr-foundation.spec.ts`

Expected: FAIL because E2E Worker OCR settings/fixture wiring are not complete.

- [ ] **Step 3: Wire E2E Fake configuration and document operations**

Configure both E2E API and E2E Worker with `Ocr:Enabled=true` and `Ocr:Provider=Fake`; the API needs Enabled for request admission and the Worker needs Fake for execution. Keep all non-E2E defaults credential-free. Document configuration keys, safe production rejection of Fake, numeric metrics, failure codes, how to disable OCR without affecting scan/export, and that Google setup begins only in Phase 3B.

```json
{
  "Ocr": {
    "Enabled": true,
    "Provider": "Fake",
    "Language": "en",
    "MaxAttempts": 3,
    "TimeoutSeconds": 30,
    "MaxElements": 10000,
    "MaxRecognizedCharacters": 1000000
  }
}
```

- [ ] **Step 4: Run focused E2E and full verification**

Run from `apps/web`: `$env:E2E_OCR_READY='1'; npx playwright test e2e/ocr-foundation.spec.ts`

Run: `dotnet test SuperScanner.slnx`

Run from `apps/web`: `npm test -- --watch=false`

Run from `apps/web`: `npm run build`

Expected: OCR E2E PASS; all .NET and Angular tests PASS; Angular production build succeeds.

- [ ] **Step 5: Inspect privacy and scope boundaries**

Run: `rg -n "Log(Trace|Debug|Information|Warning|Error|Critical).*?(FullText|Text|SourceObjectKey|signed|credential)" src`

Expected: no OCR content, object keys, signed URLs, or credentials in log templates.

Run: `rg -n "Google.Cloud|DocumentAi|GoogleDocumentAi" src apps tests`

Expected: no matches; Phase 3A contains no live Google SDK, adapter, or selectable Google provider.

- [ ] **Step 6: Commit Task 9**

```powershell
git add src/SuperScanner.Api/appsettings.E2E.json src/SuperScanner.Worker/appsettings.E2E.json apps/web/e2e docs/operations/ocr-foundation.md
git commit -m "test: verify OCR foundation end to end"
```

## Completion gate

Before declaring Phase 3A complete, use `superpowers:verification-before-completion`, run every command in Task 9 Step 4 against the final branch, inspect `git diff --check`, and confirm the database migration applies to a fresh PostgreSQL database. Then use `superpowers:requesting-code-review` for a whole-branch review before any merge or push decision.
