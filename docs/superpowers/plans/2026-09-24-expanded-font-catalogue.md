# Expanded Font Catalogue Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expand the printed-text editor from two to twenty licensed font families with deterministic server rendering and lazy browser previews.

**Architecture:** The existing pinned manifest remains authoritative, gains category and face metadata, and is exposed through a safe read-only catalogue endpoint. The estimator and renderer continue using exact catalogue/version IDs; Angular loads only recommended, selected, or previewed web fonts and provides search, grouping, recent choices, and valid weight selection.

**Tech Stack:** .NET 10, EF Core 10, ImageMagick, Angular, TypeScript, CSS Font Loading API, xUnit

**Spec:** `docs/superpowers/specs/2026-09-24-searchable-pdf-and-font-catalogue-design.md`

## Global Constraints

- The first expanded release contains exactly twenty enabled families.
- Every face must have a redistributable licence, bundled notice, stable version, and verified SHA-256 hash.
- Existing Noto catalogue/version identifiers must remain valid.
- The client never supplies filenames, paths, or CSS expressions; it submits catalogue and version IDs.
- Browser assets load on demand, not at application startup.
- Disabled versions remain reproducible for prior edits but unavailable for new edits.
- Handwriting-style fonts do not enable unique-person handwriting imitation or protected-document editing.

## Review Focus

- A family provides Regular but no Bold: the UI must not offer Bold and the server must reject a forged request.
- A font file hash or licence notice is wrong: startup/build validation must fail before editing is enabled.
- A web font fails to load: the editor must retain the selected ID, show a safe fallback warning, and let the server stay authoritative.
- Two faces share a catalogue/version key: manifest validation must reject the catalogue deterministically.
- Existing stored Noto edits are reopened after expansion: the exact old face must still render.

---

### Task 1: Extend the Catalogue Contract and Validation

**Files:**
- Modify: `src/ArksScanner.Domain/TextEditing/FontCatalogueEntry.cs`
- Modify: `src/ArksScanner.Infrastructure/TextEditing/BundledFontCatalogue.cs`
- Modify: `assets/fonts/manifest.json`
- Test: `tests/ArksScanner.Application.Tests/TextEditing/BundledFontCatalogueTests.cs`

**Interfaces:**
- Produces: `FontCategory` enum (`SansSerif`, `Serif`, `Monospace`, `Handwriting`)
- Produces: face metadata `Style`, `Weight`, `Category`, `WebFamilyName`, and `SelectableForNewEdits`
- Consumes: existing stable catalogue ID, version, hash, licence, web path, renderer path

- [ ] **Step 1: Write failing manifest tests** for unique keys, allowed categories, real weight/style metadata, safe relative paths, stable Noto IDs, duplicate rejection, hash mismatch, and missing/invalid licence notice. The exact twenty-family inventory assertion belongs to Task 2, when those assets are introduced.
- [ ] **Step 2: Run** `dotnet test tests/ArksScanner.Application.Tests/ArksScanner.Application.Tests.csproj --filter FullyQualifiedName~BundledFontCatalogueTests`; expect failures for missing metadata and family count.
- [ ] **Step 3: Extend the domain and manifest parser** with bounded enums/weights and separate `Enabled` from reproducible-but-not-selectable state.
- [ ] **Step 4: Update current Noto manifest entries** without changing their IDs or versions.
- [ ] **Step 5: Run focused tests**; expect every Task 1 contract test to pass before committing.
- [ ] **Step 6: Commit** with `git add src/ArksScanner.Domain/TextEditing/FontCatalogueEntry.cs src/ArksScanner.Infrastructure/TextEditing/BundledFontCatalogue.cs assets/fonts/manifest.json tests/ArksScanner.Application.Tests/TextEditing/BundledFontCatalogueTests.cs && git commit -m "feat: extend font catalogue metadata"`.

### Task 2: Add and Verify the Twenty Licensed Families

**Files:**
- Add/Modify: `assets/fonts/manifest.json`
- Add: `assets/fonts/<pinned-face-files>.ttf`
- Add: `assets/fonts/licenses/<family>-OFL.txt`
- Add: `apps/web/src/assets/fonts/<pinned-face-files>.ttf`
- Test: `tests/ArksScanner.Application.Tests/TextEditing/BundledFontCatalogueTests.cs`

**Interfaces:**
- Produces: the exact twenty families listed in the spec and their supported faces
- Consumes: validation contract from Task 1

