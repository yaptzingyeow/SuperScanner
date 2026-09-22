# Google Document AI OCR Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Connect the existing Phase 3A OCR pipeline to the configured Singapore Google Document AI processor for secure local recognition of printed and handwritten English.

**Architecture:** Add one Google-specific adapter behind the existing `IOcrProvider` boundary. Keep Google request creation, response normalization, and RPC failure translation in focused infrastructure classes, select the provider in the Worker composition root, and authenticate locally through Application Default Credentials without changing the public OCR API.

**Tech Stack:** .NET 10, C# 14, `Google.Cloud.DocumentAI.V1` 3.25.0, Google Application Default Credentials, xUnit, ASP.NET Core options validation, existing PostgreSQL queue/R2 object store/OCR domain model.

**Spec:** `docs/superpowers/specs/2026-09-22-google-document-ai-ocr-design.md`

## Global Constraints

- Use project `superscanner-dev`, location `asia-southeast1`, processor `fc0b14e64c62e7aa`, and endpoint `asia-southeast1-documentai.googleapis.com`.
- English (`en`) remains the only accepted Phase 3 language.
- Local authentication uses Application Default Credentials; never commit a credential JSON file, refresh token, access token, or service-account key.
- Google types remain inside `SuperScanner.Infrastructure` and the Worker composition root.
- Never log recognized text, image bytes, filenames, object keys, signed URLs, credential paths, tokens, raw Google requests, raw Google responses, or raw provider exception messages.
- Preserve the Phase 3A `IOcrProvider`, API DTOs, database schema, source-fingerprint idempotency, stale-result protection, and bounded retry behavior.
- Production and Railway keep OCR disabled in this plan; Workload Identity Federation is a later promotion gate.
- Live acceptance is opt-in and billable; ordinary unit/integration/CI runs must not contact Google.
- Do not implement selectable overlays, manual correction, replacement, font matching, reflow, undo/redo, handwriting generation, or searchable PDFs.

## File Structure

- `src/SuperScanner.Infrastructure/Ocr/GoogleDocumentAiOptions.cs` — Google resource names and input limits.
- `src/SuperScanner.Infrastructure/Ocr/DocumentAiClient.cs` — narrow wrapper around the official client, regional endpoint, ADC, and `ProcessDocumentAsync`.
- `src/SuperScanner.Infrastructure/Ocr/DocumentAiTextAnchorReader.cs` — safe extraction of UTF-8 indexed text segments.
- `src/SuperScanner.Infrastructure/Ocr/DocumentAiResultMapper.cs` — provider response to normalized block/line/word hierarchy.
- `src/SuperScanner.Infrastructure/Ocr/GoogleDocumentAiOcrProvider.cs` — media/size checks, call orchestration, safe error translation, and metrics.
- `src/SuperScanner.Infrastructure/Ocr/OcrOptions.cs` — allow `GoogleDocumentAi` only with valid Google options.
- `src/SuperScanner.Worker/OcrServiceCollectionExtensions.cs` — testable, exact provider selection and DI registration.
- `src/SuperScanner.Worker/Program.cs` — invokes the provider-specific registration.
- `tests/SuperScanner.Application.Tests/Ocr/GoogleDocumentAiOptionsTests.cs` — configuration validation.
- `tests/SuperScanner.Application.Tests/Ocr/DocumentAiTextAnchorReaderTests.cs` — byte-index text extraction.
- `tests/SuperScanner.Application.Tests/Ocr/DocumentAiResultMapperTests.cs` — hierarchy, geometry, confidence, and handwriting mapping.
- `tests/SuperScanner.Application.Tests/Ocr/GoogleDocumentAiOcrProviderTests.cs` — request limits, client orchestration, cancellation, and failure codes.
- `tests/SuperScanner.Application.Tests/Ocr/GoogleDocumentAiLiveTests.cs` — explicitly gated live acceptance.
- `tests/SuperScanner.Application.Tests/Fixtures/Ocr/google-document-ai-response.json` — synthetic provider response only.
- `docs/operations/ocr-foundation.md` — ADC setup, local enablement, live test, disablement, and production gate.

## Review Focus

