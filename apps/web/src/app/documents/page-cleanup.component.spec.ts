import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { PageCleanupComponent } from './page-cleanup.component';

describe('Page cleanup', () => {
  const base = '/api/documents/doc/pages/page';

  async function setup(previewRevision = 'crop-0') {
    TestBed.configureTestingModule({ imports: [PageCleanupComponent], providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: API_BASE_URL, useValue: '/api' },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: {
        get: (key: string) => key === 'documentId' ? 'doc' : 'page' } } } },
    ] });
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:page');
    const fixture = TestBed.createComponent(PageCleanupComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/documents/doc').flush({ id: 'doc', title: 'Page', status: 'Ready',
      revision: 1, pageOrderRevision: 1, imports: [], pages: [
        { id: 'page', state: 'Ready', previewRevision }] });
    await Promise.resolve();
    http.expectOne(`${base}/preview`).flush(new Blob(['image']));
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, http, component: fixture.componentInstance };
  }

  afterEach(() => { vi.restoreAllMocks(); });

  it('only sends the reviewed selection to the preview API', async () => {
    const { fixture, http, component } = await setup();
    component.tool.set('rectangle'); // the page opens on the eraser
    const stage = fixture.nativeElement.querySelector('.stage') as HTMLElement;
    vi.spyOn(stage, 'getBoundingClientRect').mockReturnValue(
      { left: 0, top: 0, width: 1000, height: 1000 } as DOMRect);
    Object.assign(stage, { setPointerCapture() {}, hasPointerCapture() { return false; } });
    const pointer = (x: number, y: number) =>
      ({ clientX: x, clientY: y, pointerId: 1, preventDefault() {} } as PointerEvent);
    component.begin(pointer(20, 200), stage);
    component.move(pointer(60, 250), stage);
    component.end(pointer(60, 250), stage);
    expect(component.boxes()).toEqual([[.02, .2, .06, .25]]);
    const preparing = component.preview();
    const request = http.expectOne(`${base}/repair/previews`);
    expect(request.request.body).toEqual({ sourceRevisionId: null, sourceCropRevision: 0,
      rectangles: [[.02, .2, .06, .25]], strokes: [] });
    request.flush({ operationId: 'preview', state: 'Queued' });
    await preparing;
    http.verify(); fixture.destroy();
  });

  it('cleans a page that already has a text edit: sends its revision as a standard Guid', async () => {
    const { fixture, http, component } = await setup('0c1d2e3f4a5b6c7d8e9f0a1b2c3d4e5f');
    component.boxes.set([[.1, .1, .12, .12]]);
    const preparing = component.preview();
    const request = http.expectOne(`${base}/repair/previews`);
    expect(request.request.body.sourceRevisionId).toBe('0c1d2e3f-4a5b-6c7d-8e9f-0a1b2c3d4e5f');
    expect(request.request.body.sourceCropRevision).toBeNull();
    request.flush({ operationId: 'preview', state: 'Queued' });
    await preparing;
    http.verify(); fixture.destroy();
  });

  it('lets the user zoom and pan before selecting without changing the page', async () => {
    const { fixture, http, component } = await setup();
    component.zoomIn();
    component.zoomIn();
    expect(component.zoom()).toBe(2);
    component.panMode.set(true);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.stage').classList.contains('panning')).toBe(true);
    expect(component.boxes()).toEqual([]);
    http.verify(); fixture.destroy();
  });

  it('erase, preview, keep erasing, preview again: every stroke goes into the one preview to apply', async () => {
    const { fixture, http, component } = await setup();
    const stage = fixture.nativeElement.querySelector('.stage') as HTMLElement;
    vi.spyOn(stage, 'getBoundingClientRect').mockReturnValue(
      { left: 0, top: 0, width: 1000, height: 1000 } as DOMRect);
    Object.assign(stage, { setPointerCapture() {}, hasPointerCapture() { return false; } });
    const pointer = (x: number, y: number) =>
      ({ clientX: x, clientY: y, pointerId: 1, button: 0, preventDefault() {} } as PointerEvent);
    const paint = (x: number, y: number) => {
      component.begin(pointer(x, y), stage);
      component.move(pointer(x + 30, y + 3), stage);
      component.end(pointer(x + 30, y + 3), stage);
    };
    paint(100, 100);
    const first = component.preview();
    http.expectOne(`${base}/repair/previews`).flush({ operationId: 'one', state: 'Queued' });
    await first;
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('[data-testid="keep-erasing"]') as HTMLButtonElement).click();
    expect(component.operation()).toBeNull();
    expect(component.strokes().length).toBe(1);
    paint(400, 500);
    const second = component.preview();
    const request = http.expectOne(`${base}/repair/previews`);
    expect(request.request.body.strokes.length).toBe(2);
    expect(request.request.body.strokes[1].points[0]).toEqual([.4, .5]);
    request.flush({ operationId: 'two', state: 'Queued' });
    await second;
    fixture.destroy();
  });

  it('lets the user brush a mark and erase that selection before preview', async () => {
    const { fixture, http, component } = await setup();
    const stage = fixture.nativeElement.querySelector('.stage') as HTMLElement;
    vi.spyOn(stage, 'getBoundingClientRect').mockReturnValue(
      { left: 0, top: 0, width: 1000, height: 1000 } as DOMRect);
    Object.assign(stage, { setPointerCapture() {}, hasPointerCapture() { return false; } });
    const pointer = (x: number, y: number) =>
      ({ clientX: x, clientY: y, pointerId: 1, preventDefault() {} } as PointerEvent);
    component.tool.set('brush');
    component.begin(pointer(20, 200), stage);
    component.move(pointer(25, 205), stage);
    component.end(pointer(25, 205), stage);
    expect(component.strokes()).toHaveLength(1);
    component.tool.set('erase');
    component.begin(pointer(25, 205), stage);
    expect(component.strokes()).toHaveLength(0);
    http.verify(); fixture.destroy();
  });

  it('keeps the brush round on a wide photo', async () => {
    const { fixture, http, component } = await setup();
    component.onImageLoad({ target: { naturalWidth: 2000, naturalHeight: 1000 } } as unknown as Event);
    component.strokes.set([{ radius: .008, points: [[.1, .3]] }]);
    fixture.detectChanges();
    const svg = fixture.nativeElement.querySelector('.brush-overlay') as SVGElement;
    expect(svg.getAttribute('viewBox')).toBe('0 0 1000 500');
    expect(svg.querySelector('circle')?.getAttribute('r')).toBe('4');
    expect(svg.querySelector('polyline')?.getAttribute('stroke-width')).toBe('8');
    http.verify(); fixture.destroy();
  });

  it('undoes the latest selection and clears the remaining draft without touching the page', async () => {
    const { fixture, http, component } = await setup();
    component.boxes.set([[.02, .2, .06, .25]]);
    component.strokes.set([{ radius: .008, points: [[.1, .3]] }]);
    fixture.detectChanges();
    const buttons = [...fixture.nativeElement.querySelectorAll('button')] as HTMLButtonElement[];
    buttons.find(button => button.textContent?.includes('Undo selection'))!.click();
    expect(component.strokes()).toHaveLength(0);
    expect(component.boxes()).toEqual([[.02, .2, .06, .25]]);
    buttons.find(button => button.textContent?.includes('Clear all'))!.click();
    expect(component.boxes()).toHaveLength(0);
    expect(component.strokes()).toHaveLength(0);
    http.verify(); fixture.destroy();
  });

  it('shows a recovery action instead of a permanent loading message when opening fails', async () => {
    TestBed.configureTestingModule({ imports: [PageCleanupComponent], providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: API_BASE_URL, useValue: '/api' },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: {
        get: (key: string) => key === 'documentId' ? 'doc' : 'page' } } } },
    ] });
    const fixture = TestBed.createComponent(PageCleanupComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/documents/doc').flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Could not open this page for cleanup');
    expect(fixture.nativeElement.textContent).not.toContain('Loading page');
    expect(fixture.nativeElement.textContent).toContain('Back to document');
    http.verify(); fixture.destroy();
  });
});
