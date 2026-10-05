import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { AuthService } from '../core/auth/auth.service';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { DocumentDetail, DocumentPage } from './document.models';
import { DocumentWorkspaceComponent } from './document-workspace.component';
import { DocumentsApiService } from './documents-api.service';

@Component({ standalone: true, template: 'import page' })
class ImportStubComponent {}

const page = (id: string, position: number, extra: Partial<DocumentPage> = {}): DocumentPage => ({
  id, position, pageNumber: position, sourceUploadId: 'u1', sourcePageIndex: position - 1,
  state: 'Ready', hasPreview: false, hasOriginal: true, canCrop: true, cropStatus: 'Ready',
  cropRevision: 1, appliedCropRevision: 1, previewRevision: 'crop-1', ...extra,
});

const detail = (pages = [page('p1', 1), page('p2', 2)], status = 'Ready'): DocumentDetail => ({
  id: 'doc-1', title: 'Appointment letter', status, revision: 3, pageOrderRevision: 2,
  pages, imports: [], latestExport: null,
});

const readyOcr = (id: string, text: string) => ({
  state: 'Ready', elementCount: 1, canRetry: false,
  elements: [{ id: 'b' + id, kind: 'Block', text: '', confidence: 1, textType: 'Printed', readingOrder: 0,
    polygon: [], children: [{ id: 'l' + id, kind: 'Line', text: '', confidence: 1, textType: 'Printed',
      readingOrder: 0, polygon: [], children: [{ id, kind: 'Word', text, confidence: 1,
        textType: 'Printed', readingOrder: 0,
        polygon: [{ x: .1, y: .1 }, { x: .2, y: .1 }, { x: .2, y: .2 }, { x: .1, y: .2 }], children: [] }] }] }],
});