1. A non-ASCII string before an anchor must not shift later UTF-8 byte offsets; Task 3 adds a multibyte-anchor test.
2. A page with missing normalized vertices but valid absolute vertices must normalize using page dimensions; Task 4 tests this fallback.
3. A Google response with missing or zero page dimensions must fail safely rather than divide by zero; Task 4 tests `ocr_invalid_response`.
4. `PermissionDenied` and `Unauthenticated` must never be retried indefinitely; Task 6 pins both as permanent `ocr_auth_failed` failures.
5. An input stream one byte above `MaxInputBytes` must be rejected before the client is called; Task 5 verifies zero provider calls.

---

### Task 1: Pin the Google SDK and validate configuration

**Files:**
- Modify: `src/SuperScanner.Infrastructure/SuperScanner.Infrastructure.csproj`
- Modify: `src/SuperScanner.Infrastructure/Ocr/OcrOptions.cs`
- Create: `src/SuperScanner.Infrastructure/Ocr/GoogleDocumentAiOptions.cs`
- Create: `tests/SuperScanner.Application.Tests/Ocr/GoogleDocumentAiOptionsTests.cs`
- Modify: `src/SuperScanner.Infrastructure/packages.lock.json`

**Interfaces:**
- Consumes: `OcrOptions.IsValid(string environmentName)`.
- Produces: `GoogleDocumentAiOptions` and `GoogleDocumentAiOptions.IsValid()`; `OcrOptions.Google`; support for provider name `GoogleDocumentAi` in Development while retaining the Production Fake rejection.

- [ ] **Step 1: Write failing option tests**

Create tests covering the exact valid resource, blank project/location/processor, mismatched endpoint, non-English language, non-positive/oversized input limits, enabled Google in Development, disabled Google, Fake in Production, and Google in Production with valid options. Use this valid baseline:

```csharp
private static OcrOptions ValidGoogle() => new()
{
    Enabled = true,
    Provider = OcrProviderNames.GoogleDocumentAi,
    Language = "en",
    Google = new GoogleDocumentAiOptions
    {
        ProjectId = "superscanner-dev",
        Location = "asia-southeast1",
        ProcessorId = "fc0b14e64c62e7aa",
        Endpoint = "asia-southeast1-documentai.googleapis.com",
        MaxInputBytes = 25 * 1024 * 1024
    }
};
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter GoogleDocumentAiOptionsTests`

Expected: compile failure because `GoogleDocumentAiOptions`, `OcrProviderNames`, and `OcrOptions.Google` do not exist.

- [ ] **Step 3: Add the package and minimal option types**

Run: `dotnet add src/SuperScanner.Infrastructure/SuperScanner.Infrastructure.csproj package Google.Cloud.DocumentAI.V1 --version 3.25.0`

Add:

```csharp
public static class OcrProviderNames
{
    public const string Disabled = "Disabled";
    public const string Fake = "Fake";
    public const string GoogleDocumentAi = "GoogleDocumentAi";
}

public sealed class GoogleDocumentAiOptions
{
    public string ProjectId { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string ProcessorId { get; init; } = string.Empty;
    public string? Endpoint { get; init; }
    public long MaxInputBytes { get; init; } = 25 * 1024 * 1024;

    public string EffectiveEndpoint => Endpoint ?? $"{Location}-documentai.googleapis.com";
    public string ProcessorName => $"projects/{ProjectId}/locations/{Location}/processors/{ProcessorId}";
    public bool IsValid();
}
```

`IsValid()` accepts project/location/processor identifiers containing only documented safe identifier characters, requires `asia-southeast1`, requires the endpoint to equal the derived regional endpoint when supplied, and requires `MaxInputBytes` in `1..26_214_400`. Extend `OcrOptions` with `Google` and make enabled Google valid in Development or Production only when the Google options are valid. Keep Fake invalid in Production.

