import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { CropPoint, CropState, DocumentDetail, DocumentImport, DocumentPage, ScanFilterId } from './document.models';
import { DocumentsApiService } from './documents-api.service';
import { AddPagesDialogComponent } from './add-pages-dialog.component';
import { RETIRED_LOOK_HELP, SCAN_LOOKS, lookForFilter } from './scan-looks';

export type PageChip = 'Ready' | 'Finding edges…' | 'Applying look…' | 'Check corners' | 'Could not import' | 'Look will apply when ready';
type Outcome = 'applied' | 'unsupported' | 'text-edits' | 'failed';
type CropChange = (state: CropState) => { points?: CropPoint[]; filter?: ScanFilterId; rotation?: number };

interface SkippedPage {
  pageId: string;
  pageNumber: number;
  reason: 'unsupported' | 'text-edits';
}

/** A change chosen while the page was still finding its edges; applied once it is ready. */
interface DeferredChange {
  filter?: ScanFilterId;
  rotationDelta: number;
  /** Reset to the untouched original: Original look, full-image corners, rotation 0. */
  original?: boolean;
}

type PageResult = { page: DocumentPage; outcome: Outcome };

interface CheckState {
  check: string[];
  decided: string[];
}

const POLL_MS = 1500;
const RETRY_DELAYS_MS = [1500, 3000, 6000, 10000];
const BUSY_PAGE_STATES = ['Importing', 'Processing'];
const BUSY_CROP_STATES = ['Detecting', 'Processing'];
const PENDING_IMPORT_STATES = ['AwaitingUpload', 'PendingValidation', 'Accepted'];
const FAILED_IMPORT_STATES = ['Rejected', 'Failed'];
const ZOOM_MIN = 25;
const ZOOM_MAX = 400;
const ZOOM_STEP = 25;
const fullImage = (): CropPoint[] => [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 1, y: 1 }, { x: 0, y: 1 }];

/** Paper / ink swatches for the look tiles (illustrative only). */
const SWATCHES: Record<string, { paper: string; ink: string }> = {
  Magic: { paper: '#FFFFFF', ink: '#151815' },
  Original: { paper: '#D3CCBE', ink: '#4A463F' },
  Document: { paper: '#F4F2EC', ink: '#24262A' },
  Bright: { paper: '#FBF7EC', ink: '#55524B' },
  RemoveShadows: { paper: '#EEEDE8', ink: '#2C2E2B' },
  CleanDocument: { paper: '#FFFFFF', ink: '#000000' },
  Grayscale: { paper: '#E6E6E6', ink: '#3A3A3A' },
  BlackAndWhite: { paper: '#FFFFFF', ink: '#000000' },
  ContentClean: { paper: '#FFFFFF', ink: '#1A1A1A' },
};

/** Mirrors CropEndpoints.GetGuidance; prefers the server's own `guidance` field when present. */
export function cropGuidance(state: CropState): 'accurate' | 'verify' | 'manual' {
  const server = (state as CropState & { guidance?: string | null }).guidance;
  if (server === 'accurate' || server === 'verify' || server === 'manual') return server;
  if (state.source === 'FullImage' || state.confidence === 0) return 'manual';
  if (state.source === 'Ai' && state.confidence !== null && state.confidence >= .78) return 'accurate';
  return 'verify';
}

@Component({
  selector: 'app-import-review',
  standalone: true,
  imports: [AddPagesDialogComponent],
  templateUrl: './import-review.component.html',
  styleUrl: './import-review.component.scss',
})
export class ImportReviewComponent implements OnInit, OnDestroy {
  private readonly api = inject(DocumentsApiService);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly base = inject(API_BASE_URL).replace(/\/+$/, '');

