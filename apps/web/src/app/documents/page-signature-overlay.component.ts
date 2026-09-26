import { Component, ElementRef, computed, inject, input, output } from '@angular/core';
import { SignatureBox, SignatureDraft, SignatureView } from './page-signature.models';

@Component({ selector: 'app-page-signature-overlay', standalone: true,
  templateUrl: './page-signature-overlay.component.html', styleUrl: './page-signature-overlay.component.scss' })
export class PageSignatureOverlayComponent {
  readonly signatures = input<SignatureView[]>([]);
  readonly selectedId = input<string | null>(null);
  readonly draft = input<SignatureDraft | null>(null);
  readonly panMode = input(false);
  readonly disabled = input(false);
  readonly selected = output<string>();
  readonly boxChange = output<{ id: string; box: SignatureBox }>();
  private readonly element = inject(ElementRef<HTMLElement>);
  protected readonly corners = ['nw', 'ne', 'sw', 'se'] as const;
  protected readonly entries = computed(() => {
    const draft = this.draft();
    const saved: SignatureDraft[] = this.signatures().map(signature => draft?.id === signature.id ? draft : signature);
    return draft && !saved.some(signature => signature.id === draft.id) ? [...saved, draft] : saved;
  });
  private drag?: { pointerId: number; x: number; y: number; width: number; height: number; id: string; box: SignatureBox; corner?: string };

  protected choose(id: string): void { if (!this.panMode() && !this.disabled() && this.selectedId() !== id) this.selected.emit(id); }
  protected start(event: PointerEvent, signature: SignatureDraft, corner?: string): void {
    if (this.panMode() || this.disabled() || event.button !== 0) return;
    event.stopPropagation(); event.preventDefault(); this.choose(signature.id);
    if (this.draft()?.id !== signature.id) return;
    const rect = this.element.nativeElement.getBoundingClientRect();
    if (!rect.width || !rect.height) return;
    this.drag = { pointerId: event.pointerId, x: event.clientX, y: event.clientY, width: rect.width, height: rect.height, id: signature.id, box: { ...signature.box }, corner };
    (event.currentTarget as HTMLElement).setPointerCapture?.(event.pointerId);
  }
  protected move(event: PointerEvent): void {
    const drag = this.drag;
    if (!drag || drag.pointerId !== event.pointerId) return;
    event.preventDefault(); event.stopPropagation();
    const dx = (event.clientX - drag.x) / drag.width, dy = (event.clientY - drag.y) / drag.height;
    const start = drag.box;
    if (!drag.corner) {
      this.emit(drag.id, { ...start, x: this.clamp(start.x + dx, 0, 1 - start.width), y: this.clamp(start.y + dy, 0, 1 - start.height) }); return;
    }
    const west = drag.corner.includes('w'), north = drag.corner.includes('n');
    const ratio = start.width / start.height;
    const deltaWidth = (west ? -dx : dx), deltaFromHeight = (north ? -dy : dy) * ratio;
    const delta = Math.abs(deltaWidth) >= Math.abs(deltaFromHeight) ? deltaWidth : deltaFromHeight;
    const anchorX = west ? start.x + start.width : start.x, anchorY = north ? start.y + start.height : start.y;
    const maximum = Math.min(west ? anchorX : 1 - anchorX, (north ? anchorY : 1 - anchorY) * ratio);
    const width = this.clamp(start.width + delta, Math.min(.01, maximum), maximum), height = width / ratio;
    this.emit(drag.id, { x: west ? anchorX - width : anchorX, y: north ? anchorY - height : anchorY, width, height });
  }
  protected finish(event: PointerEvent): void { if (this.drag?.pointerId === event.pointerId) { event.stopPropagation(); this.drag = undefined; } }
  protected cancel(event: PointerEvent): void {
    if (this.drag?.pointerId !== event.pointerId) return;
    this.emit(this.drag.id, this.drag.box); this.drag = undefined; event.stopPropagation();
  }
  protected key(event: KeyboardEvent, signature: SignatureDraft): void {
    if (this.panMode() || this.disabled()) return;
    if (event.key === 'Enter') { event.preventDefault(); this.choose(signature.id); return; }
    if (this.draft()?.id !== signature.id || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
    event.preventDefault(); event.stopPropagation();
    const box = signature.box;
    const direction = event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 1;
    if (event.shiftKey) {
      const width = this.clamp(box.width + direction * .005, .01, Math.min(1 - box.x, (1 - box.y) * box.width / box.height));
      this.emit(signature.id, { ...box, width, height: width * box.height / box.width });
    } else {
      const horizontal = event.key === 'ArrowLeft' || event.key === 'ArrowRight';
      this.emit(signature.id, { ...box, x: horizontal ? this.clamp(box.x + direction * .005, 0, 1 - box.width) : box.x,
        y: horizontal ? box.y : this.clamp(box.y + direction * .005, 0, 1 - box.height) });
    }
  }
  private emit(id: string, box: SignatureBox): void { this.boxChange.emit({ id, box }); }
  private clamp(value: number, min: number, max: number): number { return Math.max(min, Math.min(max, value)); }
}