- [ ] **Step 4: Run focused and existing option tests**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter "GoogleDocumentAiOptionsTests|FakeOcrProviderTests"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/SuperScanner.Infrastructure/SuperScanner.Infrastructure.csproj src/SuperScanner.Infrastructure/packages.lock.json src/SuperScanner.Infrastructure/Ocr/OcrOptions.cs src/SuperScanner.Infrastructure/Ocr/GoogleDocumentAiOptions.cs tests/SuperScanner.Application.Tests/Ocr/GoogleDocumentAiOptionsTests.cs
git commit -m "feat: configure Google Document AI OCR"
```

### Task 2: Isolate the regional Document AI client

**Files:**
- Create: `src/SuperScanner.Infrastructure/Ocr/DocumentAiClient.cs`
- Create: `tests/SuperScanner.Application.Tests/Ocr/DocumentAiClientTests.cs`

**Interfaces:**
- Consumes: `GoogleDocumentAiOptions.ProcessorName`, `EffectiveEndpoint`, ADC used by `DocumentProcessorServiceClientBuilder`.
- Produces: `IDocumentAiClient.ProcessAsync(ByteString content, string mediaType, CancellationToken ct)` returning `ProcessResponse`; `DocumentAiClient` official-client wrapper.

- [ ] **Step 1: Write failing client-construction tests**

Test that a builder factory receives exactly `asia-southeast1-documentai.googleapis.com`, that the request name is `projects/superscanner-dev/locations/asia-southeast1/processors/fc0b14e64c62e7aa`, and that content/media type/cancellation reach the wrapper. Keep tests offline by injecting a factory/delegate rather than building ADC.

- [ ] **Step 2: Verify RED**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter DocumentAiClientTests`

Expected: compile failure because `IDocumentAiClient` does not exist.

- [ ] **Step 3: Implement the narrow client wrapper**

```csharp
public interface IDocumentAiClient
{
    Task<ProcessResponse> ProcessAsync(ByteString content, string mediaType, CancellationToken ct);
}

public sealed class DocumentAiClient(
    DocumentProcessorServiceClient client,
    GoogleDocumentAiOptions options) : IDocumentAiClient
{
    public Task<ProcessResponse> ProcessAsync(ByteString content, string mediaType, CancellationToken ct) =>
        client.ProcessDocumentAsync(new ProcessRequest
        {
            Name = options.ProcessorName,
            RawDocument = new RawDocument { Content = content, MimeType = mediaType }
        }, cancellationToken: ct);
}
```

Provide a registration factory that builds `DocumentProcessorServiceClientBuilder { Endpoint = options.EffectiveEndpoint }.Build()` so ADC remains the SDK default. Do not load credential files in application code.

- [ ] **Step 4: Verify GREEN**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter DocumentAiClientTests`

Expected: PASS without network access or ADC.

- [ ] **Step 5: Commit**

```powershell
git add src/SuperScanner.Infrastructure/Ocr/DocumentAiClient.cs tests/SuperScanner.Application.Tests/Ocr/DocumentAiClientTests.cs
git commit -m "feat: add regional Document AI client"
```

### Task 3: Resolve UTF-8 text anchors safely

**Files:**
- Create: `src/SuperScanner.Infrastructure/Ocr/DocumentAiTextAnchorReader.cs`
- Create: `tests/SuperScanner.Application.Tests/Ocr/DocumentAiTextAnchorReaderTests.cs`

**Interfaces:**
- Consumes: provider `Document.Text` and `Document.Types.TextAnchor` whose indices are UTF-8 byte offsets.
- Produces: `DocumentAiTextAnchorReader.Read(string text, TextAnchor? anchor) : string`.

- [ ] **Step 1: Write failing anchor tests**

Cover a single segment, omitted `StartIndex` meaning zero, multiple non-contiguous segments concatenated in order, empty/null anchor, end past the UTF-8 buffer, end before start, and the review-focus case `"éclair name"` where the second anchor starts after the two-byte `é`.

- [ ] **Step 2: Verify RED**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter DocumentAiTextAnchorReaderTests`

Expected: compile failure because the reader does not exist.

- [ ] **Step 3: Implement byte-index extraction**

Encode the full text once as UTF-8, validate every half-open `[start,end)` segment against the byte length, decode each slice with strict UTF-8 fallback, and concatenate segments. Throw `OcrProviderException("ocr_invalid_response", false)` for invalid indices or invalid byte boundaries; never include text in the exception.

