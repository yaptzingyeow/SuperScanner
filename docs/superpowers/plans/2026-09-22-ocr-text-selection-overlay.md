# OCR Text Selection Overlay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users drag across multiple OCR words on a Ready scanned page, see an editor-only SVG highlight, and inspect the selected phrase without changing or exporting the scan.

**Architecture:** Add pure geometry and reading-order utilities, then a standalone Angular SVG overlay component that owns ephemeral selection state. Feed the existing `PageOcr` response from `OcrStatusComponent` through `DocumentDetailComponent` into `PageCardComponent`; no API, database, image-processing, or export contract changes.

**Tech Stack:** Angular 22 standalone components, TypeScript, SVG, Angular signals, Vitest, existing `PageOcr` models and document organizer.

**Spec:** `docs/superpowers/specs/2026-09-22-ocr-text-selection-overlay-design.md`

## Global Constraints

- Multi-word drag selection is required in the first Phase 3C release.
- Only OCR `Word` elements are interactive; block and line elements remain structural.
- Selection uses OCR hierarchy and reading order, never pointer direction, to form the phrase.
- Selection state remains in browser memory and is never sent to an API, database, object storage, image pipeline, export pipeline, analytics, metrics, or logs.
- The processed page image remains unchanged and the SVG highlight never appears in an exported PDF.
- OCR coordinates remain normalized in `0..1`; invalid word polygons are skipped, not repaired.
- `Escape`, empty-space click, page identity change, OCR result change, and component destruction clear selection.
- Existing scan, crop, filter, page management, OCR polling, and PDF export behavior must remain intact.
- Do not implement text replacement, font matching, replacement boxes, reflow, undo/redo, or searchable PDF generation.

## File Structure

- `apps/web/src/app/documents/ocr-selection.ts` — pure polygon validation, intersection, hierarchy flattening, reading order, and summary calculation.
- `apps/web/src/app/documents/ocr-selection.spec.ts` — deterministic utility coverage without Angular rendering.
- `apps/web/src/app/documents/ocr-text-overlay.component.ts` — pointer and keyboard state machine with browser-memory-only selection.
- `apps/web/src/app/documents/ocr-text-overlay.component.html` — normalized SVG polygons and local selection summary.
- `apps/web/src/app/documents/ocr-text-overlay.component.scss` — invisible hit targets and selected-only visual treatment.
- `apps/web/src/app/documents/ocr-text-overlay.component.spec.ts` — interaction, accessibility, scaling, invalid-input, and privacy tests.
- `apps/web/src/app/documents/ocr-status.component.ts` — emits each fetched or requested OCR snapshot to the page container.
- `apps/web/src/app/documents/ocr-status.component.spec.ts` — pins emission without changing polling behavior.
- `apps/web/src/app/documents/document-detail.component.ts` — owns per-page OCR snapshots and clears removed pages.
- `apps/web/src/app/documents/document-detail.component.html` — connects status output to each page card.
- `apps/web/src/app/documents/document-detail.component.spec.ts` — verifies page-local routing and removal cleanup.
- `apps/web/src/app/documents/page-card.component.ts` — accepts optional `PageOcr` and imports the overlay.
- `apps/web/src/app/documents/page-card.component.html` — layers overlay over the clean preview.
- `apps/web/src/app/documents/page-card.component.scss` — shared image/overlay positioning.
- `apps/web/src/app/documents/page-card.component.spec.ts` — verifies Ready-only rendering and export isolation.

## Review Focus

1. A drag that starts bottom-right and ends top-left must select the same words and phrase order as the forward drag; Task 2 pins reverse drag behavior.
2. A rotated word whose bounding box touches the drag region but whose polygon does not must remain unselected; Task 1 pins exact polygon intersection after the coarse check.
3. An OCR refresh that reuses a page ID but changes `resultId` must clear the old selection; Task 2 pins result-identity reset.
4. Pointer cancellation or lost pointer capture must not retain an active half-drawn selection; Task 2 pins both terminal events.
5. Removing a page must remove its cached OCR result, preventing content from a removed page from remaining in browser memory; Task 4 pins cleanup.