  protected readonly documentId = this.route.snapshot.paramMap.get('documentId')!;
  private readonly uploadsParam = this.route.snapshot.queryParamMap?.get('uploads') ?? null;
  private readonly checkedParam = this.route.snapshot.queryParamMap?.get('checked') ?? null;
  /** Uploads shown on this screen (null = every page of the document); grows when pages are added here. */
  private readonly uploadIds = signal<ReadonlySet<string> | null>(this.uploadsParam
    ? new Set(this.uploadsParam.split(',').map((id) => id.trim()).filter(Boolean))
    : null);
  protected readonly addingPages = signal(false);
  private readonly storageKey = `arksscanner:import-check:${this.documentId}`;

  protected readonly looks = SCAN_LOOKS;
  protected readonly swatches = SWATCHES;
  protected readonly zoomMin = ZOOM_MIN;
  protected readonly zoomMax = ZOOM_MAX;

  protected readonly detail = signal<DocumentDetail | null>(null);
  protected readonly loadError = signal('');
  protected readonly crops = signal<Record<string, CropState>>({});
  protected readonly pendingLooks = signal<Record<string, ScanFilterId>>({});
  protected readonly deferred = signal<Record<string, DeferredChange>>({});
  /** Pages whose corners the user should check; decided once per page from the server guidance. */
  protected readonly needsCheck = signal<ReadonlySet<string>>(new Set());
  private readonly decided = new Set<string>();
  protected readonly previews = signal<Record<string, string>>({});
  /** Pages whose newest preview is still downloading. */
  private readonly previewLoading = signal<ReadonlySet<string>>(new Set());
  protected readonly selectedId = signal<string | null>(null);
  protected readonly applyAll = signal(true);
  protected readonly zoom = signal(100);
  protected readonly compare = signal(false);
  protected readonly originalUrl = signal('');
  protected readonly originalLoading = signal(false);
  protected readonly skipped = signal<SkippedPage[]>([]);
  protected readonly error = signal('');
  protected readonly working = signal(false);
  /** True while Confirm / Upload originals wait for changes deferred on busy pages. */
  protected readonly applying = signal(false);

  protected readonly pages = computed(() => {
    const detail = this.detail();
    if (!detail) return [];
    return detail.pages
      .filter((page) => this.isShownUpload(page.sourceUploadId))
      .sort((a, b) => a.position - b.position);
  });
  private readonly shownImports = computed(() =>
    (this.detail()?.imports ?? []).filter((item) => this.isShownUpload(item.uploadId)));
  protected readonly failedImports = computed(() =>
    this.shownImports().filter((item) => FAILED_IMPORT_STATES.includes(item.state)));
  protected readonly selected = computed(() => {
    const pages = this.pages();
    return pages.find((page) => page.id === this.selectedId()) ?? pages[0] ?? null;
  });
  protected readonly selectedFilter = computed(() => {
    const page = this.selected();
    return page ? this.filterOf(page) : 'Magic';
  });
  protected readonly selectedLook = computed(() => lookForFilter(this.selectedFilter()));
  protected readonly lookHelp = computed(() => this.selectedLook()?.help ?? RETIRED_LOOK_HELP);
  protected readonly canRotate = computed(() => {
    const page = this.selected();
    return !!page && (page.canCrop || this.isBusy(page));
  });
  protected readonly canCompare = computed(() => {
    const page = this.selected();
    if (!page?.hasOriginal) return false;
    const source = this.detail()?.imports.find((item) => item.uploadId === page.sourceUploadId);
    return !!source?.mediaType.startsWith('image/');
  });
  protected readonly checkCount = computed(() =>
    this.pages().filter((page) => this.chip(page) === 'Check corners').length);
  protected readonly preparing = computed(() =>
    this.pages().some((page) => this.isBusy(page)) || this.shownImports().some((item) => PENDING_IMPORT_STATES.includes(item.state)));
  protected readonly summary = computed(() => {
    const count = this.pages().length;
    if (!this.detail()) return 'Opening your pages…';
    if (count === 0) return this.preparing() ? 'Preparing your pages…' : 'No pages to review.';
    const parts = [`${count} ${count === 1 ? 'page' : 'pages'}`];
    if (this.preparing()) parts.push(this.pages().every((page) => !this.isBusy(page) || this.isRestyling(page)) &&
      !this.shownImports().some((item) => PENDING_IMPORT_STATES.includes(item.state)) ? 'applying looks…' : 'finding edges…');
    const checks = this.checkCount();
    if (checks > 0) parts.push(`${checks} ${checks === 1 ? 'page needs' : 'pages need'} a quick check`);
    return parts.join(' · ');
  });
  protected readonly currentImage = computed(() => {
    const page = this.selected();
    if (!page) return '';
    return this.compare() ? this.originalUrl() : this.previews()[page.id] ?? '';
  });