- [ ] **Step 4: Verify GREEN**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter DocumentAiTextAnchorReaderTests`

Expected: PASS, including the multibyte offset case.

- [ ] **Step 5: Commit**

```powershell
git add src/SuperScanner.Infrastructure/Ocr/DocumentAiTextAnchorReader.cs tests/SuperScanner.Application.Tests/Ocr/DocumentAiTextAnchorReaderTests.cs
git commit -m "feat: resolve Document AI text anchors"
```

### Task 4: Normalize hierarchy, geometry, confidence, and handwriting

**Files:**
- Create: `src/SuperScanner.Infrastructure/Ocr/DocumentAiResultMapper.cs`
- Create: `tests/SuperScanner.Application.Tests/Ocr/DocumentAiResultMapperTests.cs`
- Create: `tests/SuperScanner.Application.Tests/Fixtures/Ocr/google-document-ai-response.json`

**Interfaces:**
- Consumes: `ProcessResponse.Document`, `DocumentAiTextAnchorReader.Read`.
- Produces: `DocumentAiResultMapper.Map(Document document) : NormalizedOcrDocument` with provider `GoogleDocumentAi` and model/version from response metadata when present, otherwise `document-ocr`.

- [ ] **Step 1: Create a synthetic fixture and failing mapper tests**

The fixture contains two blocks, paragraphs used as normalized lines, tokens used as words, reading order across pages, normalized vertices, absolute-vertex fallback, confidence values, detected breaks, one explicit handwritten token, and no real names or customer text. Tests assert parent IDs, stable IDs (`p{page}-b{block}`, `...-l{line}`, `...-w{word}`), element kinds, full text, order, four-point normalized polygons, confidence, and text type.

Add negative tests for zero page dimensions, fewer than three polygon points, non-finite/out-of-range geometry, anchor failure, and a response exceeding neither mapper nor validator limits. The zero-dimension and absolute-vertex fallback cases satisfy Review Focus items 2 and 3.

- [ ] **Step 2: Verify RED**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter DocumentAiResultMapperTests`

Expected: compile failure because the mapper does not exist.

- [ ] **Step 3: Implement deterministic mapping**

Map pages in response order, blocks in page order, paragraphs as lines, and tokens as words. Resolve each layout text anchor through Task 3. Prefer normalized vertices; otherwise divide absolute X/Y by positive page width/height. Preserve provider vertex order, clamp only floating-point drift within `1e-9`, and reject material range violations. Mark a word handwritten only when Google `StyleInfo.Handwritten` is true; otherwise map it to the existing printed/unknown contract without inference.

- [ ] **Step 4: Run mapper and validator tests**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter "DocumentAiResultMapperTests|OcrResultValidator"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/SuperScanner.Infrastructure/Ocr/DocumentAiResultMapper.cs tests/SuperScanner.Application.Tests/Ocr/DocumentAiResultMapperTests.cs tests/SuperScanner.Application.Tests/Fixtures/Ocr/google-document-ai-response.json
git commit -m "feat: normalize Document AI OCR results"
```

### Task 5: Implement bounded provider orchestration

**Files:**
- Create: `src/SuperScanner.Infrastructure/Ocr/GoogleDocumentAiOcrProvider.cs`
- Create: `tests/SuperScanner.Application.Tests/Ocr/GoogleDocumentAiOcrProviderTests.cs`

**Interfaces:**
- Consumes: `IDocumentAiClient`, `DocumentAiResultMapper`, `OcrInput`, `GoogleDocumentAiOptions`.
- Produces: `GoogleDocumentAiOcrProvider : IOcrProvider`.

- [ ] **Step 1: Write failing provider tests**

Test JPEG and PNG success, `application/pdf` rejection, non-English rejection, empty stream rejection, non-seekable stream support, exact limit acceptance, one-byte-over-limit rejection before any client call, cancellation before/during copy, and passing the mapper result unchanged. Use an in-memory fake `IDocumentAiClient`; never mock `IOcrProvider` itself.

- [ ] **Step 2: Verify RED**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter GoogleDocumentAiOcrProviderTests`

Expected: compile failure because `GoogleDocumentAiOcrProvider` does not exist.

- [ ] **Step 3: Implement minimal provider orchestration**

