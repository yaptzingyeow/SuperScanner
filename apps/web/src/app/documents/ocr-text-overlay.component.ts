import { I18nService } from '../core/i18n/i18n.service';
import { DecimalPipe } from '@angular/common';
import { Component, OnChanges, OnDestroy, SimpleChanges, computed, input, output, signal, inject } from '@angular/core';
import { OcrPoint, PageOcr } from './document.models';
import {
  SelectionRegion,
  flattenSelectableWords,
  selectWordsInRegion,
  summarizeSelection,
} from './ocr-selection';

export interface OcrEditSelection {
  pageId: string;
  ocrResultId: string;
  wordIds: string[];
  phrase: string;
  textType: 'Printed';
  polygon: OcrPoint[];
}

@Component({
  selector: 'app-ocr-text-overlay',
  standalone: true,
  imports: [DecimalPipe],
  templateUrl: './ocr-text-overlay.component.html',
  styleUrl: './ocr-text-overlay.component.scss',
})
export class OcrTextOverlayComponent implements OnChanges, OnDestroy {
  protected readonly i18n = inject(I18nService);
  readonly pageId = input.required<string>();
  readonly ocr = input.required<PageOcr>();
  readonly highlightWordIds = input<readonly string[]>([]);
  protected readonly highlightSet = computed(() => new Set(this.highlightWordIds()));
  readonly editSelection = output<OcrEditSelection>();
  readonly deleteSelection = output<OcrEditSelection>();

  protected readonly selectedIds = signal<ReadonlySet<string>>(new Set());
  protected readonly words = computed(() => flattenSelectableWords(this.ocr().elements));
  protected readonly selectedWords = computed(() => {
    const ids = this.selectedIds();
    return this.words().filter((word) => ids.has(word.id));
  });
  protected readonly summary = computed(() => summarizeSelection(this.selectedWords()));
  protected readonly actionPosition = computed(() => {
    const points = this.selectedWords().flatMap((word) => word.polygon);
    if (!points.length) return { top: '0%', left: '0%', transform: 'none' };
    const minX = Math.min(...points.map((point) => point.x));
    const maxX = Math.max(...points.map((point) => point.x));
    const minY = Math.min(...points.map((point) => point.y));
    const maxY = Math.max(...points.map((point) => point.y));
    const below = minY < .12;
    const anchorRight = minX > .5;
    return {
      top: `${(below ? maxY : minY) * 100}%`,
      left: `${(anchorRight ? maxX : minX) * 100}%`,
      transform: `${anchorRight ? 'translateX(-100%)' : ''}${below ? '' : ' translateY(calc(-100% - .4rem))'}`.trim(),
    };
  });
  protected readonly focusedIndex = signal(-1);
  protected readonly activeWordDomId = computed(() => {
    const word = this.words()[this.focusedIndex()];
    return word ? `ocr-word-${word.id}` : null;
  });