  /** What the selected page is waiting for, or '' when its preview is current. */
  protected readonly processingMessage = computed(() => {
    const page = this.selected();
    return page ? this.processing(page) : '';
  });

  private timer?: ReturnType<typeof setTimeout>;
  private failures = 0;
  private destroyed = false;
  private readonly queues = new Map<string, Promise<void>>();
  private readonly cropRequests = new Set<string>();
  private readonly previewKeys = new Map<string, string>();
  private originalFor = '';
  private originalRequest = 0;
  /** Deferred changes started by applyDeferred, with their outcomes, until a finish action collects them. */
  private deferredRuns: Promise<PageResult>[] = [];
  private deferredDrained?: () => void;

  ngOnInit(): void {
    this.restoreChecks();
    if (this.checkedParam) {
      this.markChecked(this.checkedParam);
      void this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { checked: null },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    }
    void this.refresh();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.deferredDrained?.();
    this.deferredDrained = undefined;
    clearTimeout(this.timer);
    this.timer = undefined;
    for (const url of Object.values(this.previews())) URL.revokeObjectURL(url);
    if (this.originalUrl()) URL.revokeObjectURL(this.originalUrl());
  }

  protected chip(page: DocumentPage): PageChip {
    if (page.state === 'Failed') return 'Could not import';
    if (this.isBusy(page)) {
      if (this.deferred()[page.id]) return 'Look will apply when ready';
      return this.isRestyling(page) ? 'Applying look…' : 'Finding edges…';
    }
    if (this.needsCheck().has(page.id)) return 'Check corners';
    return 'Ready';
  }

  /** Why a page's preview is not final yet ('' when it is), shown with a spinner. */
  protected processing(page: DocumentPage): string {
    if (page.state === 'Failed') return '';
    if (page.cropStatus === 'Detecting') return 'Finding the page edges…';
    if (page.state === 'Importing') return 'Preparing your page…';
    const sent = this.crops()[page.id];
    // the server accepted a change the page list has not caught up with yet
    const accepted = !!sent && BUSY_CROP_STATES.includes(sent.status) && sent.revision > page.appliedCropRevision;
    if (this.pendingLooks()[page.id] || accepted || this.isBusy(page) || this.previewLoading().has(page.id)) {
      return `Applying ${lookForFilter(this.filterOf(page))?.label ?? 'your changes'}…`;
    }
    return '';
  }

  protected filterOf(page: DocumentPage): ScanFilterId {
    return this.pendingLooks()[page.id] ?? this.deferred()[page.id]?.filter ?? this.crops()[page.id]?.filter
      ?? (page.filter as ScanFilterId | null | undefined) ?? 'Magic';
  }

  protected isLookSelected(id: ScanFilterId): boolean {
    return this.selectedFilter() === id;
  }

  private isShownUpload(uploadId: string): boolean {
    const shown = this.uploadIds();
    return !shown || shown.has(uploadId);
  }

  /** New files added from this screen join it, so their pages are reviewed here too. */
  protected async pagesAdded(uploadIds: string[]): Promise<void> {
    this.addingPages.set(false);
    const shown = this.uploadIds();
    if (shown) {
      const next = new Set([...shown, ...uploadIds]);
      this.uploadIds.set(next);
      await this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { uploads: [...next].join(',') },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    }
    await this.refresh();
  }