---

### Task 1: Build deterministic OCR selection geometry

**Files:**
- Create: `apps/web/src/app/documents/ocr-selection.ts`
- Create: `apps/web/src/app/documents/ocr-selection.spec.ts`

**Interfaces:**
- Consumes: `OcrElement`, `OcrPoint`, and `PageOcr` from `document.models.ts`.
- Produces: `SelectableOcrWord`, `OcrSelectionSummary`, `flattenSelectableWords(elements)`, `selectWordsInRegion(words, region)`, and `summarizeSelection(words)`.

- [ ] **Step 1: Write failing utility tests**

Create tests with explicit blocks, lines, and words that assert:

```ts
const words = flattenSelectableWords([block]);
expect(words.map((word) => word.text)).toEqual(['Yap', 'Tzing', 'Yeow']);
expect(selectWordsInRegion(words, { x1: .1, y1: .1, x2: .8, y2: .3 })
  .map((word) => word.text)).toEqual(['Yap', 'Tzing', 'Yeow']);
expect(selectWordsInRegion(words, { x1: .8, y1: .3, x2: .1, y2: .1 })
  .map((word) => word.text)).toEqual(['Yap', 'Tzing', 'Yeow']);
```

Also cover exact rotated-polygon intersection, a bounding-box-only false positive, duplicate reading-order tie-breaking by ID, invalid polygon isolation, confidence averaging, and `Printed`/`Handwritten`/`Mixed`/`Unknown` summaries.

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
$env:Path = 'C:\Users\tzingyeow.yap\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin;' + $env:Path
& 'C:\Users\tzingyeow.yap\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe' 'C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js' --prefix apps/web test -- ocr-selection.spec.ts
```

Expected: FAIL because `ocr-selection.ts` and its exports do not exist.

- [ ] **Step 3: Implement the pure selection contract**

Define:

```ts
export interface SelectionRegion { x1: number; y1: number; x2: number; y2: number; }
export interface SelectableOcrWord extends OcrElement {
  blockOrder: number;
  lineOrder: number;
}
export interface OcrSelectionSummary {
  phrase: string;
  wordCount: number;
  averageConfidence: number;
  textType: 'Printed' | 'Handwritten' | 'Mixed' | 'Unknown';
}
export function flattenSelectableWords(elements: OcrElement[]): SelectableOcrWord[];
export function selectWordsInRegion(
  words: SelectableOcrWord[], region: SelectionRegion,
): SelectableOcrWord[];
export function summarizeSelection(words: SelectableOcrWord[]): OcrSelectionSummary | null;
```

Validate exactly four finite points in `0..1`. Normalize the drag region using min/max. Use a bounding-box pre-check followed by segment intersection, polygon-point-in-rectangle, and rectangle-corner-in-polygon checks. Sort by block order, line order, word order, then element ID. Join display text with one space and never log inputs.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Task 1 command again.

Expected: all `ocr-selection.spec.ts` tests PASS.

- [ ] **Step 5: Commit**

```powershell
git add apps/web/src/app/documents/ocr-selection.ts apps/web/src/app/documents/ocr-selection.spec.ts
git commit -m "feat: calculate OCR word selections"
```

### Task 2: Render the SVG overlay and pointer selection

**Files:**
- Create: `apps/web/src/app/documents/ocr-text-overlay.component.ts`
- Create: `apps/web/src/app/documents/ocr-text-overlay.component.html`
- Create: `apps/web/src/app/documents/ocr-text-overlay.component.scss`
- Create: `apps/web/src/app/documents/ocr-text-overlay.component.spec.ts`

**Interfaces:**
- Consumes: `PageOcr`, `flattenSelectableWords`, `selectWordsInRegion`, and `summarizeSelection`.
- Produces: standalone `OcrTextOverlayComponent` with required `pageId`, required `ocr`, normalized SVG rendering, and no output that reaches persistence or export.

- [ ] **Step 1: Write failing pointer and rendering tests**

Configure the component with a Ready OCR result containing `Yap`, `Tzing`, and `Yeow`. Dispatch pointer events whose `clientX/clientY` values are converted through a mocked `getBoundingClientRect()`. Assert:

```ts
expect(selectedPolygons()).toHaveLength(3);
expect(summary.textContent).toContain('Yap Tzing Yeow');
expect(summary.textContent).toContain('3 words');
```

Cover forward, reverse, vertical, and diagonal drags; click-threshold word selection; empty click clearing; `Escape`; pointer cancel; lost capture; invalid word isolation; no-valid-word empty state; responsive bounding-rectangle conversion; and clearing when `pageId` or `ocr.resultId` changes.

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
$env:Path = 'C:\Users\tzingyeow.yap\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin;' + $env:Path
& 'C:\Users\tzingyeow.yap\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe' 'C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js' --prefix apps/web test -- ocr-text-overlay.component.spec.ts
```

