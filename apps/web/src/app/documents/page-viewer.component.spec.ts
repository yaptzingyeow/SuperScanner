import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DocumentsApiService } from './documents-api.service';
import { DocumentPage, PageOcr } from './document.models';
import { PageViewerComponent } from './page-viewer.component';

const page = (n: number, extra: Partial<DocumentPage> = {}): DocumentPage => ({
  id: `p${n}`, position: n, pageNumber: n, sourceUploadId: 'u', sourcePageIndex: n, state: 'Ready',
  hasPreview: true, hasOriginal: true, canCrop: true, cropStatus: 'None', cropRevision: 0,
  appliedCropRevision: 0, previewRevision: 'r1', ...extra,
});

const ocr = (): PageOcr => ({
  resultId: 'res', state: 'Ready', elementCount: 1, canRetry: false,
  elements: [{
    id: 'b1', kind: 'Block', text: 'Hi', confidence: 1, textType: 'Printed', readingOrder: 1, polygon: [],
    children: [{
      id: 'l1', kind: 'Line', text: 'Hi', confidence: 1, textType: 'Printed', readingOrder: 1, polygon: [],
      children: [{
        id: 'w1', kind: 'Word', text: 'Hi', confidence: 1, textType: 'Printed', readingOrder: 1,
        polygon: [{ x: .1, y: .1 }, { x: .2, y: .1 }, { x: .2, y: .2 }, { x: .1, y: .2 }], children: [],
      }],
    }],
  }],
});

