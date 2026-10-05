import { I18nService } from '../core/i18n/i18n.service';
import { Component, ElementRef, OnDestroy, ViewChild, output, signal, inject } from '@angular/core';
import { canvasPng } from './signature-image';
import { PageSignatureService } from './page-signature.service';

interface Point { x: number; y: number }

@Component({
  selector: 'app-signature-creator', standalone: true,
  templateUrl: './signature-creator.component.html', styleUrl: './signature-creator.component.scss',
})
export class SignatureCreatorComponent implements OnDestroy {
  protected readonly i18n = inject(I18nService);
  private readonly signatures = inject(PageSignatureService);
  private strengthTimer?: ReturnType<typeof setTimeout>;
  private drawing?: ElementRef<HTMLCanvasElement>;
  @ViewChild('drawing') set drawingElement(element: ElementRef<HTMLCanvasElement> | undefined) {
    this.drawing = element;
    if (element) this.render();
  }
  readonly confirmed = output<Blob>();
  readonly cancelled = output<void>();
  protected readonly mode = signal<'upload' | 'draw'>('upload');
  protected readonly strength = signal(.5);
  protected readonly keepOriginal = signal(false);
  protected readonly originalUrl = signal('');
  protected readonly previewUrl = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly strokeCount = signal(0);
  private source?: File;
  private preview?: Blob;
  private strokes: Point[][] = [];
  private pending?: { id: number; points: Point[] };
  private generation = 0;
  private destroyed = false;

