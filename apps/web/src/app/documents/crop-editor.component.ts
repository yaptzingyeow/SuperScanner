import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/api/security.interceptor';

import { CropPoint as Point, CropState, ScanFilterId as ScanFilter } from './document.models';
import { RETIRED_LOOK_HELP } from './scan-looks';

export type { CropState } from './document.models';
export type CropGuidance = 'accurate' | 'verify' | 'manual';
const fullImage = (): Point[] => [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 1, y: 1 }, { x: 0, y: 1 }];

@Component({
  selector: 'app-crop-editor', standalone: true, imports: [RouterLink],
  templateUrl: './crop-editor.component.html', styleUrl: './crop-editor.component.scss'
})
export class CropEditorComponent implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly documentId = this.route.snapshot.paramMap.get('documentId')!;
  private readonly pageId = this.route.snapshot.paramMap.get('pageId')!;
  private readonly returnUploads = this.route.snapshot.queryParamMap?.get('uploads') ?? null;
  /** Where Done and the close link go: the import review when opened from it, otherwise the workspace. */
  protected readonly returnsToImport = this.route.snapshot.queryParamMap?.get('returnTo') === 'import';
  protected readonly returnLink: string[] = this.returnsToImport
    ? ['/documents', this.documentId, 'import'] : ['/documents', this.documentId];
  /** Back on the import review, `checked` clears this page's "Check corners" flag. */
  protected readonly returnQuery: Record<string, string> = this.returnsToImport
    ? { ...(this.returnUploads ? { uploads: this.returnUploads } : {}), checked: this.pageId }
    : { tab: 'edit', page: this.pageId };
  protected readonly workspaceQuery = { tab: 'edit', page: this.pageId };
  private readonly pageUrl = `${inject(API_BASE_URL).replace(/\/+$/, '')}/documents/${this.documentId}/pages/${this.pageId}`;
  protected readonly names = ['Top left', 'Top right', 'Bottom right', 'Bottom left'];
  protected readonly filters: { id: ScanFilter; label: string; description: string }[] = [
    { id: 'Magic', label: 'Magic scan', description: 'Default for photos: flatten the page to its true proportions, turn paper white and ink crisp, and clear desk edges and punch holes. Colourful cards keep their colours.' },
    { id: 'Original', label: 'Original', description: 'Keep the photo’s colors without enhancement.' },
    { id: 'Document', label: 'Document', description: 'Improve contrast and gently sharpen text.' },
    { id: 'Bright', label: 'Bright', description: 'Lighten a dark photo while keeping its colors.' },
    { id: 'RemoveShadows', label: 'Faithful scan', description: 'Reduce uneven lighting while retaining ink colors and faint strokes. No OCR rewriting or automatic erasing of marks.' },
    { id: 'Grayscale', label: 'Grayscale', description: 'Remove color and retain shades of gray.' },
    { id: 'BlackAndWhite', label: 'Black & White', description: 'Create crisp black text on white. Faint handwriting may disappear.' },
  ];
  protected readonly filter = signal<ScanFilter>('Document');
  protected readonly actualResultFilter = computed(() => true);
  /** Opened from "Adjust corners": start with the corner handles showing. */
  protected readonly cropMode = signal(this.route.snapshot.queryParamMap?.get('corners') === '1');
  protected readonly zoom = signal(1);
  protected readonly pendingChanges = signal(false);
  // Smart clean and Clean content are no longer offered; pages saved with them keep rendering.
  protected readonly filterDescription = computed(() =>
    this.filters.find(item => item.id === this.filter())?.description ?? RETIRED_LOOK_HELP);
  protected readonly filterPreview = computed(() => ({
    Magic: 'none', Original: 'none', Document: 'contrast(1.08) saturate(.9)',
    Bright: 'brightness(1.2) contrast(1.05)', Grayscale: 'grayscale(1)',
    BlackAndWhite: 'grayscale(1) contrast(2.8)', RemoveShadows: 'none',
    CleanDocument: 'none', CleanDocumentGentle: 'none', CleanDocumentStrong: 'none', ContentClean: 'none',
  })[this.filter()]);
  private filterDirty = false;
  private autoSavePending = false;
  private filterTimer?: ReturnType<typeof setTimeout>;

  protected selectFilter(value: ScanFilter): void {
    this.clearResult(true);
    this.filter.set(value);
    this.filterDirty = true;
    this.pendingChanges.set(true);
    this.autoSavePending = true;
    this.cropMode.set(false);
    this.queueSelectedFilter();
  }
  private queueSelectedFilter(): void {
    clearTimeout(this.filterTimer);
    if (!this.autoSavePending || this.busy() || this.destroyed) return;
    this.filterTimer = setTimeout(() => { this.autoSavePending = false; void this.submit(false); }, 250);
  }
  protected changeZoom(delta: number): void { this.zoom.update(value => Math.max(.5, Math.min(3, value + delta))); }
  protected async finish(): Promise<void> {
    if (this.busy() || this.autoSavePending || this.filterDirty || this.dirty) return;
    await this.router.navigate(this.returnLink, { queryParams: this.returnQuery });
  }
  protected readonly points = signal<Point[]>(fullImage());
  protected readonly state = signal<CropState | null>(null);
  readonly guidance = signal<CropGuidance>('verify');
  protected readonly sourceUrl = signal('');
  protected readonly resultUrl = signal('');
  protected readonly resultLoading = signal(false);
  protected readonly resultError = signal('');
  protected readonly showOriginal = signal(false);
  private resultRequest = 0;
  private resultRevision: number | null = null;
  protected readonly error = signal('');
  protected readonly submitting = signal(false);
  protected readonly busy = computed(() => this.submitting() || ['Detecting', 'Processing'].includes(this.state()?.status ?? ''));
  protected readonly polygon = computed(() => this.points().map(p => `${p.x * 1000},${p.y * 1000}`).join(' '));
  protected readonly valid = computed(() => this.validGeometry(this.points()));
  private timer?: ReturnType<typeof setTimeout>;
  private destroyed = false;
  private activeCorner: number | null = null;
  private activePointer: number | null = null;
  private dirty = false;
  private applying = false;

  ngOnInit(): void { void this.initialize(); }
  private async initialize(): Promise<void> {
    try {
      const blob = await firstValueFrom(this.http.get(`${this.pageUrl}/crop-source`, { responseType: 'blob' }));
      if (this.destroyed) return;
      this.sourceUrl.set(URL.createObjectURL(blob));
      await this.reload();
    } catch { this.error.set('The crop editor is unavailable. Only accepted JPG and PNG photos can be adjusted.'); }
  }
  protected async reload(): Promise<void> {
    clearTimeout(this.timer);
    try {
      const state = await firstValueFrom(this.http.get<CropState>(`${this.pageUrl}/crop`));
      if (this.destroyed) return;
      this.applyCropState(state);
      if (!this.filterDirty) this.filter.set(state.filter ?? 'Document');
      if (!this.dirty) this.points.set(state.points?.map(p => ({ ...p })) ?? fullImage());
      if (state.status === 'Failed') this.error.set('The crop could not be processed. Adjust the corners and apply again, or retry Auto detect.');
      if (state.status === 'Ready' && this.applying) { this.applying = false; this.cropMode.set(false); }
      if (state.status === 'Ready' && state.appliedFilter === this.filter() &&
          this.actualResultFilter() && !this.filterDirty && !this.dirty &&
          state.appliedRevision > 0 && this.resultRevision !== state.appliedRevision && !this.resultLoading())
        void this.loadResult(state.appliedRevision);
      if (state.status === 'Detecting' || state.status === 'Processing')
        this.timer = setTimeout(() => void this.reload(), 1500);
      else this.queueSelectedFilter();
    } catch { if (!this.destroyed) this.error.set('Could not read the crop status. Reload and try again.'); }
  }
  protected reset(): void { if (this.busy()) return; this.clearResult(); this.points.set(fullImage()); this.dirty = true; this.pendingChanges.set(true); this.error.set(''); }
  /** Current page rotation in degrees clockwise (0, 90, 180 or 270). */
  protected readonly rotation = computed(() => this.state()?.rotation ?? 0);
  protected rotate(delta: 90 | -90): void {
    void this.submit(false, (((this.rotation() + delta) % 360) + 360) % 360);
  }
  protected async submit(detect: boolean, rotation?: number): Promise<void> {
    const state = this.state();
    if (!state || this.busy() || (!detect && !this.valid())) return;
    clearTimeout(this.filterTimer);
    this.autoSavePending = false;
    this.clearResult(true); this.error.set(''); this.submitting.set(true);
    const submittedFilter = this.filter();
    try {
      const updated = await firstValueFrom(this.http.post<CropState>(`${this.pageUrl}/crop/${detect ? 'detect' : 'apply'}`,
        { revision: state.revision, points: detect ? null : this.points(), filter: submittedFilter,
          ...(rotation === undefined ? {} : { rotation }) }));
      if (this.destroyed) return;
      this.applyCropState(updated); this.dirty = false; this.filterDirty = this.filter() !== submittedFilter; this.applying = !detect;
      this.pendingChanges.set(this.filterDirty);
      await this.reload();
    } catch (error) {
      this.autoSavePending = false;
      this.error.set(error instanceof HttpErrorResponse && error.status === 409
        ? 'This crop was changed in another session. Reload before applying your changes.'
        : 'Could not save this crop. Check the corners and try again.');
    } finally { this.submitting.set(false); this.queueSelectedFilter(); }
  }
  protected begin(event: PointerEvent, index: number, stage: HTMLElement): void {
    if (this.busy() || this.activePointer !== null) return;
    event.preventDefault(); this.activeCorner = index; this.activePointer = event.pointerId;
    stage.setPointerCapture(event.pointerId);
  }
  protected move(event: PointerEvent, stage: HTMLElement): void {
    if (this.activeCorner === null || event.pointerId !== this.activePointer) return;
    const box = stage.getBoundingClientRect();
    this.update(this.activeCorner, (event.clientX - box.left) / box.width, (event.clientY - box.top) / box.height);
  }
  protected end(event: PointerEvent, stage: HTMLElement): void {
    if (event.pointerId !== this.activePointer) return;
    if (stage.hasPointerCapture(event.pointerId)) stage.releasePointerCapture(event.pointerId);
    this.activeCorner = this.activePointer = null;
  }
  protected key(event: KeyboardEvent, index: number): void {
    if (this.busy()) return;
    const delta = event.shiftKey ? 0.015 : 0.003;
    const offsets: Record<string, Point> = { ArrowLeft: { x: -delta, y: 0 }, ArrowRight: { x: delta, y: 0 }, ArrowUp: { x: 0, y: -delta }, ArrowDown: { x: 0, y: delta } };
    const offset = offsets[event.key]; if (!offset) return;
    event.preventDefault(); const point = this.points()[index]; this.update(index, point.x + offset.x, point.y + offset.y);
  }
  protected coordinate(event: Event, index: number, axis: 'x' | 'y'): void {
    const value = (event.target as HTMLInputElement).valueAsNumber / 100;
    if (this.busy() || !Number.isFinite(value)) return;
    const p = this.points()[index]; this.update(index, axis === 'x' ? value : p.x, axis === 'y' ? value : p.y);
  }
  protected percent(value: number): number { return Math.round(value * 1000) / 10; }
  applyCropState(state: CropState): void {
    this.state.set(state);
    if (state.source === 'FullImage' || state.confidence === 0) this.guidance.set('manual');
    else if (state.source === 'Ai' && state.confidence !== null && state.confidence >= .78
      && state.diagnosticsCode === 'ai_high_confidence') this.guidance.set('accurate');
    else this.guidance.set('verify');
  }
  private update(index: number, x: number, y: number): void {
    this.clearResult();
    this.points.update(points => points.map((p, i) => i === index ? { x: Math.max(0, Math.min(1, x)), y: Math.max(0, Math.min(1, y)) } : p));
    this.dirty = true;
    this.pendingChanges.set(true);
  }
  private validGeometry(p: Point[]): boolean {
    let area = 0;
    for (let i = 0; i < 4; i++) {
      const a = p[i], b = p[(i + 1) % 4], c = p[(i + 2) % 4];
      if ((b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x) <= 0.0001 || (b.x - a.x) ** 2 + (b.y - a.y) ** 2 < 0.0004) return false;
      area += a.x * b.y - b.x * a.y;
    }
    return area >= 0.02 && p[0].y + p[1].y < p[2].y + p[3].y && p[0].x + p[3].x < p[1].x + p[2].x;
  }
  private async loadResult(revision: number): Promise<void> {
    const request = ++this.resultRequest;
    this.resultLoading.set(true); this.resultError.set('');
    try {
      const blob = await firstValueFrom(this.http.get(`${this.pageUrl}/preview`,
        { responseType: 'blob', params: { revision } }));
      if (this.destroyed || request !== this.resultRequest || !this.actualResultFilter()) return;
      if (this.resultUrl()) URL.revokeObjectURL(this.resultUrl());
      this.resultUrl.set(URL.createObjectURL(blob)); this.resultRevision = revision;
      this.showOriginal.set(false);
    } catch {
      if (!this.destroyed && request === this.resultRequest)
        this.resultError.set('Your scan was saved, but its preview could not load. Reload the result.');
    } finally {
      if (!this.destroyed && request === this.resultRequest) this.resultLoading.set(false);
    }
  }
  private clearResult(keepImage = false): void {
    this.resultRequest++; this.resultLoading.set(false); this.resultError.set('');
    if (!keepImage) {
      if (this.resultUrl()) URL.revokeObjectURL(this.resultUrl());
      this.resultUrl.set('');
    }
    this.resultRevision = null; this.showOriginal.set(false);
  }
  ngOnDestroy(): void { this.destroyed = true; this.clearResult(); clearTimeout(this.timer); clearTimeout(this.filterTimer); if (this.sourceUrl()) URL.revokeObjectURL(this.sourceUrl()); }
}