  protected selectPage(page: DocumentPage): void {
    this.selectedId.set(page.id);
    this.compare.set(false);
  }

  protected changeZoom(direction: 1 | -1): void {
    this.zoom.update((value) => Math.max(ZOOM_MIN, Math.min(ZOOM_MAX, value + direction * ZOOM_STEP)));
  }

  protected toggleApplyAll(event: Event): void {
    this.applyAll.set((event.target as HTMLInputElement).checked);
  }

  protected async toggleCompare(): Promise<void> {
    const page = this.selected();
    if (!page || this.working()) return;
    if (this.compare()) { this.compare.set(false); return; }
    this.compare.set(true);
    if (this.originalFor === page.id && this.originalUrl()) return;
    const request = ++this.originalRequest;
    this.originalLoading.set(true);
    try {
      const blob = await firstValueFrom(this.http.get(
        `${this.base}/documents/${this.documentId}/pages/${page.id}/original`, { responseType: 'blob' }));
      if (this.destroyed || request !== this.originalRequest) return;
      if (this.originalUrl()) URL.revokeObjectURL(this.originalUrl());
      this.originalUrl.set(URL.createObjectURL(blob));
      this.originalFor = page.id;
    } catch {
      if (!this.destroyed && request === this.originalRequest) {
        this.compare.set(false);
        this.error.set('Could not load the original photo. Please try again.');
      }
    } finally {
      if (!this.destroyed && request === this.originalRequest) this.originalLoading.set(false);
    }
  }

  protected async chooseLook(filter: ScanFilterId): Promise<void> {
    const current = this.selected();
    if (!current || this.working()) return;
    const targets = this.applyAll() ? this.pages() : [current];
    if (targets.every((page) => this.filterOf(page) === filter)) return;
    this.startAction();
    const busy = targets.filter((page) => this.isBusy(page));
    const ready = targets.filter((page) => !this.isBusy(page));
    for (const page of busy) this.defer(page.id, { filter });
    this.pendingLooks.update((looks) => {
      const next = { ...looks };
      for (const page of ready) if (page.canCrop) next[page.id] = filter;
      return next;
    });
    const results = await Promise.all(ready.map(async (page) =>
      ({ page, outcome: await this.updatePage(page, () => ({ filter })) })));
    if (this.destroyed) return;
    this.pendingLooks.update((looks) => {
      const next = { ...looks };
      for (const { page } of results) if (next[page.id] === filter) delete next[page.id];
      return next;
    });
    this.report(results);
  }

  protected async rotate(delta: 90 | -90): Promise<void> {
    const page = this.selected();
    if (!page || this.working()) return;
    if (this.isBusy(page)) {
      this.defer(page.id, { rotationDelta: delta });
      return;
    }
    if (!page.canCrop) return;
    this.startAction();
    const outcome = await this.updatePage(page, (state) => ({ rotation: turn(state.rotation, delta) }));
    if (!this.destroyed) this.report([{ page, outcome }]);
  }

  protected async adjustCorners(): Promise<void> {
    const page = this.selected();
    if (!page?.canCrop) return;
    await this.router.navigate(['/documents', this.documentId, 'pages', page.id, 'crop'], {
      queryParams: { returnTo: 'import', corners: '1', ...(this.uploadIds() ? { uploads: [...this.uploadIds()!].join(',') } : {}) },
    });
  }

  protected async uploadOriginals(): Promise<void> {
    if (this.working()) return;
    this.startAction();
    this.working.set(true);
    try {
      const pages = this.pages();
      // Pages still importing or finding edges get the reset once they are ready.
      for (const page of pages.filter((item) => this.isBusy(item))) this.defer(page.id, { original: true });
      const results = await Promise.all(pages.filter((page) => !this.isBusy(page)).map(async (page) => ({
        page,
        outcome: await this.updatePage(page, () => ({ filter: 'Original', points: fullImage(), rotation: 0 })),
      })));
      if (this.destroyed) return;
      this.report(results);
      const deferred = await this.settleDeferred();
      if (this.destroyed) return;
      if ([...results, ...deferred].some((result) => result.outcome !== 'applied')) return;
      await this.router.navigate(['/documents', this.documentId]);
    } finally {
      if (!this.destroyed) {
        this.working.set(false);
        this.applying.set(false);
      }
    }
  }