  protected switchMode(mode: 'upload' | 'draw'): void { this.mode.set(mode); this.error.set(''); }
  protected async upload(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0]; input.value = '';
    if (!file) return;
    if (!['image/png', 'image/jpeg'].includes(file.type) || file.size > 5 * 1024 * 1024) {
      this.error.set(this.i18n.t('editor.errChooseImage')); return;
    }
    this.source = file;
    if (this.originalUrl()) URL.revokeObjectURL(this.originalUrl());
    this.originalUrl.set(URL.createObjectURL(file));
    await this.process();
  }
  protected async changeStrength(event: Event): Promise<void> {
    this.strength.set(Number((event.target as HTMLInputElement).value));
    // The slider fires on every step; ask the server once the person pauses.
    clearTimeout(this.strengthTimer);
    this.strengthTimer = setTimeout(() => void this.process(), 250);
  }
  protected async changeOriginal(event: Event): Promise<void> {
    this.keepOriginal.set((event.target as HTMLInputElement).checked); await this.process();
  }
  private async process(): Promise<void> {
    if (!this.source) return;
    const generation = ++this.generation;
    this.busy.set(true); this.error.set(''); this.preview = undefined;
    try {
      const blob = await this.signatures.prepare(this.source, this.strength(), this.keepOriginal());
      if (generation !== this.generation || this.destroyed) return;
      if (this.previewUrl()) URL.revokeObjectURL(this.previewUrl());
      this.preview = blob; this.previewUrl.set(URL.createObjectURL(blob));
    } catch (error) {
      if (generation === this.generation && !this.destroyed) this.error.set(this.i18n.t(await prepareErrorKey(error)));
    } finally { if (generation === this.generation && !this.destroyed) this.busy.set(false); }
  }
  protected canConfirm(): boolean {
    return !this.busy() && (this.mode() === 'draw' ? this.strokeCount() > 0 && !this.pending : !!this.preview);
  }
  protected start(event: PointerEvent): void {
    if (event.button !== 0 || this.pending) return;
    event.preventDefault();
    (event.currentTarget as HTMLCanvasElement).setPointerCapture?.(event.pointerId);
    this.pending = { id: event.pointerId, points: [this.point(event)] };
    this.render();
  }
  protected move(event: PointerEvent): void {
    if (this.pending?.id !== event.pointerId) return;
    event.preventDefault(); this.pending.points.push(this.point(event)); this.render();
  }
  protected finish(event: PointerEvent): void {
    if (this.pending?.id !== event.pointerId) return;
    this.pending.points.push(this.point(event)); this.strokes.push(this.pending.points);
    this.pending = undefined; this.strokeCount.set(this.strokes.length); this.render();
  }
  protected cancelStroke(event: PointerEvent): void {
    if (this.pending?.id !== event.pointerId) return;
    this.pending = undefined; this.render();
  }
  protected undo(): void { this.pending = undefined; this.strokes.pop(); this.strokeCount.set(this.strokes.length); this.render(); }
  protected clear(): void { this.pending = undefined; this.strokes = []; this.strokeCount.set(0); this.render(); }
  private point(event: PointerEvent): Point {
    const canvas = event.currentTarget as HTMLCanvasElement;
    const rect = canvas.getBoundingClientRect();
    return { x: Math.max(0, Math.min(canvas.width, (event.clientX - rect.left) * canvas.width / (rect.width || canvas.width))),
      y: Math.max(0, Math.min(canvas.height, (event.clientY - rect.top) * canvas.height / (rect.height || canvas.height))) };
  }
  private render(): void {
    const canvas = this.drawing?.nativeElement;
    const context = canvas?.getContext('2d');
    if (!canvas || !context) return;
    context.clearRect(0, 0, canvas.width, canvas.height);
    context.strokeStyle = '#172033'; context.fillStyle = '#172033'; context.lineWidth = 3;
    context.lineCap = 'round'; context.lineJoin = 'round';
    for (const stroke of [...this.strokes, ...(this.pending ? [this.pending.points] : [])]) {
      if (stroke.length === 1) { context.beginPath(); context.arc(stroke[0].x, stroke[0].y, 1.5, 0, Math.PI * 2); context.fill(); }
      else { context.beginPath(); context.moveTo(stroke[0].x, stroke[0].y); for (const point of stroke.slice(1)) context.lineTo(point.x, point.y); context.stroke(); }
    }
  }
  protected async confirm(): Promise<void> {
    if (!this.canConfirm()) return;
    if (this.mode() === 'upload') { this.confirmed.emit(this.preview!); return; }
    this.busy.set(true); this.error.set('');
    try {
      const source = this.drawing!.nativeElement;
      const points = this.strokes.flat();
      const left = Math.max(0, Math.floor(Math.min(...points.map(point => point.x))) - 4);
      const top = Math.max(0, Math.floor(Math.min(...points.map(point => point.y))) - 4);
      const right = Math.min(source.width, Math.ceil(Math.max(...points.map(point => point.x))) + 4);
      const bottom = Math.min(source.height, Math.ceil(Math.max(...points.map(point => point.y))) + 4);
      const cropped = document.createElement('canvas'); cropped.width = right - left; cropped.height = bottom - top;
      const context = cropped.getContext('2d');
      if (!context) throw new Error('Drawing is not supported by this browser.');
      context.drawImage(source, left, top, cropped.width, cropped.height, 0, 0, cropped.width, cropped.height);
      const blob = await canvasPng(cropped);
      if (!this.destroyed) this.confirmed.emit(blob);
    } catch { this.error.set(this.i18n.t('editor.errPrepareDrawing')); }
    finally { if (!this.destroyed) this.busy.set(false); }
  }
  ngOnDestroy(): void { clearTimeout(this.strengthTimer);
    this.destroyed = true; this.generation++;
    for (const url of [this.originalUrl(), this.previewUrl()]) if (url) URL.revokeObjectURL(url);
  }
}

/** Message key for a failed server-side preparation (the problem body is a Blob for blob requests). */
async function prepareErrorKey(error: unknown): Promise<string> {
  const body = (error as { error?: unknown }).error;
  let code = '';
  try { code = body instanceof Blob ? JSON.parse(await body.text()).code ?? '' : (body as { code?: string })?.code ?? ''; } catch { /* not JSON */ }
  return {
    signature_no_ink: 'editor.errSigNoInk',
    signature_too_large: 'editor.errSigTooLarge',
    signature_invalid_image: 'editor.errChooseImage',
  }[code] ?? 'editor.errPrepareSig';
}