- [ ] **Step 1: Add a failing inventory assertion** listing all twenty expected stable family IDs rather than checking only a count.
- [ ] **Step 2: Acquire only official upstream release assets** for Noto Sans, Liberation Sans, Carlito, Roboto, Open Sans, Lato, Montserrat, Source Sans 3, Poppins, Oswald, Noto Serif, Liberation Serif, Caladea, Source Serif 4, Merriweather, Libre Baskerville, Noto Sans Mono, Liberation Mono, Caveat, and Dancing Script.
- [ ] **Step 3: Record licence notices, versions, weights, styles, and SHA-256 hashes** in the manifest. Do not add a face whose redistribution terms or upstream version cannot be proven.
- [ ] **Step 4: Copy identical pinned web assets** and verify web/server hashes match for each face.
- [ ] **Step 5: Run catalogue tests and a script that recalculates every hash**; expect exactly twenty unique enabled families and no unreferenced font binaries.
- [ ] **Step 6: Run a clean API and Worker build** to prove assets are included in publish output.
- [ ] **Step 7: Commit** with `git add assets/fonts apps/web/src/assets/fonts tests/ArksScanner.Application.Tests/TextEditing/BundledFontCatalogueTests.cs && git commit -m "feat: add expanded licensed font catalogue"`.

### Task 3: Expose a Safe Read-Only Font Catalogue API

**Files:**
- Create: `src/ArksScanner.Application/TextEditing/GetFontCatalogue.cs`
- Modify: `src/ArksScanner.Api/Endpoints/TextEditingEndpoints.cs`
- Modify: `src/ArksScanner.Api/Program.cs`
- Test: `tests/ArksScanner.Api.IntegrationTests/Documents/TextEditingEndpointsTests.cs`

**Interfaces:**
- Produces: `GET /api/text-edit-fonts`
- Produces: `FontCatalogueDto(CatalogueId, Version, DisplayName, FamilyName, Category, Weight, Style, WebAssetUrl, Enabled)`
- Consumes: `IFontCatalogue.Entries`

- [ ] **Step 1: Write failing endpoint tests** for authenticated access, cache headers, safe metadata, twenty families, supported weights, disabled-face omission for new edits, and absence of renderer paths/hash filesystem details.
- [ ] **Step 2: Run the focused API tests**; expect 404.
- [ ] **Step 3: Implement the query and endpoint** with a deterministic category/name/weight order and versioned private cache semantics appropriate to the current auth boundary.
- [ ] **Step 4: Run focused tests**; expect pass.
- [ ] **Step 5: Commit** with `git add src/ArksScanner.Application/TextEditing/GetFontCatalogue.cs src/ArksScanner.Api/Endpoints/TextEditingEndpoints.cs src/ArksScanner.Api/Program.cs tests/ArksScanner.Api.IntegrationTests/Documents/TextEditingEndpointsTests.cs && git commit -m "feat: expose approved font catalogue"`.

### Task 4: Rank the Expanded Catalogue Without Weakening Validation

**Files:**
- Modify: `src/ArksScanner.Infrastructure/TextEditing/TextStyleEstimator.cs`
- Modify: `src/ArksScanner.Infrastructure/TextEditing/TextEditRenderer.cs`
- Modify: `src/ArksScanner.Infrastructure/TextEditing/TextEditPreparation.cs`
- Test: `tests/ArksScanner.Application.Tests/TextEditing/TextStyleEstimatorTests.cs`
- Test: `tests/ArksScanner.Application.Tests/TextEditing/TextEditRendererTests.cs`
- Test: `tests/ArksScanner.Application.Tests/TextEditing/TextEditPreparationTests.cs`

**Interfaces:**
- Produces: ranked candidates with real face weight/style and top-three recommendations
- Consumes: expanded catalogue metadata and exact pinned font paths

- [ ] **Step 1: Write failing tests** for ranking across categories, deterministic tie-breaking, top-three diversity, Regular/Bold pairing, missing-weight rejection, disabled-face exclusion, and existing Noto edit reproducibility.
- [ ] **Step 2: Run focused text-editing tests**; expect candidate/weight failures.
- [ ] **Step 3: Update estimator ranking** to compare only selectable faces, return real metadata, and preserve deterministic ordering without loading fonts outside the pinned root.
- [ ] **Step 4: Keep renderer validation exact**: catalogue ID, version, requested weight, file hash, and safe path must agree before rendering.
- [ ] **Step 5: Run focused tests and existing text-edit regression tests**; expect pass.
- [ ] **Step 6: Commit** with `git add src/ArksScanner.Infrastructure/TextEditing/TextStyleEstimator.cs src/ArksScanner.Infrastructure/TextEditing/TextEditRenderer.cs src/ArksScanner.Infrastructure/TextEditing/TextEditPreparation.cs tests/ArksScanner.Application.Tests/TextEditing && git commit -m "feat: rank expanded text fonts"`.

