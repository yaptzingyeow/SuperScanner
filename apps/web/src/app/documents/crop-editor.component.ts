import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/api/security.interceptor';

interface Point { x: number; y: number; }
type ScanFilter = 'Original' | 'Document' | 'Bright' | 'Grayscale' | 'BlackAndWhite';
export type CropGuidance = 'accurate' | 'verify' | 'manual';
export interface CropState {
  revision: number; appliedRevision: number; status: string; confidence: number | null;
  source: string | null; modelVersion: string | null; diagnosticsCode: string | null;
  points: Point[] | null; filter: ScanFilter; appliedFilter: ScanFilter;
}
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
  private readonly pageUrl = `${inject(API_BASE_URL).replace(/\/+$/, '')}/documents/${this.documentId}/pages/${this.pageId}`;
  protected readonly names = ['Top left', 'Top right', 'Bottom right', 'Bottom left'];
  protected readonly filters: { id: ScanFilter; label: string; description: string }[] = [
    { id: 'Original', label: 'Original', description: 'Keep the photo’s colors without enhancement.' },
    { id: 'Document', label: 'Document', description: 'Improve contrast and gently sharpen text.' },
    { id: 'Bright', label: 'Bright', description: 'Lighten a dark photo while keeping its colors.' },
    { id: 'Grayscale', label: 'Grayscale', description: 'Remove color and retain shades of gray.' },
    { id: 'BlackAndWhite', label: 'Black & White', description: 'Create crisp black text on white. Faint handwriting may disappear.' },
  ];
  protected readonly filter = signal<ScanFilter>('Document');
  protected readonly filterDescription = computed(() => this.filters.find(item => item.id === this.filter())!.description);
  private filterDirty = false;

  protected selectFilter(value: ScanFilter): void {
    if (this.busy()) return;
    this.filter.set(value);
    this.filterDirty = true;
  }
  protected readonly points = signal<Point[]>(fullImage());
  protected readonly state = signal<CropState | null>(null);
  readonly guidance = signal<CropGuidance>('verify');
  protected readonly sourceUrl = signal('');
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
      if (state.status === 'Ready' && this.applying) {
        await this.router.navigate(['/documents', this.documentId]); return;
      }
      if (state.status === 'Detecting' || state.status === 'Processing')
        this.timer = setTimeout(() => void this.reload(), 1500);
    } catch { if (!this.destroyed) this.error.set('Could not read the crop status. Reload and try again.'); }
  }
  protected reset(): void { if (this.busy()) return; this.points.set(fullImage()); this.dirty = true; this.error.set(''); }
  protected async submit(detect: boolean): Promise<void> {
    const state = this.state();
    if (!state || this.busy() || (!detect && !this.valid())) return;
    this.error.set(''); this.submitting.set(true);
    try {
      const updated = await firstValueFrom(this.http.post<CropState>(`${this.pageUrl}/crop/${detect ? 'detect' : 'apply'}`,
        { revision: state.revision, points: detect ? null : this.points(), filter: this.filter() }));
      if (this.destroyed) return;
      this.applyCropState(updated); this.dirty = false; this.filterDirty = false; this.applying = !detect;
      await this.reload();
    } catch (error) {
      this.error.set(error instanceof HttpErrorResponse && error.status === 409
        ? 'This crop was changed in another session. Reload before applying your changes.'
        : 'Could not save this crop. Check the corners and try again.');
    } finally { this.submitting.set(false); }
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
    this.points.update(points => points.map((p, i) => i === index ? { x: Math.max(0, Math.min(1, x)), y: Math.max(0, Math.min(1, y)) } : p));
    this.dirty = true;
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
  ngOnDestroy(): void { this.destroyed = true; clearTimeout(this.timer); if (this.sourceUrl()) URL.revokeObjectURL(this.sourceUrl()); }
}