  protected async confirm(): Promise<void> {
    if (this.working()) return;
    this.working.set(true);
    try {
      const deferred = await this.settleDeferred();
      await Promise.all([...this.queues.values()]);
      if (this.destroyed) return;
      if (deferred.some((result) => result.outcome !== 'applied')) return;
      await this.router.navigate(['/documents', this.documentId]);
    } finally {
      if (!this.destroyed) {
        this.working.set(false);
        this.applying.set(false);
      }
    }
  }

  protected async close(event: Event): Promise<void> {
    event.preventDefault();
    if (this.hasDeferred()
      && !window.confirm('Some changes are waiting for pages that are still being prepared. Leave without applying them?')) return;
    await this.router.navigate(['/documents', this.documentId]);
  }

  private hasDeferred(): boolean {
    return Object.keys(this.deferred()).length > 0;
  }

  /**
   * Waits (polling continues) until every change deferred on a busy page has been sent and has finished,
   * then returns their outcomes; each one was already reported as it finished.
   */
  private async settleDeferred(): Promise<PageResult[]> {
    if (this.hasDeferred()) {
      this.applying.set(true);
      await new Promise<void>((resolve) => {
        this.deferredDrained = resolve;
        this.schedulePoll();
      });
    }
    const runs = this.deferredRuns;
    this.deferredRuns = [];
    return Promise.all(runs);
  }

  protected retryLoad(): void {
    this.loadError.set('');
    void this.refresh();
  }

  private async refresh(): Promise<void> {
    clearTimeout(this.timer);
    this.timer = undefined;
    try {
      const detail = await this.api.getDocument(this.documentId);
      if (this.destroyed) return;
      this.failures = 0;
      this.detail.set(detail);
      this.loadError.set('');
      this.syncPreviews();
      this.applyDeferred();
      this.syncCrops();
      if (!this.hasDeferred() && this.deferredDrained) {
        const drained = this.deferredDrained;
        this.deferredDrained = undefined;
        drained();
      }
      if (this.preparing() || this.hasDeferred()) this.schedulePoll();
    } catch {
      if (this.destroyed) return;
      this.loadError.set('Could not load your pages. Retrying…');
      this.schedulePoll(RETRY_DELAYS_MS[Math.min(this.failures++, RETRY_DELAYS_MS.length - 1)]);
    }
  }

  private schedulePoll(delay = POLL_MS): void {
    if (this.destroyed || this.timer !== undefined) return;
    this.timer = setTimeout(() => {
      this.timer = undefined;
      void this.refresh();
    }, delay);
  }

  /** Re-rendering a page that already had a result (a look, rotation or crop change), not its first preparation. */
  private isRestyling(page: DocumentPage): boolean {
    return page.cropStatus === 'Processing' && page.appliedCropRevision > 0;
  }

  private isBusy(page: DocumentPage): boolean {
    return BUSY_PAGE_STATES.includes(page.state) || BUSY_CROP_STATES.includes(page.cropStatus);
  }

  private defer(pageId: string, change: Partial<DeferredChange>): void {
    this.deferred.update((all) => {
      const current = all[pageId] ?? { rotationDelta: 0 };
      const next: DeferredChange = change.original
        ? { original: true, rotationDelta: 0 }
        : {
          ...(current.original ? { original: true } : {}),
          filter: change.filter ?? current.filter,
          rotationDelta: (current.rotationDelta + (change.rotationDelta ?? 0)) % 360,
        };
      const updated = { ...all };
      if (isNoOp(next)) delete updated[pageId];
      else updated[pageId] = next;
      return updated;
    });
  }