### Task 5: Build the Lazy Font Picker

**Files:**
- Create: `apps/web/src/app/documents/font-catalogue.models.ts`
- Create: `apps/web/src/app/documents/font-catalogue.service.ts`
- Create: `apps/web/src/app/documents/font-picker.component.ts`
- Create: `apps/web/src/app/documents/font-picker.component.html`
- Create: `apps/web/src/app/documents/font-picker.component.scss`
- Modify: `apps/web/src/app/documents/text-replacement-editor.component.ts`
- Modify: `apps/web/src/app/documents/text-replacement-editor.component.html`
- Modify: `apps/web/src/assets/fonts/text-fonts.css`
- Test: `apps/web/src/app/documents/font-catalogue.service.spec.ts`
- Test: `apps/web/src/app/documents/font-picker.component.spec.ts`
- Test: `apps/web/src/app/documents/text-replacement-editor.component.spec.ts`

**Interfaces:**
- Produces: `FontCatalogueService.list()` and `loadFace(face)` using `FontFace`/`document.fonts`
- Produces: `FontPickerComponent` input `faces`, `recommended`, `selected`; output `faceChange`
- Consumes: safe catalogue endpoint from Task 3

- [ ] **Step 1: Write failing service tests** for one catalogue request, cached results, URL allowlisting, one-time lazy face load, load failure, and no eager twenty-family download.
- [ ] **Step 2: Write failing component tests** for search, four category groups, top-three recommendations, recent selections, keyboard navigation, visible focus, accessible labels, valid weights only, and fallback warning.
- [ ] **Step 3: Run focused Angular tests**; expect missing service/component failures.
- [ ] **Step 4: Implement the service** so only recommended, selected, or explicitly previewed faces are loaded and a failed web preview never changes the stable server identifiers.
- [ ] **Step 5: Implement the picker** with readable family names, virtual/simple bounded list rendering, local recent IDs, and no document text in storage or analytics.
- [ ] **Step 6: Replace the hard-coded Noto `<select>`** in the editor and make weight options derive from available faces.
- [ ] **Step 7: Run focused Angular tests**; expect pass.
- [ ] **Step 8: Commit** with `git add apps/web/src/app/documents/font-* apps/web/src/app/documents/text-replacement-editor.component.* apps/web/src/assets/fonts/text-fonts.css && git commit -m "feat: add lazy twenty-family font picker"`.

### Task 6: End-to-End Font Verification and Operations

**Files:**
- Create: `tests/e2e/font-catalogue.spec.ts` or extend the current printed-text Playwright suite
- Modify: `docs/operations/printed-text-editing.md`
- Modify: `src/ArksScanner.Api/appsettings.json`
- Modify: `src/ArksScanner.Worker/appsettings.json`

**Interfaces:**
- Produces: documented catalogue upgrade, disablement, hash failure, rollback, and old-edit recovery procedure

- [ ] **Step 1: Add E2E coverage** that opens the editor, searches a non-Noto family, previews it, selects supported Regular/Bold faces, applies an edit, reloads, and verifies deterministic persistence.
- [ ] **Step 2: Add a browser-network assertion** showing startup does not fetch all font binaries and selection fetches only required faces.
- [ ] **Step 3: Add an old-Noto-edit regression** proving expansion does not alter previously rendered output.
- [ ] **Step 4: Document licence inventory, manifest upgrades, startup validation, disabling a compromised face, rollback, and retained-version recovery.**
- [ ] **Step 5: Run all text-editing .NET tests, Angular tests, clean production builds, and the opt-in E2E**; record exact pass/skip counts.
- [ ] **Step 6: Inspect `git diff` for unlicensed/unreferenced binaries and commit** with `git add tests/e2e docs/operations/printed-text-editing.md src/ArksScanner.Api/appsettings.json src/ArksScanner.Worker/appsettings.json && git commit -m "test: verify expanded font catalogue"`.

