import { Component, ElementRef, inject, input, output } from '@angular/core';
import { SignatureBox } from './page-signature.models';
import { PageMarkDraft, PageMarkDto, markViewBoxPath } from './page-mark.models';

@Component({ selector: 'app-page-mark-overlay', standalone: true,
  templateUrl: './page-mark-overlay.component.html', styleUrl: './page-mark-overlay.component.scss' })
export class PageMarkOverlayComponent {
  readonly marks = input<PageMarkDto[]>([]);
  readonly draft = input<PageMarkDraft | null>(null);
  readonly selectedId = input<string | null>(null);
  readonly placementMode = input(false);
  readonly panMode = input(false);
  readonly disabled = input(false);
  readonly place = output<{ x: number; y: number }>();
  readonly select = output<string>();
  readonly toggleDelete = output<string>();
  readonly boxChange = output<{ id: string; box: SignatureBox }>();
  private readonly element = inject(ElementRef<HTMLElement>);
  protected readonly corners = ['nw', 'ne', 'sw', 'se'] as const;
  protected path = markViewBoxPath;
  protected entries(): PageMarkDraft[] {
    const draft = this.draft();
    const saved = this.marks().map(mark => draft?.id === mark.id ? draft : mark);
    return draft && !saved.some(mark => mark.id === draft.id) ? [...saved, draft] : saved;
  }
  private drag?: { pointerId: number; x: number; y: number; width: number; height: number; id: string; box: SignatureBox; corner?: string };

  protected placeAt(event: MouseEvent): void {
    if (!this.placementMode() || this.panMode() || this.disabled() || event.target !== event.currentTarget) return;
    const rect = this.element.nativeElement.getBoundingClientRect();
    if (rect.width && rect.height)
      this.place.emit({ x: Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width)),
        y: Math.max(0, Math.min(1, (event.clientY - rect.top) / rect.height)) });
  }
  protected choose(event: MouseEvent, id: string): void {
    event.stopPropagation();
    if (this.disabled() || this.panMode()) return;
    if (this.placementMode() && id !== this.draft()?.id) this.toggleDelete.emit(id);
    else this.select.emit(id);
  }
  protected start(event: PointerEvent, mark: PageMarkDraft, corner?: string): void {
    if (this.disabled() || this.panMode() || event.button !== 0 || this.draft()?.id !== mark.id) return;
    event.stopPropagation(); event.preventDefault();
    const rect = this.element.nativeElement.getBoundingClientRect();
    if (!rect.width || !rect.height) return;
    this.drag = { pointerId: event.pointerId, x: event.clientX, y: event.clientY,
      width: rect.width, height: rect.height, id: mark.id, box: { ...mark.box }, corner };
    (event.currentTarget as HTMLElement).setPointerCapture?.(event.pointerId);
  }
  protected move(event: PointerEvent): void {
    const drag = this.drag;
    if (!drag || drag.pointerId !== event.pointerId) return;
    event.preventDefault(); event.stopPropagation();
    const dx = (event.clientX - drag.x) / drag.width, dy = (event.clientY - drag.y) / drag.height;
    const box = drag.box;
    if (!drag.corner) {
      this.boxChange.emit({ id: drag.id, box: { ...box,
        x: this.clamp(box.x + dx, 0, 1 - box.width), y: this.clamp(box.y + dy, 0, 1 - box.height) } });
      return;
    }
    const west = drag.corner.includes('w'), north = drag.corner.includes('n');
    const ratio = box.width / box.height;
    const delta = Math.abs(dx) >= Math.abs(dy * ratio) ? (west ? -dx : dx) : (north ? -dy : dy) * ratio;
    const anchorX = west ? box.x + box.width : box.x, anchorY = north ? box.y + box.height : box.y;
    const maxWidth = Math.min(west ? anchorX : 1 - anchorX, (north ? anchorY : 1 - anchorY) * ratio);
    const width = this.clamp(box.width + delta, Math.min(.005, maxWidth), maxWidth), height = width / ratio;
    this.boxChange.emit({ id: drag.id, box: { x: west ? anchorX - width : anchorX,
      y: north ? anchorY - height : anchorY, width, height } });
  }
  protected finish(event: PointerEvent): void { if (this.drag?.pointerId === event.pointerId) { event.stopPropagation(); this.drag = undefined; } }
  protected cancel(event: PointerEvent): void {
    if (this.drag?.pointerId !== event.pointerId) return;
    this.boxChange.emit({ id: this.drag.id, box: this.drag.box });
    this.drag = undefined; event.stopPropagation();
  }
  protected key(event: KeyboardEvent, mark: PageMarkDraft): void {
    if (this.disabled() || this.panMode() || this.draft()?.id !== mark.id ||
      !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
    event.preventDefault(); event.stopPropagation();
    const direction = event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 1;
    const horizontal = event.key === 'ArrowLeft' || event.key === 'ArrowRight';
    const box = mark.box;
    this.boxChange.emit({ id: mark.id, box: { ...box,
      x: horizontal ? this.clamp(box.x + direction * .002, 0, 1 - box.width) : box.x,
      y: horizontal ? box.y : this.clamp(box.y + direction * .002, 0, 1 - box.height) } });
  }
  private clamp(value: number, min: number, max: number): number { return Math.max(min, Math.min(max, value)); }
}