  /** Applies looks/rotations chosen while a page was busy, now that it is ready, from its fresh crop state. */
  private applyDeferred(): void {
    const all = this.deferred();
    const pages = this.pages();
    const shown = new Set(pages.map((page) => page.id));
    const ready = pages.filter((page) => all[page.id] && !this.isBusy(page));
    // Pages no longer shown (removed from the document) drop their deferred change.
    const gone = Object.keys(all).filter((id) => !shown.has(id));
    if (ready.length === 0 && gone.length === 0) return;
    this.deferred.update((current) => {
      const next = { ...current };
      for (const page of ready) delete next[page.id];
      for (const id of gone) delete next[id];
      return next;
    });
    for (const page of ready) {
      const change = all[page.id];
      if (isNoOp(change)) continue;
      const run = this.updatePage(page, (state) => change.original
        ? { filter: 'Original', points: fullImage(), rotation: 0 }
        : {
          ...(change.filter ? { filter: change.filter } : {}),
          ...(change.rotationDelta ? { rotation: turn(state.rotation, change.rotationDelta) } : {}),
        }, true).then((outcome) => {
        if (!this.destroyed) this.report([{ page, outcome }]);
        return { page, outcome };
      });
      this.deferredRuns.push(run);
    }
  }

  private syncCrops(): void {
    for (const page of this.pages()) {
      if (!page.canCrop || this.isBusy(page) || this.queues.has(page.id) || this.cropRequests.has(page.id)) continue;
      const cached = this.crops()[page.id];
      if (cached && cached.revision >= page.cropRevision && this.decided.has(page.id)) continue;
      this.cropRequests.add(page.id);
      this.api.getCrop(this.documentId, page.id)
        .then((state) => {
          if (this.destroyed) return;
          this.decide(page.id, state);
          if (!this.queues.has(page.id)) this.storeCrop(page.id, state);
        })
        .catch(() => undefined)
        .finally(() => this.cropRequests.delete(page.id));
    }
  }

  private syncPreviews(): void {
    for (const page of this.pages()) {
      if (!page.hasPreview) continue;
      const key = `${page.appliedCropRevision}:${page.previewRevision}`;
      if (this.previewKeys.get(page.id) === key) continue;
      this.previewKeys.set(page.id, key);
      this.setPreviewLoading(page.id, true);
      this.api.getPagePreview(this.documentId, page.id)
        .then((blob) => {
          const url = URL.createObjectURL(blob);
          if (this.destroyed || this.previewKeys.get(page.id) !== key) { URL.revokeObjectURL(url); return; }
          const old = this.previews()[page.id];
          if (old) URL.revokeObjectURL(old);
          this.previews.update((previews) => ({ ...previews, [page.id]: url }));
        })
        .catch(() => {
          if (this.previewKeys.get(page.id) === key) this.previewKeys.delete(page.id);
        })
        .finally(() => {
          // a newer preview request for this page owns the flag now
          if (this.previewKeys.has(page.id) && this.previewKeys.get(page.id) !== key) return;
          this.setPreviewLoading(page.id, false);
        });
    }
  }

  private setPreviewLoading(pageId: string, loading: boolean): void {
    this.previewLoading.update((ids) => {
      if (ids.has(pageId) === loading) return ids;
      const next = new Set(ids);
      if (loading) next.add(pageId); else next.delete(pageId);
      return next;
    });
  }

  private storeCrop(pageId: string, state: CropState): void {
    this.crops.update((crops) => ({ ...crops, [pageId]: state }));
  }

  /** Decides once per page, from the server's guidance after detection, whether its corners need a check. */
  private decide(pageId: string, state: CropState): void {
    if (this.decided.has(pageId) || BUSY_CROP_STATES.includes(state.status)) return;
    this.decided.add(pageId);
    if (cropGuidance(state) !== 'accurate') this.needsCheck.update((set) => new Set(set).add(pageId));
    this.saveChecks();
  }