Accept only `image/jpeg` and `image/png` with language `en`. Copy through a bounded buffer that aborts once `MaxInputBytes + 1` is observed, convert to `ByteString`, call `IDocumentAiClient`, require a non-null document, and map it. Reject unsupported media as `ocr_unsupported_media` and excessive/empty input as a permanent safe failure without calling Google.

- [ ] **Step 4: Verify GREEN**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter GoogleDocumentAiOcrProviderTests`

Expected: PASS, including zero client calls for the over-limit input.

- [ ] **Step 5: Commit**

```powershell
git add src/SuperScanner.Infrastructure/Ocr/GoogleDocumentAiOcrProvider.cs tests/SuperScanner.Application.Tests/Ocr/GoogleDocumentAiOcrProviderTests.cs
git commit -m "feat: call Google Document AI safely"
```

### Task 6: Translate Google failures and record content-safe metrics

**Files:**
- Modify: `src/SuperScanner.Infrastructure/Ocr/GoogleDocumentAiOcrProvider.cs`
- Modify: `src/SuperScanner.Infrastructure/Ocr/OcrMetrics.cs`
- Modify: `tests/SuperScanner.Application.Tests/Ocr/GoogleDocumentAiOcrProviderTests.cs`
- Modify: `tests/SuperScanner.Application.Tests/Ocr/OcrWorkerTests.cs`

**Interfaces:**
- Consumes: `RpcException.StatusCode`, existing `OcrProviderException`, `OcrMetrics`.
- Produces: deterministic safe-code/retryability translation and metric tag `provider=google_document_ai`.

- [ ] **Step 1: Add failing status-mapping tests**

Use theory rows:

```csharp
[InlineData(StatusCode.DeadlineExceeded, "ocr_timeout", true)]
[InlineData(StatusCode.ResourceExhausted, "ocr_rate_limited", true)]
[InlineData(StatusCode.Unavailable, "ocr_provider_unavailable", true)]
[InlineData(StatusCode.Internal, "ocr_provider_unavailable", true)]
[InlineData(StatusCode.Unauthenticated, "ocr_auth_failed", false)]
[InlineData(StatusCode.PermissionDenied, "ocr_auth_failed", false)]
[InlineData(StatusCode.InvalidArgument, "ocr_unsupported_media", false)]
```

Also assert that unknown status becomes `ocr_failed`/non-retryable, caller cancellation remains `OperationCanceledException`, local timeout becomes `ocr_timeout`, and captured logs/metrics contain no fixture text, object key, credential path, or raw RPC detail.

- [ ] **Step 2: Verify RED**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter "GoogleDocumentAiOcrProviderTests|OcrWorkerTests"`

Expected: failures because raw RPC exceptions are not translated.

- [ ] **Step 3: Implement allow-listed translation and metrics**

Catch only expected SDK/transport exceptions around the client call. Convert status using the table above, preserve external cancellation, and record request count/latency/outcome with existing allow-listed tags. Do not log `exception.Message`, `Status.Detail`, response content, or text counts tagged by IDs.

- [ ] **Step 4: Verify GREEN**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter "GoogleDocumentAiOcrProviderTests|OcrWorkerTests"`

Expected: PASS, including permanent authentication failures and retryable transient failures.

- [ ] **Step 5: Commit**

```powershell
git add src/SuperScanner.Infrastructure/Ocr/GoogleDocumentAiOcrProvider.cs src/SuperScanner.Infrastructure/Ocr/OcrMetrics.cs tests/SuperScanner.Application.Tests/Ocr/GoogleDocumentAiOcrProviderTests.cs tests/SuperScanner.Application.Tests/Ocr/OcrWorkerTests.cs
git commit -m "feat: classify Google OCR failures"
```

### Task 7: Wire provider selection into the Worker

**Files:**
- Modify: `src/SuperScanner.Worker/Program.cs`
- Modify: `src/SuperScanner.Worker/appsettings.json`
- Modify: `src/SuperScanner.Worker/appsettings.Development.json`
- Create: `src/SuperScanner.Worker/OcrServiceCollectionExtensions.cs`
- Create: `tests/SuperScanner.Application.Tests/Ocr/WorkerOcrRegistrationTests.cs`

**Interfaces:**
- Consumes: `OcrOptions`, `GoogleDocumentAiOptions`, `IDocumentAiClient`, `GoogleDocumentAiOcrProvider`.
- Produces: exact provider registrations for Disabled, Fake, and GoogleDocumentAi with no silent fallback.

- [ ] **Step 1: Write failing composition tests**

Build a Worker service collection through an extracted `AddOcrServices(IServiceCollection, IConfiguration, IHostEnvironment)` method. Assert Disabled resolves `DisabledOcrProvider`, E2E Fake resolves `FakeOcrProvider`, Development Google resolves `GoogleDocumentAiOcrProvider`, malformed Google config fails validation, unknown provider fails startup, and Production Fake remains rejected.

- [ ] **Step 2: Verify RED**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter WorkerOcrRegistrationTests`

