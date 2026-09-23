import { Component, OnChanges, SimpleChanges, computed, input, output, signal } from '@angular/core';
import { OcrPoint } from './document.models';
import { TextEditBox, TextEditStyle } from './text-edit.models';

type Handle = 'n' | 'ne' | 'e' | 'se' | 's' | 'sw' | 'w' | 'nw';
type Gesture = { pointerId: number; kind: 'move' | Handle; origin: TextEditBox;
  startX: number; startY: number };

@Component({
  selector: 'app-text-replacement-overlay',
  standalone: true,
  templateUrl: './text-replacement-overlay.component.html',
  styleUrl: './text-replacement-overlay.component.scss',
})
export class TextReplacementOverlayComponent implements OnChanges {
  readonly box = input.required<TextEditBox>();
  readonly text = input.required<string>();
  readonly style = input.required<TextEditStyle>();
  readonly originalPolygon = input.required<OcrPoint[]>();
  readonly otherPolygons = input<OcrPoint[][]>([]);
  readonly boxChange = output<TextEditBox>();
  protected readonly current = signal<TextEditBox>({ x: 0, y: 0, width: .1, height: .05 });
  protected readonly handles: Handle[] = ['n', 'ne', 'e', 'se', 's', 'sw', 'w', 'nw'];
  protected readonly collision = computed(() => this.otherPolygons().some((polygon) => {
    const box = this.current();
    const xs = polygon.map((point) => point.x);
    const ys = polygon.map((point) => point.y);
    return xs.length > 0 && Math.min(...xs) < box.x + box.width &&
      Math.max(...xs) > box.x && Math.min(...ys) < box.y + box.height &&
      Math.max(...ys) > box.y;
  }));
  protected readonly fontFamily = computed(() =>
    this.style().fontId === 'noto-serif' ? 'Noto Serif' : 'Noto Sans');
  protected readonly sourceArea = computed(() => {
    const points = this.originalPolygon();
    const left = Math.min(...points.map((point) => point.x));
    const right = Math.max(...points.map((point) => point.x));
    const top = Math.min(...points.map((point) => point.y));
    const bottom = Math.max(...points.map((point) => point.y));
    return { left: percent(left), top: percent(top), width: percent(right - left),
      height: percent(bottom - top) };
  });
  private gesture?: Gesture;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['box']) this.current.set({ ...this.box() });
  }

  protected begin(event: PointerEvent, kind: 'move' | Handle): void {
    event.preventDefault();
    event.stopPropagation();
    (event.currentTarget as HTMLElement).focus();
    this.gesture = { pointerId: event.pointerId, kind, origin: { ...this.current() },
      startX: event.clientX, startY: event.clientY };
    (event.currentTarget as HTMLElement).setPointerCapture?.(event.pointerId);
  }

  protected move(event: PointerEvent): void {
    const gesture = this.gesture;
    if (!gesture || gesture.pointerId !== event.pointerId) return;
    const bounds = (event.currentTarget as HTMLElement).getBoundingClientRect();
    if (!bounds.width || !bounds.height) return;
    const dx = (event.clientX - gesture.startX) / bounds.width;
    const dy = (event.clientY - gesture.startY) / bounds.height;
    const b = gesture.origin;
    let x = b.x; let y = b.y; let right = b.x + b.width; let bottom = b.y + b.height;
    if (gesture.kind === 'move') {
      x = clamp(b.x + dx, 0, 1 - b.width);
      y = clamp(b.y + dy, 0, 1 - b.height);
      right = x + b.width; bottom = y + b.height;
    } else {
      if (gesture.kind.includes('w')) x = clamp(b.x + dx, 0, right - .04);
      if (gesture.kind.includes('e')) right = clamp(right + dx, x + .04, 1);
      if (gesture.kind.includes('n')) y = clamp(b.y + dy, 0, bottom - .02);
      if (gesture.kind.includes('s')) bottom = clamp(bottom + dy, y + .02, 1);
    }
    this.update({ x, y, width: right - x, height: bottom - y });
  }

  protected end(event: PointerEvent): void {
    if (this.gesture?.pointerId !== event.pointerId) return;
    this.gesture = undefined;
  }

  protected keydown(event: KeyboardEvent): void {
    if (event.key === 'Escape' && this.gesture) {
      event.preventDefault();
      this.update(this.gesture.origin);
      this.gesture = undefined;
      return;
    }
    if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
    event.preventDefault();
    const b = this.current();
    const step = .005;
    const horizontal = event.key === 'ArrowRight' ? step : event.key === 'ArrowLeft' ? -step : 0;
    const vertical = event.key === 'ArrowDown' ? step : event.key === 'ArrowUp' ? -step : 0;
    if (event.shiftKey) this.update({ ...b,
      width: clamp(b.width + horizontal, .04, 1 - b.x),
      height: clamp(b.height + vertical, .02, 1 - b.y) });
    else this.update({ ...b, x: clamp(b.x + horizontal, 0, 1 - b.width),
      y: clamp(b.y + vertical, 0, 1 - b.height) });
  }

  private update(next: TextEditBox): void {
    this.current.set(next);
    this.boxChange.emit({ ...next });
  }
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(maximum, Math.max(minimum, value));
}

function percent(value: number): number { return Math.round(value * 10000) / 100; }