Expected: FAIL because `OcrTextOverlayComponent` does not exist.

- [ ] **Step 3: Implement the pointer state machine and SVG**

Use Angular signal inputs and local signals:

```ts
readonly pageId = input.required<string>();
readonly ocr = input.required<PageOcr>();
protected readonly words = computed(() => flattenSelectableWords(this.ocr().elements));
protected readonly selectedIds = signal<ReadonlySet<string>>(new Set());
protected readonly summary = computed(() => summarizeSelection(this.selectedWords()));
```

Render a normalized SVG:

```html
<svg viewBox="0 0 1 1" preserveAspectRatio="none"
     (pointerdown)="beginSelection($event)"
     (pointermove)="updateSelection($event)"
     (pointerup)="finishSelection($event)"
     (pointercancel)="cancelSelection($event)"
     (lostpointercapture)="cancelSelection($event)"
     (keydown)="handleKeydown($event)">
  @for (word of words(); track word.id) {
    <polygon [attr.points]="polygonPoints(word)"
             [class.selected]="selectedIds().has(word.id)" />
  }
</svg>
```

Convert client coordinates to normalized coordinates from the SVG bounding rectangle. Keep drag state private and ephemeral. Stop propagation for overlay pointer events so page reordering cannot start. Clear on input identity changes and destruction.

- [ ] **Step 4: Style selected-only feedback**

Normal polygons use transparent fill and stroke while retaining pointer hit testing. Selected polygons use a translucent blue fill and blue outline. Add an editor-only selection panel with an `aria-live="polite"` summary. Do not add a screenshot, canvas, rasterization, or download path.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run the Task 2 command again.

Expected: all pointer, rendering, lifecycle, and responsive-coordinate tests PASS.

- [ ] **Step 6: Commit**

```powershell
git add apps/web/src/app/documents/ocr-text-overlay.component.ts apps/web/src/app/documents/ocr-text-overlay.component.html apps/web/src/app/documents/ocr-text-overlay.component.scss apps/web/src/app/documents/ocr-text-overlay.component.spec.ts
git commit -m "feat: add OCR SVG selection overlay"
```

### Task 3: Add keyboard selection and accessible state

**Files:**
- Modify: `apps/web/src/app/documents/ocr-text-overlay.component.ts`
- Modify: `apps/web/src/app/documents/ocr-text-overlay.component.html`
- Modify: `apps/web/src/app/documents/ocr-text-overlay.component.scss`
- Modify: `apps/web/src/app/documents/ocr-text-overlay.component.spec.ts`

**Interfaces:**
- Consumes: Task 2's ordered `words`, `selectedIds`, and `summary` signals.
- Produces: roving word focus, arrow navigation, `Shift` range extension, `Enter` selection, `Escape` clearing, and selected-state semantics.

- [ ] **Step 1: Write failing keyboard tests**

Assert that:

```ts
overlay.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
overlay.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', shiftKey: true }));
expect(componentSelection()).toEqual(['word-1', 'word-2']);
overlay.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
expect(componentSelection()).toEqual([]);
```

Cover left/right movement, first/last boundary behavior, `Enter`, `Shift` extension and contraction, selected-state attributes, focus preservation, and the live-region summary.

- [ ] **Step 2: Run focused tests and verify RED**

Run the Task 2 focused command.

Expected: keyboard tests FAIL because the handlers and accessibility state are incomplete.