describe('PageViewerComponent', () => {
  let fixture: ComponentFixture<PageViewerComponent>;
  const api = { getPagePreview: vi.fn(), getPageOcr: vi.fn() };
  const pages = [page(1), page(2), page(3)];

  beforeEach(() => {
    api.getPagePreview.mockReset().mockResolvedValue(new Blob(['x']));
    api.getPageOcr.mockReset().mockResolvedValue(ocr());
    let n = 0;
    vi.spyOn(URL, 'createObjectURL').mockImplementation(() => `blob:${++n}`);
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    TestBed.configureTestingModule({
      imports: [PageViewerComponent],
      providers: [{ provide: DocumentsApiService, useValue: api }],
    });
    fixture = TestBed.createComponent(PageViewerComponent);
    fixture.componentRef.setInput('documentId', 'd1');
    fixture.componentRef.setInput('pages', pages);
    fixture.componentRef.setInput('selectedPageId', 'p1');
  });

  const shown = () => [...fixture.nativeElement.querySelectorAll('[data-page-id]')]
    .map((e: Element) => e.getAttribute('data-page-id'));

  it('in pan mode dragging moves the pages; otherwise dragging leaves the scroll alone', () => {
    const canvas = fixture.nativeElement.querySelector('.canvas') as HTMLElement;
    const drag = () => {
      canvas.scrollLeft = 100; canvas.scrollTop = 200;
      const at = (type: string, x: number, y: number) =>
        canvas.dispatchEvent(new PointerEvent(type, { pointerId: 1, button: 0, clientX: x, clientY: y, bubbles: true }));
      at('pointerdown', 300, 300); at('pointermove', 260, 220); at('pointerup', 260, 220);
      return [canvas.scrollLeft, canvas.scrollTop];
    };
    Object.defineProperty(canvas, 'scrollLeft', { configurable: true, writable: true, value: 0 });
    Object.defineProperty(canvas, 'scrollTop', { configurable: true, writable: true, value: 0 });

    expect(drag()).toEqual([100, 200]);

    fixture.componentRef.setInput('panMode', true);
    fixture.detectChanges();
    expect(canvas.classList).toContain('pan');
    expect(drag()).toEqual([140, 280]);
  });

  it('single shows only the selected page', () => {
    fixture.componentRef.setInput('layout', 'single');
    fixture.componentRef.setInput('selectedPageId', 'p2');
    fixture.detectChanges();
    expect(shown()).toEqual(['p2']);
  });

  it('double shows the selected page and the next one (last page shows alone)', () => {
    fixture.componentRef.setInput('layout', 'double');
    fixture.componentRef.setInput('selectedPageId', 'p1');
    fixture.detectChanges();
    expect(shown()).toEqual(['p1', 'p2']);
    fixture.componentRef.setInput('selectedPageId', 'p3');
    fixture.detectChanges();
    expect(shown()).toEqual(['p3']);
  });

  it('continuous shows every page in order', () => {
    fixture.componentRef.setInput('layout', 'continuous');
    fixture.detectChanges();
    expect(shown()).toEqual(['p1', 'p2', 'p3']);
  });

  it('zoom is clamped to 25–400', () => {
    fixture.componentRef.setInput('zoom', 900);
    fixture.detectChanges();
    expect(fixture.componentInstance.effectiveZoom()).toBe(400);
    fixture.componentRef.setInput('zoom', 3);
    fixture.detectChanges();
    expect(fixture.componentInstance.effectiveZoom()).toBe(25);
  });

  it('fit width sets the zoom from the container width', () => {
    fixture.detectChanges();
    const canvas = fixture.nativeElement.querySelector('.canvas') as HTMLElement;
    Object.defineProperty(canvas, 'clientWidth', { value: 1000, configurable: true });
    expect(fixture.componentInstance.fitWidthZoom(500)).toBe(200);
  });

  it('highlights are passed to the OCR overlay of the matching page', async () => {
    fixture.componentRef.setInput('layout', 'continuous');
    fixture.componentRef.setInput('highlights', { p2: ['w1'] });
    fixture.detectChanges();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelectorAll('app-ocr-text-overlay').length).toBe(3);
    });
    fixture.detectChanges();
    const hl = fixture.nativeElement.querySelectorAll('polygon.highlighted');
    expect(hl.length).toBe(1);
    expect(fixture.nativeElement.querySelector('[data-page-id="p2"] polygon.highlighted')).toBeTruthy();
  });

  it('does not fetch previews or OCR for pages that are hidden or have no preview', async () => {
    fixture.componentRef.setInput('pages', [page(1, { hasPreview: false }), page(2), page(3)]);
    fixture.componentRef.setInput('layout', 'single');
    fixture.detectChanges();
    await fixture.whenStable();
    expect(api.getPagePreview).not.toHaveBeenCalled();
    expect(api.getPageOcr).not.toHaveBeenCalled();
  });

  it('reloads on previewRevision change and revokes old object URLs', async () => {
    fixture.componentRef.setInput('layout', 'single');
    fixture.detectChanges();
    await vi.waitFor(() => expect(api.getPagePreview).toHaveBeenCalledTimes(1));
    await fixture.whenStable();
    fixture.componentRef.setInput('pages', [page(1, { previewRevision: 'r2' }), page(2), page(3)]);
    fixture.detectChanges();
    await vi.waitFor(() => expect(api.getPagePreview).toHaveBeenCalledTimes(2));
    await vi.waitFor(() => expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:1'));
    fixture.destroy();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:2');
  });

  it('drops stale OCR when the preview revision changes', async () => {
    fixture.componentRef.setInput('layout', 'single');
    fixture.detectChanges();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('app-ocr-text-overlay')).toBeTruthy();
    });
    let resolve!: (o: PageOcr) => void;
    api.getPageOcr.mockReturnValueOnce(new Promise<PageOcr>((r) => { resolve = r; }));
    fixture.componentRef.setInput('pages', [page(1, { previewRevision: 'r2' }), page(2), page(3)]);
    fixture.detectChanges();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-ocr-text-overlay')).toBeNull();
    resolve(ocr());
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('app-ocr-text-overlay')).toBeTruthy();
    });
  });
  describe('scrolling to the selected page', () => {
    const original = Object.getOwnPropertyDescriptor(Element.prototype, 'scrollIntoView');
    afterEach(() => {
      if (original) Object.defineProperty(Element.prototype, 'scrollIntoView', original);
      else delete (Element.prototype as { scrollIntoView?: unknown }).scrollIntoView;
    });

    it('scrolls the selected page into view in the continuous layout', async () => {
      const scrolled: string[] = [];
      Object.defineProperty(Element.prototype, 'scrollIntoView', {
        configurable: true, writable: true,
        value(this: Element) { scrolled.push(this.getAttribute('data-page-id') ?? ''); },
      });
      fixture.componentRef.setInput('layout', 'continuous');
      fixture.detectChanges();
      await fixture.whenStable();
      scrolled.length = 0;

      fixture.componentRef.setInput('selectedPageId', 'p3');
      fixture.detectChanges();
      await fixture.whenStable();

      expect(scrolled).toEqual(['p3']);
    });

    it('does not scroll in single or double layouts', async () => {
      const spy = vi.fn();
      Object.defineProperty(Element.prototype, 'scrollIntoView', { configurable: true, writable: true, value: spy });
      fixture.componentRef.setInput('layout', 'single');
      fixture.detectChanges();
      fixture.componentRef.setInput('selectedPageId', 'p2');
      fixture.detectChanges();
      await fixture.whenStable();

      expect(spy).not.toHaveBeenCalled();
    });

    it('does not fail where scrollIntoView is unavailable', async () => {
      Object.defineProperty(Element.prototype, 'scrollIntoView', { configurable: true, writable: true, value: undefined });
      fixture.componentRef.setInput('layout', 'continuous');
      fixture.detectChanges();
      fixture.componentRef.setInput('selectedPageId', 'p2');
      expect(() => fixture.detectChanges()).not.toThrow();
      await fixture.whenStable();
    });
  });
});