Expected: compile failure because the registration method does not exist.

- [ ] **Step 3: Implement explicit registration**

Replace the binary Fake/Disabled branch with an exact switch. Bind `OcrOptions` once, register the Google client and provider only for `GoogleDocumentAi`, and throw an options-validation failure for any enabled unknown provider. Keep checked-in defaults disabled. Add non-secret Development examples for the project, location, processor, endpoint, and maximum bytes while leaving `Enabled=false` and `Provider=Disabled`.

- [ ] **Step 4: Run composition and startup tests**

Run both focused suites:

```powershell
dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter WorkerOcrRegistrationTests
dotnet test tests/SuperScanner.Api.IntegrationTests/SuperScanner.Api.IntegrationTests.csproj --filter OcrEndpointsTests
```

Expected: PASS without ADC or network access.

- [ ] **Step 5: Commit**

```powershell
git add src/SuperScanner.Worker/Program.cs src/SuperScanner.Worker/OcrServiceCollectionExtensions.cs src/SuperScanner.Worker/appsettings.json src/SuperScanner.Worker/appsettings.Development.json tests/SuperScanner.Application.Tests/Ocr/WorkerOcrRegistrationTests.cs
git commit -m "feat: register Google OCR provider"
```

### Task 8: Add opt-in live acceptance and local operating instructions

**Files:**
- Create: `tests/SuperScanner.Application.Tests/Ocr/GoogleDocumentAiLiveTests.cs`
- Modify: `docs/operations/ocr-foundation.md`
- Modify: `.gitignore` if it does not already reject Google credential JSON patterns

**Interfaces:**
- Consumes: ADC, the configured Google processor, `GoogleDocumentAiOcrProvider`.
- Produces: explicitly gated live test and reproducible local setup commands.

- [ ] **Step 1: Write the skipped-by-default live test**

The test must skip unless `GOOGLE_DOCUMENT_AI_LIVE_TEST=1`. It reads only these non-secret variables, with the approved values as defaults: project, location, processor, endpoint. Generate the PNG in memory during the test from fixed, non-identifying English text (including one line rendered with a generic cursive test font); do not commit a customer image or binary fixture. Assert non-empty full text, at least one word, valid polygons, finite confidence, and provider name, and write only counts/latency/confidence to test output.