- [ ] **Step 3: Implement roving focus and range selection**

Keep `focusedIndex` and `anchorIndex` local. Arrow keys move through the Task 1 reading-order array. Without `Shift`, movement changes focus only; with `Shift`, select the inclusive range between anchor and focus. `Enter` selects only the focused word. `Escape` clears selection and anchor. Prevent default only for handled keys.

Add local DOM labels and `aria-selected` state without calling any service. The component root must explain the controls through `aria-describedby`.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Task 2 focused command again.

Expected: all overlay tests PASS.

- [ ] **Step 5: Commit**

```powershell
git add apps/web/src/app/documents/ocr-text-overlay.component.ts apps/web/src/app/documents/ocr-text-overlay.component.html apps/web/src/app/documents/ocr-text-overlay.component.scss apps/web/src/app/documents/ocr-text-overlay.component.spec.ts
git commit -m "feat: make OCR selection keyboard accessible"
```

### Task 4: Connect OCR results to the page preview

**Files:**
- Modify: `apps/web/src/app/documents/ocr-status.component.ts`
- Modify: `apps/web/src/app/documents/ocr-status.component.spec.ts`
- Modify: `apps/web/src/app/documents/document-detail.component.ts`
- Modify: `apps/web/src/app/documents/document-detail.component.html`
- Modify: `apps/web/src/app/documents/document-detail.component.spec.ts`
- Modify: `apps/web/src/app/documents/page-card.component.ts`
- Modify: `apps/web/src/app/documents/page-card.component.html`
- Modify: `apps/web/src/app/documents/page-card.component.scss`
- Modify: `apps/web/src/app/documents/page-card.component.spec.ts`

**Interfaces:**
- Consumes: existing `PageOcr` polling snapshots and Task 2's `OcrTextOverlayComponent`.
- Produces: `OcrStatusComponent.statusChange: EventEmitter<PageOcr>`, `DocumentDetailComponent.ocrByPage`, and `PageCardComponent.ocr?: PageOcr`.

- [ ] **Step 1: Write failing data-flow and privacy tests**

Pin these behaviors:

- `OcrStatusComponent` emits every successful load and request result, but emits nothing for failed requests.
- `DocumentDetailComponent` stores a snapshot only under the originating page ID.
- Reloading a document removes cached OCR for page IDs no longer present.
- `PageCardComponent` renders the overlay only when it has a preview and `ocr.state === 'Ready'`.
- Invoking `downloadOriginal`, moving/removing a page, and requesting export receives no OCR selection state or selected phrase.
- No new HTTP POST/PUT/PATCH request occurs when overlay selection changes.

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
$env:Path = 'C:\Users\tzingyeow.yap\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin;' + $env:Path
& 'C:\Users\tzingyeow.yap\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe' 'C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js' --prefix apps/web test -- ocr-status.component.spec.ts document-detail.component.spec.ts page-card.component.spec.ts
```

Expected: FAIL because the status output, per-page cache, and overlay input do not exist.

- [ ] **Step 3: Emit successful OCR snapshots**

Add:

```ts
@Output() readonly statusChange = new EventEmitter<PageOcr>();
```

After each successful `status.set(next)`, call `statusChange.emit(next)`. Do not emit inside catch blocks and do not emit raw errors.

- [ ] **Step 4: Route snapshots by page and clear removed-page content**

Add an `ocrByPage` signal to `DocumentDetailComponent` and a method:

```ts
protected ocrUpdated(pageId: string, ocr: PageOcr): void {
  this.ocrByPage.update((current) => ({ ...current, [pageId]: ocr }));
}
```

During document reload, filter both preview URLs and OCR snapshots to active page IDs. Bind `(statusChange)="ocrUpdated(page.id, $event)"` and `[ocr]="ocrByPage()[page.id]"`.

- [ ] **Step 5: Layer the overlay over the clean image**

Import `OcrTextOverlayComponent` into `PageCardComponent`, add `@Input() ocr?: PageOcr`, and render it inside the existing `.thumbnail` only when a preview exists and OCR is Ready. Make the thumbnail a positioned container and the overlay absolute with the same inset and dimensions as the image.

Do not change download or export events. The overlay receives no service capable of persistence or export.

- [ ] **Step 6: Run focused tests and verify GREEN**

Run the Task 4 command again.

Expected: all status, detail, and page-card tests PASS.

- [ ] **Step 7: Commit**

```powershell
git add apps/web/src/app/documents/ocr-status.component.ts apps/web/src/app/documents/ocr-status.component.spec.ts apps/web/src/app/documents/document-detail.component.ts apps/web/src/app/documents/document-detail.component.html apps/web/src/app/documents/document-detail.component.spec.ts apps/web/src/app/documents/page-card.component.ts apps/web/src/app/documents/page-card.component.html apps/web/src/app/documents/page-card.component.scss apps/web/src/app/documents/page-card.component.spec.ts
git commit -m "feat: show OCR selection on page previews"
```

### Task 5: Full regression, privacy audit, and visual acceptance

**Files:**
- Modify only Phase 3C files if verification exposes a Phase 3C defect.

**Interfaces:**
- Consumes: complete Phase 3C overlay and existing document/export UI.
- Produces: verified Phase 3C revision ready for user acceptance.

- [ ] **Step 1: Run the complete Angular suite**

Run:

```powershell
$env:Path = 'C:\Users\tzingyeow.yap\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin;' + $env:Path
& 'C:\Users\tzingyeow.yap\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe' 'C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js' --prefix apps/web test
```

Expected: all Angular tests PASS.

- [ ] **Step 2: Run the production build**

Run:

```powershell
$env:Path = 'C:\Users\tzingyeow.yap\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin;' + $env:Path
& 'C:\Users\tzingyeow.yap\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe' 'C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js' --prefix apps/web run build
```

Expected: production build succeeds; pre-existing bundle-budget warnings may remain, but no new error or warning is introduced by Phase 3C.

- [ ] **Step 3: Audit the Phase 3C source boundary**

Run:

```powershell
rg -n "HttpClient|DocumentsApiService|console\.|localStorage|sessionStorage|analytics|export|download" apps/web/src/app/documents/ocr-selection.ts apps/web/src/app/documents/ocr-text-overlay.component.*
git diff --check
```

Expected: the selection utility and overlay contain no API, persistence, telemetry, export, download, or console dependency; diff check is clean apart from preserved unrelated user-file line-ending notices.

- [ ] **Step 4: Perform local visual acceptance**

Start the existing API and Angular environment. Open a Ready page with OCR results and verify:

- a single drag selects several words;
- reverse dragging produces the same phrase order;
- resizing the viewport preserves alignment;
- `Escape` clears the highlight;
- removing or changing the page clears selection;
- exporting the document produces a PDF without any blue highlight.

Use only a user-approved or synthetic page. Do not capture OCR text in logs or screenshots committed to the repository.

- [ ] **Step 5: Request independent whole-feature review**

Use `superpowers:requesting-code-review` against the Phase 3C spec and the complete Phase 3C commit range. Resolve validated Critical or Important findings with a reproducing test, rerun Steps 1–3, and ledger deferred Minor findings.

- [ ] **Step 6: Record the verified revision**

Run:

```powershell
git status --short
git log -1 --oneline
```

Expected: Phase 3C files are committed; unrelated pre-existing user changes remain unstaged; no empty verification commit exists.

## Completion Checklist

- [ ] Multi-word pointer selection works in every drag direction.
- [ ] Selection phrase follows OCR hierarchy and reading order.
- [ ] Rotated polygons use exact intersection rather than bounding-box-only selection.
- [ ] Keyboard navigation, range extension, selection, and clearing work.
- [ ] Responsive image/SVG alignment remains correct.
- [ ] Invalid OCR geometry fails locally without breaking the page or export.
- [ ] Page/result changes and removed pages clear browser-memory selection data.
- [ ] No selection state reaches an API, storage, telemetry, image processing, or export.
- [ ] Exported PDFs remain clean and contain no selection highlight.
- [ ] Angular tests and production build pass.
- [ ] Independent review has no unresolved Critical or Important finding.