  private dragStart?: OcrPoint;
  private dragStartClient?: OcrPoint;
  private activePointerId?: number;
  private anchorIndex?: number;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['pageId'] || changes['ocr']) this.clearSelection();
  }

  protected beginSelection(event: PointerEvent): void {
    const point = this.toNormalizedPoint(event);
    if (!point) return;
    event.stopPropagation();
    event.preventDefault();
    (event.currentTarget as SVGSVGElement).focus();
    this.dragStart = point;
    this.dragStartClient = { x: event.clientX, y: event.clientY };
    this.activePointerId = event.pointerId;
    const target = event.currentTarget as SVGSVGElement;
    target.setPointerCapture?.(event.pointerId);
    this.applyRegion({ x1: point.x, y1: point.y, x2: point.x, y2: point.y });
  }

  protected updateSelection(event: PointerEvent): void {
    if (!this.dragStart || event.pointerId !== this.activePointerId) return;
    const point = this.toNormalizedPoint(event);
    if (!point) return;
    event.stopPropagation();
    this.applyRegion({
      x1: this.dragStart.x,
      y1: this.dragStart.y,
      x2: point.x,
      y2: point.y,
    });
  }

  protected finishSelection(event: PointerEvent): void {
    if (!this.dragStart || event.pointerId !== this.activePointerId) return;
    const point = this.toNormalizedPoint(event);
    const clickSized = this.dragStartClient !== undefined &&
      Math.hypot(
        event.clientX - this.dragStartClient.x,
        event.clientY - this.dragStartClient.y,
      ) <= 4;
    if (point && clickSized) {
      event.stopPropagation();
      this.applyRegion({ x1: point.x, y1: point.y, x2: point.x, y2: point.y });
    } else {
      this.updateSelection(event);
    }
    this.endGesture(event);
  }

  protected cancelSelection(event: PointerEvent): void {
    if (this.activePointerId !== undefined &&
        event.pointerId !== undefined && event.pointerId !== this.activePointerId) return;
    this.dragStart = undefined;
    this.dragStartClient = undefined;
    this.activePointerId = undefined;
  }

  protected handleKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.clearSelection();
      return;
    }
    if (event.key === 'Enter') {
      const word = this.words()[this.focusedIndex()];
      if (!word) return;
      event.preventDefault();
      this.anchorIndex = this.focusedIndex();
      this.selectedIds.set(new Set([word.id]));
      return;
    }
    if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;
    if (this.words().length === 0) return;
    event.preventDefault();
    const direction = event.key === 'ArrowRight' ? 1 : -1;
    const current = this.focusedIndex();
    const next = current < 0
      ? 0
      : Math.min(this.words().length - 1, Math.max(0, current + direction));
    if (event.shiftKey) {
      this.anchorIndex ??= current < 0 ? next : current;
      const start = Math.min(this.anchorIndex, next);
      const end = Math.max(this.anchorIndex, next);
      this.selectedIds.set(new Set(this.words().slice(start, end + 1).map((word) => word.id)));
    } else {
      this.anchorIndex = next;
    }
    this.focusedIndex.set(next);
  }

  protected polygonPoints(points: OcrPoint[]): string {
    return points.map((point) => `${point.x},${point.y}`).join(' ');
  }

  protected requestEdit(kind: 'replace' | 'delete' = 'replace'): void {
    const selected = this.selectedWords();
    const summary = this.summary();
    const ocrResultId = this.ocr().resultId;
    if (!summary || summary.textType !== 'Printed' || selected.length === 0 || !ocrResultId) return;
    const points = selected.flatMap((word) => word.polygon);
    const left = Math.min(...points.map((point) => point.x));
    const right = Math.max(...points.map((point) => point.x));
    const top = Math.min(...points.map((point) => point.y));
    const bottom = Math.max(...points.map((point) => point.y));
    const selection: OcrEditSelection = {
      pageId: this.pageId(),
      ocrResultId,
      wordIds: selected.map((word) => word.id),
      phrase: summary.phrase,
      textType: 'Printed',
      polygon: [
        { x: left, y: top }, { x: right, y: top },
        { x: right, y: bottom }, { x: left, y: bottom },
      ],
    };
    if (kind === 'delete') this.deleteSelection.emit(selection);
    else this.editSelection.emit(selection);
  }

  ngOnDestroy(): void {
    this.clearSelection();
  }

  private applyRegion(region: SelectionRegion): void {
    const selected = selectWordsInRegion(this.words(), region);
    this.selectedIds.set(new Set(selected.map((word) => word.id)));
  }

  private endGesture(event: PointerEvent): void {
    const target = event.currentTarget as SVGSVGElement;
    if (target.hasPointerCapture?.(event.pointerId)) target.releasePointerCapture(event.pointerId);
    this.dragStart = undefined;
    this.dragStartClient = undefined;
    this.activePointerId = undefined;
  }

  private clearSelection(): void {
    this.dragStart = undefined;
    this.dragStartClient = undefined;
    this.activePointerId = undefined;
    this.anchorIndex = undefined;
    this.focusedIndex.set(-1);
    this.selectedIds.set(new Set());
  }

  private toNormalizedPoint(event: PointerEvent): OcrPoint | null {
    const bounds = (event.currentTarget as SVGSVGElement).getBoundingClientRect();
    if (bounds.width <= 0 || bounds.height <= 0) return null;
    return {
      x: Math.min(1, Math.max(0, (event.clientX - bounds.left) / bounds.width)),
      y: Math.min(1, Math.max(0, (event.clientY - bounds.top) / bounds.height)),
    };
  }
}