describe('DocumentWorkspaceComponent', () => {
  let api: Record<string, ReturnType<typeof vi.fn>>;

  async function open(url: string, doc: DocumentDetail | DocumentDetail[] = detail(), guest = false) {
    const docs = Array.isArray(doc) ? doc : [doc];
    let call = 0;
    api = {
      getDocument: vi.fn().mockImplementation(async () => docs[Math.min(call++, docs.length - 1)]),
      getPagePreview: vi.fn().mockResolvedValue(new Blob(['x'])),
      getPageOcr: vi.fn().mockResolvedValue({ state: 'NotRequested', elementCount: 0, canRetry: false, elements: [] }),
      reorderPages: vi.fn(),
      requestPageOcr: vi.fn().mockResolvedValue({ state: 'Queued', elementCount: 0, canRetry: false, elements: [] }),
      removePage: vi.fn(),
    };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'documents/:documentId', component: DocumentWorkspaceComponent },
          { path: 'documents/:documentId/import', component: ImportStubComponent },
        ]),
        { provide: API_BASE_URL, useValue: '/api' },
        { provide: DocumentsApiService, useValue: api },
        { provide: AuthService, useValue: { user$: of({ uid: 'u1', isAnonymous: guest }) } },
      ],
    });
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    await harness.fixture.whenStable();
    harness.detectChanges();
    const router = TestBed.inject(Router);
    return { harness, router, el: harness.routeNativeElement as HTMLElement };
  }

  const tabs = (el: HTMLElement) => [...el.querySelectorAll<HTMLButtonElement>('[role="tablist"] [role="tab"]')];
  const button = (el: HTMLElement, label: string) =>
    [...el.querySelectorAll<HTMLButtonElement>('button')].find((b) =>
      (b.getAttribute('aria-label') ?? b.textContent ?? '').replace(/\s+/g, ' ').trim().startsWith(label));

  afterEach(() => vi.useRealTimers());

  it('opens on View when no tab is given', async () => {
    const { el } = await open('/documents/doc-1');
    expect(tabs(el).map((t) => t.textContent?.trim())).toEqual(['View', 'Edit', 'Export']);
    expect(tabs(el).find((t) => t.getAttribute('aria-selected') === 'true')?.textContent?.trim()).toBe('View');
    expect(el.textContent).toContain('Appointment letter');
    expect(el.textContent).toContain('2 pages');
    expect(el.querySelector('app-page-rail')).not.toBeNull();
    expect(el.querySelector('app-page-viewer')).not.toBeNull();
  });

  it('tab buttons update ?tab= and keep ?page=', async () => {
    const { harness, router, el } = await open('/documents/doc-1?page=p2');
    tabs(el)[1].click();
    await harness.fixture.whenStable();
    harness.detectChanges();
    const tree = router.parseUrl(router.url);
    expect(tree.queryParams).toEqual({ page: 'p2', tab: 'edit' });
    expect(tabs(el)[1].getAttribute('aria-selected')).toBe('true');
    expect(el.querySelector('[data-slot="edit-toolbar"]')).not.toBeNull();
  });

  it('Add text, Signature and Edit text open the page editor inside the Edit tab without changing the URL', async () => {
    for (const label of ['Add text', 'Signature', 'Edit text']) {
      TestBed.resetTestingModule();
      const { harness, router, el } = await open('/documents/doc-1?tab=edit&page=p2');
      const before = router.url;
      button(el, label)!.click();
      await harness.fixture.whenStable();
      harness.detectChanges();
      expect(router.url, label).toBe(before);
      expect(el.querySelector('app-page-text-editor'), label).not.toBeNull();
      expect(el.querySelector('app-page-viewer'), label).toBeNull();
      expect(el.querySelector('app-page-rail'), label).not.toBeNull();
      expect(el.querySelector('[data-testid="done-editing"]'), label).not.toBeNull();
    }
  });

  it('asks a guest to log in instead of opening Add text, and Log in returns here afterwards', async () => {
    const { harness, router, el } = await open('/documents/doc-1?tab=edit&page=p2', detail(), true);
    const navigate = vi.spyOn(router, 'navigate');
    button(el, 'Add text')!.click();
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(el.querySelector('app-page-text-editor')).toBeNull();
    expect(el.querySelector('[data-slot="login-prompt"]')?.textContent).toContain('Log in to use Add text');

    button(el, 'Log in')!.click();
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/documents/doc-1?tab=edit&page=p2' } });
  });

  it('highlights an edit tool only while it is open', async () => {
    const { harness, el } = await open('/documents/doc-1?tab=edit&page=p1');
    const lit = () => [...el.querySelectorAll('[data-slot="edit-toolbar"] button.on')].map((b) => b.textContent!.trim());
    expect(lit()).toEqual([]);
    button(el, 'Add text')!.click();
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(lit()).toEqual(['Add text']);
    button(el, 'Done')!.click();
    harness.detectChanges();
    expect(lit()).toEqual([]);
  });

  it('Done closes the page editor and shows the pages again', async () => {
    const { harness, el } = await open('/documents/doc-1?tab=edit&page=p1');
    button(el, 'Add text')!.click();
    await harness.fixture.whenStable();
    harness.detectChanges();
    button(el, 'Done')!.click();
    harness.detectChanges();
    expect(el.querySelector('app-page-text-editor')).toBeNull();
    expect(el.querySelector('app-page-viewer')).not.toBeNull();
  });

  it('leaving the Edit tab closes the page editor', async () => {
    const { harness, el } = await open('/documents/doc-1?tab=edit&page=p1');
    button(el, 'Signature')!.click();
    await harness.fixture.whenStable();
    harness.detectChanges();
    tabs(el)[0].click();
    await harness.fixture.whenStable();
    harness.detectChanges();
    tabs(el)[1].click();
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(el.querySelector('app-page-text-editor')).toBeNull();
  });

  it('Search text is an Edit tool (it needs recognized text), not a View tool', async () => {
    const { el } = await open('/documents/doc-1?tab=edit');
    const toolbar = el.querySelector<HTMLElement>('[data-slot="edit-toolbar"]')!;
    expect(button(toolbar, 'Search text')).toBeTruthy();
  });

  it('View toolbar has Select text, Pan, Continuous, Single page, Two pages and zoom', async () => {
    const { harness, el } = await open('/documents/doc-1');
    const toolbar = el.querySelector<HTMLElement>('[role="toolbar"]')!;
    for (const label of ['Select text', 'Pan', 'Continuous', 'Single page', 'Two pages',
      'Zoom out', 'Zoom in', 'Fit width']) {
      expect(button(toolbar, label), label).toBeTruthy();
    }
    expect(toolbar.textContent).toContain('100%');
    button(toolbar, 'Zoom in')!.click();
    harness.detectChanges();
    expect(toolbar.textContent).toContain('110%');
    button(toolbar, 'Two pages')!.click();
    harness.detectChanges();
    expect(button(toolbar, 'Two pages')!.getAttribute('aria-pressed')).toBe('true');
    expect(button(toolbar, 'Continuous')!.getAttribute('aria-pressed')).toBe('false');
  });

  it('Next moves to the next hit and selects its page', async () => {
    const { harness, router, el } = await open('/documents/doc-1?tab=edit&page=p1');
    api['getPageOcr'].mockImplementation(async (_d: string, pageId: string) =>
      pageId === 'p1' ? readyOcr('w1', 'invoice') : readyOcr('w2', 'invoice'));
    button(el, 'Search text')!.click();
    harness.detectChanges();
    await harness.fixture.whenStable();
    const input = el.querySelector<HTMLInputElement>('input[aria-label="Search in document"]')!;
    input.value = 'invoice';
    input.dispatchEvent(new Event('input'));
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(el.querySelector('[data-slot="search-count"]')!.textContent).toContain('1 of 2');
    button(el, 'Next')!.click();
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(el.querySelector('[data-slot="search-count"]')!.textContent).toContain('2 of 2');
    expect(router.parseUrl(router.url).queryParams['page']).toBe('p2');
    expect(router.url).not.toContain('invoice');
  });

  async function typeQuery(harness: { fixture: { whenStable(): Promise<unknown> }; detectChanges(): void }, el: HTMLElement, q: string) {
    button(el, 'Search text')!.click();
    harness.detectChanges();
    await harness.fixture.whenStable();
    const input = el.querySelector<HTMLInputElement>('input[aria-label="Search in document"]')!;
    input.value = q;
    input.dispatchEvent(new Event('input'));
    await harness.fixture.whenStable();
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
  }
  const countText = (el: HTMLElement) => el.querySelector('[data-slot="search-count"]')!.textContent;

  it("typing a query selects the first hit's page and Next moves to the second hit", async () => {
    const { harness, router, el } = await open('/documents/doc-1?tab=edit&page=p2');
    api['getPageOcr'].mockImplementation(async (_d: string, pageId: string) =>
      readyOcr(pageId === 'p1' ? 'w1' : 'w2', 'invoice'));
    await typeQuery(harness, el, 'invoice');
    expect(countText(el)).toContain('1 of 2');
    expect(router.parseUrl(router.url).queryParams['page']).toBe('p1');
    button(el, 'Next')!.click();
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(countText(el)).toContain('2 of 2');
    expect(router.parseUrl(router.url).queryParams['page']).toBe('p2');
  });

  it('the counter stays in range when the hit list shrinks', async () => {
    const { harness, el } = await open('/documents/doc-1?tab=edit&page=p1', [detail(), detail([page('p1', 1)])]);
    api['getPageOcr'].mockImplementation(async (_d: string, pageId: string) =>
      readyOcr(pageId === 'p1' ? 'w1' : 'w2', 'invoice'));
    await typeQuery(harness, el, 'invoice');
    button(el, 'Next')!.click();
    harness.detectChanges();
    expect(countText(el)).toContain('2 of 2');
    await (harness.routeDebugElement!.componentInstance as { load(): Promise<void> }).load();
    harness.detectChanges();
    expect(countText(el)).toContain('1 of 1');
  });

  it('names pages whose text is not recognized yet', async () => {
    const { harness, el } = await open('/documents/doc-1?tab=edit');
    button(el, 'Search text')!.click();
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
    const status = el.querySelector('[data-slot="search-status"]')!.textContent!;
    expect(status).toContain('Page 1, Page 2');
    expect(status).toContain('not recognized yet');
  });

  it('recognizes unread pages only when asked from search, then searches their text', async () => {
    const { harness, el } = await open('/documents/doc-1?tab=edit');
    button(el, 'Search text')!.click();
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(api['requestPageOcr']).not.toHaveBeenCalled();

    vi.useFakeTimers({ shouldAdvanceTime: true });
    api['getPageOcr'].mockResolvedValue(readyOcr('w1', 'invoice'));
    button(el, 'Recognize 2 pages')!.click();
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(api['requestPageOcr'].mock.calls.map((call) => call[1])).toEqual(['p1', 'p2']);
    expect(el.querySelector('[data-slot="search-status"]')!.textContent).toContain('Recognizing text…');

    await vi.advanceTimersByTimeAsync(2000);
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(el.querySelector('[data-slot="search-status"]')!.textContent).not.toContain('Recognizing');
    expect(el.querySelector('[data-slot="recognize-for-search"]')).toBeNull();
  });

  it('polls while any page is processing and stops polling on destroy', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const processing = detail([page('p1', 1, { state: 'Processing' })], 'Processing');
    const { harness } = await open('/documents/doc-1', processing);
    expect(api['getDocument']).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(3000);
    expect(api['getDocument']).toHaveBeenCalledTimes(2);
    harness.fixture.destroy();
    await vi.advanceTimersByTimeAsync(9000);
    expect(api['getDocument']).toHaveBeenCalledTimes(2);
  });

  it('does not poll when every page is ready', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    await open('/documents/doc-1');
    await vi.advanceTimersByTimeAsync(9000);
    expect(api['getDocument']).toHaveBeenCalledTimes(1);
  });

  it('Export PDF switches to the Export tab', async () => {
    const { harness, router, el } = await open('/documents/doc-1?page=p1');
    button(el, 'Export PDF')!.click();
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(router.parseUrl(router.url).queryParams).toEqual({ page: 'p1', tab: 'export' });
    expect(tabs(el)[2].getAttribute('aria-selected')).toBe('true');
    expect(el.querySelector('[data-slot="export-panel"]')).not.toBeNull();
  });

  it('retries failed pages and reloads the document', async () => {
    const failed = detail([page('p1', 1, { state: 'Failed' })], 'Failed');
    const { harness, el } = await open('/documents/doc-1', [failed, detail()]);
    const http = TestBed.inject(HttpTestingController);
    button(el, 'Retry failed pages')!.click();
    http.expectOne({ method: 'POST', url: '/api/documents/doc-1/retry-preview' }).flush({});
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(api['getDocument']).toHaveBeenCalledTimes(2);
    expect(button(el, 'Retry failed pages')).toBeUndefined();
  });

  it('keeps a running export across tab switches so the re-created panel resumes polling it', async () => {
    const { harness, el } = await open('/documents/doc-1?tab=export');
    const running = {
      id: 'e9', pageLayout: 'Original' as const, state: 'Processing', documentRevision: 3, readyPageCount: 2,
      excludedPageCount: 0, searchablePageCount: 0, searchability: 'ImageOnly' as const,
      createdAt: '2026-10-01T10:00:00Z', expiresAt: '2026-10-02T10:00:00Z', isOutdated: false,
    };
    api['getExportPreview'] = vi.fn().mockResolvedValue({
      readyPageCount: 2, excludedPageCount: 0, searchablePageCount: 0, searchability: 'ImageOnly' });
    api['getExport'] = vi.fn().mockResolvedValue({ ...running, state: 'Ready' });
    const panel = harness.fixture.debugElement.query((d) => d.name === 'app-export-panel');
    panel.componentInstance.exportChange.emit(running);

    tabs(el)[0].click();
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(el.querySelector('app-export-panel')).toBeNull();
    tabs(el)[2].click();
    await harness.fixture.whenStable();
    harness.detectChanges();
    await harness.fixture.whenStable();

    const status = harness.fixture.debugElement.query((d) => d.name === 'app-export-status');
    expect(status.componentInstance.initialExport).toEqual(running);
    await vi.waitFor(() => expect(api['getExport']).toHaveBeenCalledWith('doc-1', 'e9'));
  });

  it('ignores a stale load response that finishes after a newer one', async () => {
    const { harness, el } = await open('/documents/doc-1');
    const component = harness.routeDebugElement!.componentInstance as { load(): Promise<void> };
    let finishStale!: (doc: DocumentDetail) => void;
    api['getDocument']
      .mockImplementationOnce(() => new Promise<DocumentDetail>((resolve) => { finishStale = resolve; }))
      .mockImplementationOnce(async () => detail([page('p2', 1), page('p1', 2), page('p3', 3)]));

    const stale = component.load();
    await component.load();
    finishStale(detail([page('p1', 1)]));
    await stale;
    harness.detectChanges();

    expect(el.textContent).toContain('3 pages');
  });

  it('a load started before a reorder does not undo the reorder', async () => {
    const { harness, el } = await open('/documents/doc-1');
    const component = harness.routeDebugElement!.componentInstance as { load(): Promise<void> };
    let finishStale!: (doc: DocumentDetail) => void;
    api['getDocument'].mockImplementationOnce(() => new Promise<DocumentDetail>((resolve) => { finishStale = resolve; }));

    const stale = component.load();
    const rail = harness.fixture.debugElement.query((d) => d.name === 'app-page-rail');
    rail.componentInstance.pagesChanged.emit(detail([page('p2', 1), page('p1', 2)]));
    finishStale(detail([page('p1', 1), page('p2', 2)]));
    await stale;
    harness.detectChanges();

    const order = [...el.querySelectorAll('app-page-viewer [data-page-id]')].map((e) => e.getAttribute('data-page-id'));
    expect(order).toEqual(['p2', 'p1']);
  });

  it('overlapping loads keep a single polling chain', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const processing = detail([page('p1', 1, { state: 'Processing' })], 'Processing');
    const { harness } = await open('/documents/doc-1', processing);
    const component = harness.routeDebugElement!.componentInstance as { load(): Promise<void> };
    let finishFirst!: (doc: DocumentDetail) => void;
    api['getDocument'].mockImplementationOnce(() => new Promise<DocumentDetail>((resolve) => { finishFirst = resolve; }));
    const first = component.load();
    await component.load();
    finishFirst(processing);
    await first;
    api['getDocument'].mockClear();

    await vi.advanceTimersByTimeAsync(3000);
    expect(api['getDocument']).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(3000);
    expect(api['getDocument']).toHaveBeenCalledTimes(2);
    harness.fixture.destroy();
  });

  it('sends added uploads to the import review', async () => {
    const { harness, router } = await open('/documents/doc-1');
    const rail = harness.fixture.debugElement.query((d) => d.name === 'app-page-rail');
    rail.componentInstance.pagesAdded.emit(['u7', 'u8']);
    await harness.fixture.whenStable();
    expect(router.url).toBe('/documents/doc-1/import?uploads=u7,u8');
  });
});
