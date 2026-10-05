import { I18nService } from '../core/i18n/i18n.service';
import { HttpClient } from '@angular/common/http';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { DocumentsApiService } from './documents-api.service';

type Box = [number, number, number, number];
type Point = [number, number];
interface BrushStroke { radius: number; points: Point[]; }
interface RepairOperation { operationId: string; state: 'Queued' | 'Ready' | 'Failed' | 'Applied'; hasPreview: boolean; candidates?: Box[] | null; }

@Component({
  selector: 'app-page-cleanup', standalone: true, imports: [RouterLink],
  templateUrl: './page-cleanup.component.html', styleUrl: './page-cleanup.component.scss',
})
export class PageCleanupComponent implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(DocumentsApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly documentId = this.route.snapshot.paramMap.get('documentId')!;
  readonly pageId = this.route.snapshot.paramMap.get('pageId')!;
  /** Closing or finishing returns to this page in the workspace Edit tab. */
  readonly workspaceQuery = { tab: 'edit', page: this.pageId };
  private readonly url = `${inject(API_BASE_URL).replace(/\/+$/, '')}/documents/${this.documentId}/pages/${this.pageId}`;
  readonly originalUrl = signal('');
  readonly previewUrl = signal('');
  readonly boxes = signal<Box[]>([]);
  readonly strokes = signal<BrushStroke[]>([]);
  readonly activeStroke = signal<BrushStroke | null>(null);
  readonly tool = signal<'rectangle' | 'brush' | 'erase'>('rectangle');
  readonly brushRadius = signal(.008);
  readonly drawing = signal<Box | null>(null);
  readonly operation = signal<RepairOperation | null>(null);
  readonly busy = signal(false);
  readonly finding = signal(false);
  readonly zoom = signal(1);
  readonly panMode = signal(false);
  readonly brushViewHeight = signal(1000);
  readonly error = signal('');
  readonly notice = signal(this.i18n.t('pages.clean.notice.initial'));
  private sourceRevisionId: string | null = null;
  private sourceCropRevision: number | null = null;
  private start: [number, number] | null = null;
  private pan: { x: number; y: number; left: number; top: number } | null = null;
  private timer?: ReturnType<typeof setTimeout>;
  private previewGeneration = 0;
  private destroyed = false;
  private selectionOrder: Array<{ kind: 'box'; value: Box } | { kind: 'stroke'; value: BrushStroke }> = [];

  onImageLoad(event: Event): void {
    const image = event.target as HTMLImageElement;
    if (image.naturalWidth > 0 && image.naturalHeight > 0)
      this.brushViewHeight.set(1000 * image.naturalHeight / image.naturalWidth);
  }

  brushUnitRadius(stroke: BrushStroke): number {
    return stroke.radius * Math.min(1000, this.brushViewHeight());
  }

  ngOnInit(): void { void this.load(); }
  ngOnDestroy(): void {
    this.destroyed = true;
    this.previewGeneration++;
    clearTimeout(this.timer);
    if (this.originalUrl()) URL.revokeObjectURL(this.originalUrl());
    if (this.previewUrl()) URL.revokeObjectURL(this.previewUrl());
  }
  private async load(): Promise<void> {
    try {
      const document = await this.api.getDocument(this.documentId);
      const page = document.pages.find(item => item.id === this.pageId);
      if (!page || page.state !== 'Ready') throw new Error('Page unavailable');
      this.sourceRevisionId = page.previewRevision.startsWith('crop-') ? null : page.previewRevision;
      this.sourceCropRevision = page.previewRevision.startsWith('crop-') ?
        Number(page.previewRevision.slice(5)) : null;
      const blob = await firstValueFrom(this.http.get(`${this.url}/preview`, { responseType: 'blob' }));
      if (this.destroyed) return;
      this.originalUrl.set(URL.createObjectURL(blob));
    } catch { this.error.set(this.i18n.t('pages.clean.err.open')); }
  }
  private point(event: PointerEvent, stage: HTMLElement): [number, number] {
    const bounds = stage.getBoundingClientRect();
    return [Math.max(0, Math.min(1, (event.clientX - bounds.left) / bounds.width)),
      Math.max(0, Math.min(1, (event.clientY - bounds.top) / bounds.height))];
  }
  begin(event: PointerEvent, stage: HTMLElement): void {
    if (this.busy() || this.operation()) return;
    event.preventDefault();
    if (this.panMode()) {
      const viewport = stage.parentElement!;
      this.pan = { x: event.clientX, y: event.clientY,
        left: viewport.scrollLeft, top: viewport.scrollTop };
      stage.setPointerCapture(event.pointerId);
      return;
    }
    const point = this.point(event, stage);
    if (this.tool() === 'erase') { this.eraseAt(point); return; }
    if (this.boxes().length + this.strokes().length >= 20) {
      this.error.set(this.i18n.t('pages.clean.err.limit'));
      return;
    }
    if (this.tool() === 'brush') {
      this.activeStroke.set({ radius: this.brushRadius(), points: [point] });
      stage.setPointerCapture(event.pointerId);
      return;
    }
    this.start = this.point(event, stage);
    stage.setPointerCapture(event.pointerId);
    this.drawing.set([this.start[0], this.start[1], this.start[0], this.start[1]]);
  }
  move(event: PointerEvent, stage: HTMLElement): void {
    if (this.pan) {
      const viewport = stage.parentElement!;
      viewport.scrollLeft = this.pan.left + this.pan.x - event.clientX;
      viewport.scrollTop = this.pan.top + this.pan.y - event.clientY;
      return;
    }
    const stroke = this.activeStroke();
    if (stroke) {
      const point = this.point(event, stage);
      const last = stroke.points[stroke.points.length - 1];
      if (stroke.points.length < 256 && Math.hypot(point[0] - last[0], point[1] - last[1]) > .002)
        this.activeStroke.set({ ...stroke, points: [...stroke.points, point] });
      return;
    }
    if (!this.start) return;
    const end = this.point(event, stage);
    this.drawing.set([Math.min(this.start[0], end[0]), Math.min(this.start[1], end[1]),
      Math.max(this.start[0], end[0]), Math.max(this.start[1], end[1])]);
  }
  end(event: PointerEvent, stage: HTMLElement): void {
    if (this.pan) {
      if (stage.hasPointerCapture(event.pointerId)) stage.releasePointerCapture(event.pointerId);
      this.pan = null;
      return;
    }
    if (this.activeStroke()) {
      if (stage.hasPointerCapture(event.pointerId)) stage.releasePointerCapture(event.pointerId);
      const stroke = this.activeStroke()!;
      this.strokes.update(items => [...items, stroke]);
      this.selectionOrder.push({ kind: 'stroke', value: stroke });
      this.activeStroke.set(null);
      return;
    }
    if (!this.start) return;
    if (stage.hasPointerCapture(event.pointerId)) stage.releasePointerCapture(event.pointerId);
    const box = this.drawing();
    if (box && box[2] - box[0] > .005 && box[3] - box[1] > .005 &&
        (box[2] - box[0]) * (box[3] - box[1]) <= .03 && this.boxes().length < 20) {
      this.boxes.update(items => [...items, box]);
      this.selectionOrder.push({ kind: 'box', value: box });
    } else this.error.set(this.i18n.t('pages.clean.err.small'));
    this.start = null;
    this.drawing.set(null);
  }
  cancel(event: PointerEvent, stage: HTMLElement): void {
    if (stage.hasPointerCapture(event.pointerId)) stage.releasePointerCapture(event.pointerId);
    this.pan = null; this.start = null; this.drawing.set(null);
    this.activeStroke.set(null);
  }
  remove(index: number): void {
    const box = this.boxes()[index];
    this.boxes.update(items => items.filter((_, i) => i !== index));
    this.selectionOrder = this.selectionOrder.filter(item => item.value !== box);
  }
  removeStroke(index: number): void {
    const stroke = this.strokes()[index];
    this.strokes.update(items => items.filter((_, i) => i !== index));
    this.selectionOrder = this.selectionOrder.filter(item => item.value !== stroke);
  }
  undoSelection(): void {
    if (this.operation()) return;
    const latest = this.selectionOrder.pop();
    if (latest?.kind === 'box') this.boxes.update(items => items.filter(item => item !== latest.value));
    else if (latest?.kind === 'stroke') this.strokes.update(items => items.filter(item => item !== latest.value));
    else if (this.strokes().length) this.strokes.update(items => items.slice(0, -1));
    else this.boxes.update(items => items.slice(0, -1));
    this.error.set('');
  }
  clearSelection(): void {
    if (this.operation()) return;
    this.boxes.set([]); this.strokes.set([]); this.selectionOrder = [];
    this.error.set('');
  }
  private eraseAt(point: Point): void {
    const boxIndex = this.boxes().findIndex(box => point[0] >= box[0] && point[0] <= box[2] &&
      point[1] >= box[1] && point[1] <= box[3]);
    if (boxIndex >= 0) { this.remove(boxIndex); return; }
    const strokeIndex = this.strokes().findIndex(stroke => stroke.points.some(p =>
      Math.hypot(point[0] - p[0], point[1] - p[1]) <= stroke.radius * 1.5));
    if (strokeIndex >= 0) this.removeStroke(strokeIndex);
  }
  strokePoints(stroke: BrushStroke): string {
    return stroke.points.map(point => `${point[0] * 1000},${point[1] * this.brushViewHeight()}`).join(' ');
  }
  zoomIn(): void { this.zoom.set(Math.min(3, this.zoom() + .5)); }
  zoomOut(): void { this.zoom.set(Math.max(1, this.zoom() - .5)); }
  async findHoles(): Promise<void> {
    if (this.finding() || this.operation()) return;
    this.finding.set(true); this.error.set('');
    try {
      const started = await firstValueFrom(this.http.post<RepairOperation>(`${this.url}/repair/suggestions`, {}));
      this.notice.set(this.i18n.t('pages.clean.notice.looking'));
      this.pollSuggestions(started.operationId);
    } catch {
      this.finding.set(false);
      this.error.set(this.i18n.t('pages.clean.err.detect'));
    }
  }
  private pollSuggestions(id: string): void {
    this.timer = setTimeout(async () => {
      try {
        const status = await firstValueFrom(this.http.get<RepairOperation>(`${this.url}/repair/previews/${id}`));
        if (this.destroyed) return;
        if (status.state === 'Queued') { this.pollSuggestions(id); return; }
        this.finding.set(false);
        if (status.state !== 'Ready') { this.error.set(this.i18n.t('pages.clean.err.holesFailed')); return; }
        const found = (status.candidates ?? []).filter(box => box.length === 4 &&
          box.every(Number.isFinite) && box[0] >= 0 && box[1] >= 0 &&
          box[2] <= 1 && box[3] <= 1 && box[0] < box[2] && box[1] < box[3]);
        const accepted = found.slice(0, 20 - this.boxes().length - this.strokes().length);
        this.boxes.update(existing => [...existing, ...accepted]);
        this.selectionOrder.push(...accepted.map(value => ({ kind: 'box' as const, value })));
        this.notice.set(found.length ?
          this.i18n.t('pages.clean.notice.found', { count: found.length }) :
          this.i18n.t('pages.clean.notice.none'));
      } catch {
        if (!this.destroyed) { this.finding.set(false); this.error.set(this.i18n.t('pages.clean.err.suggestions')); }
      }
    }, 1200);
  }
  boxStyle(box: Box): { left: string; top: string; width: string; height: string } {
    return { left: `${box[0] * 100}%`, top: `${box[1] * 100}%`,
      width: `${(box[2] - box[0]) * 100}%`, height: `${(box[3] - box[1]) * 100}%` };
  }
  async preview(): Promise<void> {
    if (!this.boxes().length && !this.strokes().length || this.busy() || this.finding() || this.operation()) return;
    if (this.boxes().reduce((area, box) => area +
        (box[2] - box[0]) * (box[3] - box[1]), 0) > .05) {
      this.error.set(this.i18n.t('pages.clean.err.cover'));
      return;
    }
    this.busy.set(true); this.error.set('');
    try {
      const result = await firstValueFrom(this.http.post<RepairOperation>(`${this.url}/repair/previews`,
        { sourceRevisionId: this.sourceRevisionId, sourceCropRevision: this.sourceCropRevision,
          rectangles: this.boxes(), strokes: this.strokes() }));
      this.operation.set(result);
      this.notice.set(this.i18n.t('pages.clean.notice.preparing'));
      this.poll(result.operationId, ++this.previewGeneration);
    } catch { this.error.set(this.i18n.t('pages.clean.err.prepare')); }
    finally { this.busy.set(false); }
  }
  private poll(id: string, generation: number): void {
    this.timer = setTimeout(async () => {
      try {
        const status = await firstValueFrom(this.http.get<RepairOperation>(`${this.url}/repair/previews/${id}`));
        if (this.destroyed || generation !== this.previewGeneration) return;
        this.operation.set(status);
        if (status.state === 'Queued') { this.poll(id, generation); return; }
        if (status.state !== 'Ready') { this.error.set(this.i18n.t('pages.clean.err.previewFailed')); return; }
        const blob = await firstValueFrom(this.http.get(`${this.url}/repair/previews/${id}/image`, { responseType: 'blob' }));
        if (this.destroyed || generation !== this.previewGeneration) return;
        this.previewUrl.set(URL.createObjectURL(blob));
        this.notice.set(this.i18n.t('pages.clean.notice.compare'));
      } catch {
        if (!this.destroyed && generation === this.previewGeneration)
          this.error.set(this.i18n.t('pages.clean.err.loadPreview'));
      }
    }, 1200);
  }
  discard(): void {
    this.previewGeneration++;
    clearTimeout(this.timer);
    this.operation.set(null);
    if (this.previewUrl()) URL.revokeObjectURL(this.previewUrl());
    this.previewUrl.set('');
    this.error.set('');
  }
  async apply(): Promise<void> {
    const operation = this.operation();
    if (!operation || operation.state !== 'Ready' || this.busy()) return;
    this.busy.set(true); this.error.set('');
    try {
      await firstValueFrom(this.http.post(`${this.url}/repair/previews/${operation.operationId}/apply`, {}));
      await this.router.navigate(['/documents', this.documentId], { queryParams: this.workspaceQuery });
    } catch { this.error.set(this.i18n.t('pages.clean.err.apply')); }
    finally { this.busy.set(false); }
  }
}