- [ ] **Step 2: Verify the ordinary run is skipped and free**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter GoogleDocumentAiLiveTests`

Expected: test skipped; no Google request.

- [ ] **Step 3: Document local ADC and enablement**

Add exact commands:

```powershell
gcloud auth application-default login
gcloud config set project superscanner-dev
$env:Ocr__Enabled = 'true'
$env:Ocr__Provider = 'GoogleDocumentAi'
$env:Ocr__Google__ProjectId = 'superscanner-dev'
$env:Ocr__Google__Location = 'asia-southeast1'
$env:Ocr__Google__ProcessorId = 'fc0b14e64c62e7aa'
$env:Ocr__Google__Endpoint = 'asia-southeast1-documentai.googleapis.com'
```

Document that the API requires only `Ocr__Enabled=true`, while the Worker requires the provider and Google values. Include the IAM role `roles/documentai.apiUser`, safe disable commands, the billable live-test command, and the Railway/WIF promotion checklist. Explicitly forbid copying ADC or service-account JSON into the repository or `.task-tools`.

- [ ] **Step 4: Run the live test only after ADC is configured**

Run:

```powershell
$env:GOOGLE_DOCUMENT_AI_LIVE_TEST='1'
dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter GoogleDocumentAiLiveTests
```

Expected: PASS against `asia-southeast1`; output contains counts/latency/confidence but no recognized text. If ADC or IAM is unavailable, stop and report `ocr_auth_failed`; do not create a service-account key as a workaround.

- [ ] **Step 5: Commit**

```powershell
git add tests/SuperScanner.Application.Tests/Ocr/GoogleDocumentAiLiveTests.cs docs/operations/ocr-foundation.md .gitignore
git commit -m "test: verify live Google OCR locally"
```

### Task 9: Full verification, security audit, and local user handoff

**Files:**
- Modify only if verification reveals a Phase 3B defect in files already owned by Tasks 1–8.

**Interfaces:**
- Consumes: complete Google OCR adapter and local runbook.
- Produces: verified branch ready for user acceptance; no Railway enablement.

- [ ] **Step 1: Run all .NET suites**

Run:

```powershell
dotnet test tests/SuperScanner.Domain.Tests/SuperScanner.Domain.Tests.csproj
dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj
dotnet test tests/SuperScanner.Infrastructure.IntegrationTests/SuperScanner.Infrastructure.IntegrationTests.csproj
dotnet test tests/SuperScanner.Api.IntegrationTests/SuperScanner.Api.IntegrationTests.csproj
```

Expected: PASS with the live test skipped unless explicitly enabled.

- [ ] **Step 2: Run frontend regression and production build**

Run:

```powershell
npm --prefix apps/web test -- --run
npm --prefix apps/web run build
```

Expected: all Angular tests PASS and production build succeeds; existing documented budget warnings may remain but no new errors are allowed.

- [ ] **Step 3: Audit source and logs for credential/content leakage**

Run:

```powershell
rg -n "private_key|refresh_token|client_secret|BEGIN PRIVATE KEY|GOOGLE_APPLICATION_CREDENTIALS" . -g '!**/bin/**' -g '!**/obj/**' -g '!**/node_modules/**' -g '!docs/superpowers/plans/**'
rg -n "FullText|RawDocument|Status\.Detail|exception\.Message" src/SuperScanner.Infrastructure/Ocr src/SuperScanner.Worker
git diff --check
```

Expected: no committed credential material; any configuration-name occurrence is documentation/code only; no provider content or raw exception detail is logged; diff check is clean.

- [ ] **Step 4: Run local acceptance through the UI**

Start API with OCR admission enabled and Worker with the Google provider settings. Upload a synthetic or user-approved English page, complete automatic/manual crop until Ready, choose **Recognize text**, and verify `Queued → Processing → Ready`. Confirm the UI reports word count/confidence/completion without exposing credentials or raw provider errors. Disable OCR and confirm scan and image-PDF flows remain usable.

- [ ] **Step 5: Request independent code review**

Use `superpowers:requesting-code-review` for the entire Phase 3B diff. Resolve only technically validated findings, rerun the owning tests, then repeat Steps 1–3.

- [ ] **Step 6: Record the verified revision**

Run: `git status --short` and `git log -1 --oneline`.

Expected: the Phase 3B implementation files are committed, unrelated pre-existing working-tree changes remain unstaged, and no empty verification commit is created. If Step 5 required a correction, commit that correction within its owning task using that task's explicit file list before repeating verification.

## Completion Checklist

- [ ] Tasks 1–9 each passed their focused red-green cycle and review gate.
- [ ] Official SDK remains pinned to `Google.Cloud.DocumentAI.V1` 3.25.0.
- [ ] Ordinary tests make zero Google requests.
- [ ] Opt-in live test passes using ADC and the Singapore processor.
- [ ] Printed and explicitly marked handwritten English map into the existing normalized contract.
- [ ] Transient failures retry within existing bounds; authentication and invalid responses fail safely.
- [ ] No credential or recognized-content leakage is present.
- [ ] Local UI reaches OCR Ready on an approved test page.
- [ ] Railway production remains disabled pending the separate Workload Identity Federation promotion gate.
