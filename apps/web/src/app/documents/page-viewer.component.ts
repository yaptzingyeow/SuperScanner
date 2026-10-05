import {
  afterNextRender,
  Component, DestroyRef, ElementRef, afterRenderEffect, effect, inject, input, linkedSignal, output, signal,
  computed, untracked, viewChild,
} from '@angular/core';
import { DocumentsApiService } from './documents-api.service';
import { DocumentPage, PageOcr } from './document.models';
import { OcrTextOverlayComponent } from './ocr-text-overlay.component';
import { PageSignatureService } from './page-signature.service';
import { PageMarkService } from './page-mark.service';
import { PageMarkDto, markViewBoxPath } from './page-mark.models';
import { SignatureBox } from './page-signature.models';

/** A saved signature shown read-only on top of its page. */
interface PlacedSignature { id: string; box: SignatureBox; url: string }

export type ViewerLayout = 'continuous' | 'single' | 'double';

export const MIN_ZOOM = 25;
export const MAX_ZOOM = 400;
const BASE_PAGE_WIDTH = 640;

export function clampZoom(value: number): number {
  const n = Number.isFinite(value) ? value : 100;
  return Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, Math.round(n)));
}

@Component({
  selector: 'app-page-viewer',
  standalone: true,
  imports: [OcrTextOverlayComponent],
  templateUrl: './page-viewer.component.html',
  styleUrl: './page-viewer.component.scss',
})
export class PageViewerComponent {
  private readonly api = inject(DocumentsApiService);
  private readonly signatureApi = inject(PageSignatureService);
  private readonly markApi = inject(PageMarkService);

  readonly documentId = input.required<string>();
  readonly pages = input.required<readonly DocumentPage[]>();
  readonly selectedPageId = input<string | null>(null);
  readonly layout = input<ViewerLayout>('continuous');
  readonly zoom = input(100);
  readonly highlights = input<Record<string, string[]>>({});
  /** Pan mode: dragging moves the pages instead of selecting text. */
  readonly panMode = input(false);
  readonly selectPage = output<string>();
  readonly zoomChange = output<number>();

  private readonly canvas = viewChild<ElementRef<HTMLElement>>('canvas');
  protected readonly panning = signal(false);
  private drag?: { pointerId: number; x: number; y: number; left: number; top: number };

  protected beginPan(event: PointerEvent): void {
    if (!this.panMode() || event.button !== 0) return;
    if (event.target instanceof Element && event.target.closest('button, a, input')) return;
    const viewport = event.currentTarget as HTMLElement;
    event.preventDefault();
    this.drag = { pointerId: event.pointerId, x: event.clientX, y: event.clientY,
      left: viewport.scrollLeft, top: viewport.scrollTop };
    this.panning.set(true);
    viewport.setPointerCapture?.(event.pointerId);
  }

  protected movePan(event: PointerEvent): void {
    if (!this.drag || this.drag.pointerId !== event.pointerId) return;
    const viewport = event.currentTarget as HTMLElement;
    viewport.scrollLeft = this.drag.left + this.drag.x - event.clientX;
    viewport.scrollTop = this.drag.top + this.drag.y - event.clientY;
  }

  protected endPan(event: PointerEvent): void {
    if (this.drag?.pointerId !== event.pointerId) return;
    this.drag = undefined;
    this.panning.set(false);
  }

  private readonly zoomOverride = linkedSignal<number, number>({
    source: this.zoom,
    computation: (value) => value,
  });
  readonly effectiveZoom = computed(() => clampZoom(this.zoomOverride()));
  protected readonly pageWidth = computed(() => `${(BASE_PAGE_WIDTH * this.effectiveZoom()) / 100}px`);

  readonly visiblePages = computed<readonly DocumentPage[]>(() => {
    const pages = this.pages();
    const layout = this.layout();
    if (layout === 'continuous') return pages;
    const index = Math.max(0, pages.findIndex((p) => p.id === this.selectedPageId()));
    if (layout === 'single') return pages.slice(index, index + 1);
    return pages.slice(index, index + 2);
  });

  protected readonly urls = signal<Record<string, string>>({});
  protected readonly ocrs = signal<Record<string, PageOcr>>({});
  private readonly loadedRevision = new Map<string, string>();
  private readonly ocrRequested = new Map<string, string>();
  /** Signatures and tick/cross marks are stored apart from the page image, so they are drawn here. */
  protected readonly signatures = signal<Record<string, PlacedSignature[]>>({});
  protected readonly marks = signal<Record<string, PageMarkDto[]>>({});
  protected readonly markPath = markViewBoxPath;
  private readonly signatureImages = new Map<string, string>();
  private readonly overlayRun = new Map<string, number>();
  private destroyed = false;