  private markChecked(pageId: string): void {
    this.decided.add(pageId);
    this.needsCheck.update((set) => {
      const next = new Set(set);
      next.delete(pageId);
      return next;
    });
    this.saveChecks();
  }

  private restoreChecks(): void {
    try {
      const raw = sessionStorage.getItem(this.storageKey);
      if (!raw) return;
      const saved = JSON.parse(raw) as Partial<CheckState>;
      for (const id of saved.decided ?? []) if (typeof id === 'string') this.decided.add(id);
      this.needsCheck.set(new Set((saved.check ?? []).filter((id): id is string => typeof id === 'string')));
    } catch { /* storage unavailable or corrupt: decide again from the server */ }
  }

  private saveChecks(): void {
    try {
      const state: CheckState = { check: [...this.needsCheck()], decided: [...this.decided] };
      sessionStorage.setItem(this.storageKey, JSON.stringify(state));
    } catch { /* storage unavailable: keep the in-memory set */ }
  }

  private startAction(): void {
    this.skipped.set([]);
    this.error.set('');
  }

  /** Runs one crop update per page at a time; later requests wait for earlier ones. */
  private updatePage(page: DocumentPage, change: CropChange, fresh = false): Promise<Outcome> {
    if (!page.canCrop) return Promise.resolve('unsupported');
    let outcome: Outcome = 'failed';
    const previous = this.queues.get(page.id) ?? Promise.resolve();
    const next = previous.then(async () => { outcome = await this.applyWithRetry(page.id, change, fresh); });
    this.queues.set(page.id, next);
    return next.then(() => {
      if (this.queues.get(page.id) === next) this.queues.delete(page.id);
      return outcome;
    });
  }

  private async applyWithRetry(pageId: string, change: CropChange, fresh: boolean): Promise<Outcome> {
    for (let attempt = 0; attempt < 2; attempt++) {
      try {
        let state = this.crops()[pageId];
        if (!state || fresh || attempt > 0) {
          state = await this.api.getCrop(this.documentId, pageId);
          if (this.destroyed) return 'failed';
          this.decide(pageId, state);
          this.storeCrop(pageId, state);
        }
        const update = change(state);
        const result = await this.api.applyCrop(this.documentId, pageId, {
          revision: state.revision,
          points: update.points ?? state.points ?? fullImage(),
          filter: update.filter ?? state.filter,
          rotation: update.rotation ?? state.rotation ?? 0,
        });
        if (this.destroyed) return 'applied';
        this.storeCrop(pageId, result);
        this.schedulePoll();
        return 'applied';
      } catch (error) {
        if (!(error instanceof HttpErrorResponse)) return 'failed';
        if (error.status === 404) return 'unsupported';
        if (error.status !== 409) return 'failed';
        const message = String((error.error as { message?: unknown } | null)?.message ?? '');
        if (/text edit/i.test(message)) return 'text-edits';
      }
    }
    return 'failed';
  }

  private report(results: { page: DocumentPage; outcome: Outcome }[]): void {
    const skipped: SkippedPage[] = [];
    for (const { page, outcome } of results) {
      if (outcome === 'unsupported' || outcome === 'text-edits')
        skipped.push({ pageId: page.id, pageNumber: page.pageNumber || page.position, reason: outcome });
    }
    this.skipped.update((current) => [...current, ...skipped]);
    if (results.some((result) => result.outcome === 'failed'))
      this.error.set('Some pages could not be updated. Please try again.');
  }

  protected failedImportNames(items: DocumentImport[]): string {
    return items.map((item) => item.fileName).filter(Boolean).join(', ');
  }
}

function isNoOp(change: DeferredChange): boolean {
  return !change.original && !change.filter && change.rotationDelta % 360 === 0;
}

function turn(rotation: number | null | undefined, delta: number): number {
  return (((rotation ?? 0) + delta) % 360 + 360) % 360;
}