  constructor() {
    // On a narrow screen, start with the whole page width visible instead of cutting it off.
    afterNextRender(() => {
      const width = this.canvas()?.nativeElement.clientWidth ?? 0;
      if (width > 0 && width < BASE_PAGE_WIDTH * this.effectiveZoom() / 100) this.fitWidth();
    });
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      for (const url of Object.values(this.urls())) URL.revokeObjectURL(url);
      for (const url of this.signatureImages.values()) URL.revokeObjectURL(url);
    });
    effect(() => {
      const visible = this.visiblePages();
      const documentId = this.documentId();
      untracked(() => {
        for (const page of visible) {
          this.ensureLoaded(documentId, page);
          // Re-read on every pages refresh: adding a signature does not change the page revision.
          this.loadOverlays(documentId, page);
        }
      });
    });
    // In the continuous layout, bring the selected page (rail click, search hit) into view once rendered.
    afterRenderEffect(() => {
      const pageId = this.selectedPageId();
      if (this.layout() !== 'continuous' || !pageId) return;
      untracked(() => this.scrollToPage(pageId));
    });
  }

  private scrollToPage(pageId: string): void {
    const canvas = this.canvas()?.nativeElement;
    const target = Array.from(canvas?.querySelectorAll<HTMLElement>('[data-page-id]') ?? [])
      .find((element) => element.dataset['pageId'] === pageId);
    if (typeof target?.scrollIntoView !== 'function') return;
    target.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
  }

  fitWidthZoom(baseWidth = BASE_PAGE_WIDTH): number {
    const width = this.canvas()?.nativeElement.clientWidth ?? 0;
    return width > 0 ? clampZoom((width / baseWidth) * 100) : this.effectiveZoom();
  }

  fitWidth(): void {
    const next = this.fitWidthZoom();
    this.zoomOverride.set(next);
    this.zoomChange.emit(next);
  }

  protected highlightsFor(pageId: string): readonly string[] {
    return this.highlights()[pageId] ?? [];
  }

  protected ocrFor(page: DocumentPage): PageOcr | null {
    const ocr = this.ocrs()[page.id];
    return ocr && ocr.state === 'Ready' ? ocr : null;
  }

  protected signaturesFor(pageId: string): PlacedSignature[] {
    return this.signatures()[pageId] ?? [];
  }

  protected marksFor(pageId: string): PageMarkDto[] {
    return this.marks()[pageId] ?? [];
  }

  private loadOverlays(documentId: string, page: DocumentPage): void {
    const run = (this.overlayRun.get(page.id) ?? 0) + 1;
    this.overlayRun.set(page.id, run);
    const current = () => !this.destroyed && this.overlayRun.get(page.id) === run;
    void this.markApi.list(documentId, page.id).then((marks) => {
      if (current()) this.marks.update((cur) => ({ ...cur, [page.id]: marks.filter((mark) => !mark.isDeleted) }));
    }).catch(() => undefined);
    void this.signatureApi.list(documentId, page.id).then(async (signatures) => {
      const placed = await Promise.all(signatures.map(async (signature) => {
        const key = `${signature.id}:${signature.revision}`;
        let url = this.signatureImages.get(key);
        if (!url) {
          url = URL.createObjectURL(await this.signatureApi.image(documentId, page.id, signature.id));
          this.signatureImages.set(key, url);
        }
        return { id: signature.id, box: signature.box, url };
      }));
      if (current()) this.signatures.update((cur) => ({ ...cur, [page.id]: placed }));
    }).catch(() => undefined);
  }

  private ensureLoaded(documentId: string, page: DocumentPage): void {
    if (!page.hasPreview) return;
    const key = `${documentId}:${page.id}`;
    const revision = page.previewRevision;
    if (this.loadedRevision.get(key) !== revision) {
      this.loadedRevision.set(key, revision);
      void this.api.getPagePreview(documentId, page.id).then((blob) => {
        if (this.destroyed || this.loadedRevision.get(key) !== revision) return;
        const url = URL.createObjectURL(blob);
        const old = this.urls()[page.id];
        this.urls.update((cur) => ({ ...cur, [page.id]: url }));
        if (old) URL.revokeObjectURL(old);
      }).catch(() => {
        if (this.loadedRevision.get(key) === revision) this.loadedRevision.delete(key);
      });
    }
    if (this.ocrRequested.get(key) !== revision) {
      this.ocrRequested.set(key, revision);
      if (this.ocrs()[page.id]) {
        this.ocrs.update((cur) => {
          const { [page.id]: _stale, ...rest } = cur;
          return rest;
        });
      }
      void this.api.getPageOcr(documentId, page.id).then((ocr) => {
        if (this.destroyed || this.ocrRequested.get(key) !== revision) return;
        this.ocrs.update((cur) => ({ ...cur, [page.id]: ocr }));
      }).catch(() => {
        if (this.ocrRequested.get(key) === revision) this.ocrRequested.delete(key);
      });
    }
  }
}
