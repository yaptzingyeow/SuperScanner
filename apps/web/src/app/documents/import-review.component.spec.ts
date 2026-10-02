import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { vi } from 'vitest';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { CropState, DocumentDetail, DocumentImport, DocumentPage } from './document.models';
import { DocumentsApiService } from './documents-api.service';
import { ImportReviewComponent } from './import-review.component';
import { AddPagesDialogComponent } from './add-pages-dialog.component';
import { UploadService } from './upload.service';

const full = [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 1, y: 1 }, { x: 0, y: 1 }];

function page(id: string, position: number, overrides: Partial<DocumentPage> = {}): DocumentPage {
  return {
    id, position, pageNumber: position, sourceUploadId: 'up-1', sourcePageIndex: position - 1,
    state: 'Ready', hasPreview: true, hasOriginal: true, canCrop: true, cropStatus: 'Ready',
    cropRevision: 1, appliedCropRevision: 1, previewRevision: 'crop-1', filter: 'Magic', appliedFilter: 'Magic',
    ...overrides,
  };
}

function importOf(uploadId: string, overrides: Partial<DocumentImport> = {}): DocumentImport {
  return {
    uploadId, fileName: `${uploadId}.jpg`, mediaType: 'image/jpeg', state: 'Completed',
    discoveredPageCount: 1, createdPageCount: 1, failedPageCount: 0, ...overrides,
  };
}

function doc(pages: DocumentPage[], imports: DocumentImport[] = [importOf('up-1')]): DocumentDetail {
  return { id: 'doc-1', title: 'Appointment letter', status: 'Ready', revision: 1, pageOrderRevision: 1, pages, imports };
}

function crop(overrides: Partial<CropState> = {}): CropState {
  return {
    revision: 3, appliedRevision: 3, status: 'Ready', confidence: .9, source: 'Ai', modelVersion: 'm',
    diagnosticsCode: 'ai_high_confidence', filter: 'Magic', appliedFilter: 'Magic', rotation: 0,
    points: [{ x: .1, y: .1 }, { x: .9, y: .1 }, { x: .9, y: .9 }, { x: .1, y: .9 }],
    ...overrides,
  };
}

function conflict(message: string): HttpErrorResponse {
  return new HttpErrorResponse({ status: 409, error: { message } });
}

describe('ImportReviewComponent', () => {
  let fixture: ComponentFixture<ImportReviewComponent>;
  let api: {
    getDocument: ReturnType<typeof vi.fn>;
    getCrop: ReturnType<typeof vi.fn>;
    applyCrop: ReturnType<typeof vi.fn>;
    getPagePreview: ReturnType<typeof vi.fn>;
  };
  let navigate: ReturnType<typeof vi.fn>;

  function setup(detail: DocumentDetail | DocumentDetail[], uploads: string | null = 'up-1', extraQuery: Record<string, string> = {}) {
    Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: vi.fn(() => 'blob:preview') });
    Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: vi.fn() });
    const details = Array.isArray(detail) ? detail : [detail];
    let call = 0;
    api = {
      getDocument: vi.fn(() => Promise.resolve(details[Math.min(call++, details.length - 1)])),
      getCrop: vi.fn(() => Promise.resolve(crop())),
      applyCrop: vi.fn((_d: string, _p: string, body: { revision: number; filter: string; rotation: number; points: unknown }) =>
        Promise.resolve(crop({ revision: body.revision + 1, filter: body.filter as CropState['filter'], rotation: body.rotation, status: 'Processing' }))),
      getPagePreview: vi.fn(() => Promise.resolve(new Blob(['img'], { type: 'image/jpeg' }))),
    };
    TestBed.configureTestingModule({
      imports: [ImportReviewComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: API_BASE_URL, useValue: '/api' },
        { provide: DocumentsApiService, useValue: api },
        { provide: UploadService, useValue: { items: signal([]), addFiles: vi.fn() } },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap({ documentId: 'doc-1' }),
              queryParamMap: convertToParamMap({ ...(uploads === null ? {} : { uploads }), ...extraQuery }),
            },
          },
        },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true) as unknown as ReturnType<typeof vi.fn>;
    fixture = TestBed.createComponent(ImportReviewComponent);
    fixture.detectChanges();
  }

  async function flush(): Promise<void> {
    for (let i = 0; i < 30; i++) await Promise.resolve();
    fixture.detectChanges();
  }

  function button(text: string): HTMLButtonElement {
    const found = Array.from(fixture.nativeElement.querySelectorAll('button, a') as NodeListOf<HTMLButtonElement>)
      .find((b) => b.textContent!.trim() === text || b.getAttribute('aria-label') === text);
    if (!found) throw new Error(`No control named ${text}`);
    return found;
  }

  function statusText(): string {
    return Array.from(fixture.nativeElement.querySelectorAll('[role="status"]') as NodeListOf<HTMLElement>)
      .map((el) => el.textContent).join(' ');
  }

  beforeEach(() => sessionStorage.clear());

  afterEach(() => {
    vi.useRealTimers();
  });

  it('polls the document every 1500 ms until no shown page is Importing, Detecting or Processing', async () => {
    vi.useFakeTimers();
    const other = page('px', 9, { sourceUploadId: 'other', state: 'Importing' });
    setup([
      doc([page('p1', 1, { state: 'Importing' }), other]),
      doc([page('p1', 1, { state: 'Processing', cropStatus: 'Detecting' }), other]),
      doc([page('p1', 1), other]),
    ]);
    await vi.advanceTimersByTimeAsync(0);
    expect(api.getDocument).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1499);
    expect(api.getDocument).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(api.getDocument).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(1500);
    expect(api.getDocument).toHaveBeenCalledTimes(3);
    await vi.advanceTimersByTimeAsync(6000);
    expect(api.getDocument).toHaveBeenCalledTimes(3);
  });

  it('offers the seven looks without Smart clean or Clean content', async () => {
    setup(doc([page('p1', 1)]));
    await flush();
    const labels = Array.from(fixture.nativeElement.querySelectorAll('.look') as NodeListOf<HTMLElement>)
      .map((el) => el.textContent!.trim());
    expect(labels).toEqual(['Magic scan', 'Original', 'Document', 'Bright', 'No shadow', 'Grayscale', 'Black & white']);
    expect(fixture.nativeElement.querySelector('.strengths')).toBeNull();
  });

  it('Add photos or PDF opens the add-pages dialog and the new uploads join this screen', async () => {
    setup(doc([page('p1', 1), page('p2', 2, { sourceUploadId: 'up-2' })], [importOf('up-1'), importOf('up-2')]));
    await flush();
    expect(fixture.nativeElement.querySelectorAll('.page-tile').length).toBe(1);
    button('Add photos or PDF').click();
    fixture.detectChanges();
    const dialog = fixture.debugElement.query(By.directive(AddPagesDialogComponent));
    expect(dialog).toBeTruthy();
    const calls = api.getDocument.mock.calls.length;
    (dialog.componentInstance as AddPagesDialogComponent).completed.emit(['up-2']);
    await flush();
    expect(fixture.debugElement.query(By.directive(AddPagesDialogComponent))).toBeNull();
    expect(api.getDocument.mock.calls.length).toBeGreaterThan(calls);
    expect(fixture.nativeElement.querySelectorAll('.page-tile').length).toBe(2);
    expect(navigate).toHaveBeenCalledWith([], expect.objectContaining({
      queryParams: { uploads: 'up-1,up-2' }, queryParamsHandling: 'merge', replaceUrl: true,
    }));
  });

  it('stops polling on destroy', async () => {
    vi.useFakeTimers();
    setup(doc([page('p1', 1, { state: 'Importing' })]));
    await vi.advanceTimersByTimeAsync(0);
    expect(api.getDocument).toHaveBeenCalledTimes(1);
    fixture.destroy();
    await vi.advanceTimersByTimeAsync(6000);
    expect(api.getDocument).toHaveBeenCalledTimes(1);
  });

  it('choosing a look with apply-to-all posts applyCrop for every croppable shown page with its current points, revision and rotation', async () => {
    const p2Points = [{ x: .2, y: .2 }, { x: .8, y: .2 }, { x: .8, y: .8 }, { x: .2, y: .8 }];
    setup(doc([page('p1', 1), page('p2', 2), page('p3', 3, { sourceUploadId: 'other' })]));
    api.getCrop.mockImplementation((_d: string, pageId: string) => Promise.resolve(pageId === 'p2'
      ? crop({ revision: 8, rotation: 180, points: p2Points })
      : crop({ revision: 4, rotation: 90 })));
    await flush();

    button('Bright').click();
    await flush();

    expect(api.applyCrop).toHaveBeenCalledTimes(2);
    expect(api.applyCrop).toHaveBeenCalledWith('doc-1', 'p1', { revision: 4, points: crop().points, filter: 'Bright', rotation: 90 });
    expect(api.applyCrop).toHaveBeenCalledWith('doc-1', 'p2', { revision: 8, points: p2Points, filter: 'Bright', rotation: 180 });
    expect(api.applyCrop.mock.calls.some((call) => call[1] === 'p3')).toBe(false);
  });

  it('choosing a look without apply-to-all only updates the selected page', async () => {
    setup(doc([page('p1', 1), page('p2', 2)]));
    await flush();
    const checkbox = fixture.nativeElement.querySelector('input[type="checkbox"]') as HTMLInputElement;
    expect(checkbox.checked).toBe(true);
    checkbox.click();
    await flush();

    button('Grayscale').click();
    await flush();

    expect(api.applyCrop).toHaveBeenCalledTimes(1);
    expect(api.applyCrop.mock.calls[0][1]).toBe('p1');
    expect(api.applyCrop.mock.calls[0][2]).toEqual(expect.objectContaining({ filter: 'Grayscale' }));
  });

  it('shows a loading signal over the preview while a chosen look is being applied', async () => {
    setup(doc([page('p1', 1)]));
    await flush();
    expect(statusText()).not.toContain('Applying');
    api.applyCrop.mockReturnValue(new Promise(() => undefined));

    button('Grayscale').click();
    await flush();

    expect(statusText()).toContain('Applying Grayscale…');
    expect(fixture.nativeElement.querySelector('.page-image.stale')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.page-tile .spinner')).toBeTruthy();
  });

  it('keeps the loading signal after the server accepts a look, before the page list shows it rendering', async () => {
    setup(doc([page('p1', 1)]));
    await flush();

    button('Grayscale').click();
    await flush();

    expect(api.applyCrop).toHaveBeenCalledTimes(1);
    expect(statusText()).toContain('Applying Grayscale…');
  });

  it('keeps the loading signal while the server renders the look, and clears it once the new preview arrives', async () => {
    vi.useFakeTimers();
    setup([
      doc([page('p1', 1, { state: 'Processing', cropStatus: 'Processing', cropRevision: 2, appliedCropRevision: 1 })]),
      doc([page('p1', 1, { cropRevision: 2, appliedCropRevision: 2, previewRevision: 'crop-2' })]),
    ]);
    await flush();
    expect(statusText()).toContain('Applying Magic scan…');

    await vi.advanceTimersByTimeAsync(1500);
    await flush();

    expect(statusText()).not.toContain('Applying');
    expect(fixture.nativeElement.querySelector('.spinner')).toBeNull();
  });

  it('labels a page re-rendering after a look change as Applying look…, not Finding edges…', async () => {
    setup(doc([page('p1', 1, { state: 'Processing', cropStatus: 'Processing', cropRevision: 3, appliedCropRevision: 2 })]));
    await flush();
    expect(button('Page 1, Applying look…')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.summary').textContent).toContain('applying looks…');
  });

  it('says it is finding the page edges while detection runs', async () => {
    setup(doc([page('p1', 1, { state: 'Processing', cropStatus: 'Detecting', hasPreview: false })]));
    await flush();
    expect(statusText()).toContain('Finding the page edges…');
  });

  it('serializes rotation clicks per page', async () => {
    setup(doc([page('p1', 1)]));
    await flush();
    const resolvers: Array<() => void> = [];
    api.applyCrop.mockImplementation((_d: string, _p: string, body: { revision: number; rotation: number; filter: CropState['filter'] }) =>
      new Promise((resolve) => resolvers.push(() => resolve(crop({ revision: body.revision + 1, rotation: body.rotation, filter: body.filter })))));

    const rotate = button('Rotate right');
    rotate.click(); rotate.click(); rotate.click();
    await flush();
    expect(api.applyCrop).toHaveBeenCalledTimes(1);
    resolvers[0]();
    await flush();
    expect(api.applyCrop).toHaveBeenCalledTimes(2);
    resolvers[1]();
    await flush();
    expect(api.applyCrop).toHaveBeenCalledTimes(3);
    resolvers[2]();
    await flush();

    expect(api.applyCrop.mock.calls.map((call) => [call[2].revision, call[2].rotation])).toEqual([[3, 90], [4, 180], [5, 270]]);
  });

  it('retries once after a crop revision conflict', async () => {
    setup(doc([page('p1', 1)]));
    await flush();
    api.getCrop.mockResolvedValue(crop({ revision: 7 }));
    api.applyCrop
      .mockRejectedValueOnce(conflict('This crop changed. Reload before editing.'))
      .mockResolvedValueOnce(crop({ revision: 8, filter: 'Document' }));

    button('Document').click();
    await flush();

    expect(api.applyCrop).toHaveBeenCalledTimes(2);
    expect(api.applyCrop.mock.calls[0][2]).toEqual(expect.objectContaining({ revision: 3, filter: 'Document' }));
    expect(api.applyCrop.mock.calls[1][2]).toEqual(expect.objectContaining({ revision: 7, filter: 'Document' }));
    expect(api.getCrop).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('skips pages that cannot be re-cropped and reports them', async () => {
    setup(doc([page('p1', 1, { canCrop: false }), page('p2', 2), page('p3', 3)]));
    await flush();
    api.applyCrop.mockImplementation((_d: string, pageId: string, body: { revision: number; filter: CropState['filter'] }) => pageId === 'p2'
      ? Promise.reject(conflict('This page has text edits. Undo them before changing the crop.'))
      : Promise.resolve(crop({ revision: body.revision + 1, filter: body.filter })));

    button('Bright').click();
    await flush();

    expect(api.applyCrop.mock.calls.map((call) => call[1]).sort()).toEqual(['p2', 'p3']);
    const status = statusText();
    expect(status).toContain('Page 1');
    expect(status).toContain('Page 2');
    expect(status).not.toContain('Page 3');
  });

  it('reports uploads the server rejected after navigation', async () => {
    setup(doc([page('p1', 1)], [importOf('up-1'), importOf('up-2', { state: 'Rejected', fileName: 'scan.heic' }), importOf('up-9', { state: 'Failed' })]), 'up-1,up-2');
    await flush();

    expect(statusText()).toContain('1 file could not be imported');
    expect(statusText()).toContain('scan.heic');
  });

  it('upload originals applies Original with full-image corners and rotation 0, then navigates to the workspace', async () => {
    setup(doc([page('p1', 1), page('p2', 2)]));
    api.getCrop.mockResolvedValue(crop({ revision: 5, rotation: 90, filter: 'Bright' }));
    await flush();

    button('Upload originals without changes').click();
    await flush();

    expect(api.applyCrop).toHaveBeenCalledTimes(2);
    expect(api.applyCrop).toHaveBeenCalledWith('doc-1', 'p1', { revision: 5, points: full, filter: 'Original', rotation: 0 });
    expect(api.applyCrop).toHaveBeenCalledWith('doc-1', 'p2', { revision: 5, points: full, filter: 'Original', rotation: 0 });
    expect(navigate).toHaveBeenCalledWith(['/documents', 'doc-1']);
  });

  it('confirm navigates to /documents/:id', async () => {
    setup(doc([page('p1', 1)]));
    await flush();

    button('Confirm').click();
    await flush();

    expect(navigate).toHaveBeenCalledWith(['/documents', 'doc-1']);
    expect(api.applyCrop).not.toHaveBeenCalled();
  });

  it('Adjust corners navigates to the crop editor with returnTo=import', async () => {
    setup(doc([page('p1', 1), page('p2', 2)]));
    await flush();
    button('Page 2, Ready').click();
    await flush();

    button('Adjust corners').click();
    await flush();

    expect(navigate).toHaveBeenCalledWith(['/documents', 'doc-1', 'pages', 'p2', 'crop'],
      { queryParams: { returnTo: 'import', uploads: 'up-1' } });
  });

  it('shows every page when no uploads parameter is given and flags pages that need a corner check', async () => {
    setup(doc([page('p1', 1), page('p2', 2, { sourceUploadId: 'other' })]), null);
    api.getCrop.mockImplementation((_d: string, pageId: string) => Promise.resolve(pageId === 'p2'
      ? crop({ source: 'FullImage', confidence: 0, diagnosticsCode: 'manual_required' }) : crop()));
    await flush();

    expect(fixture.nativeElement.querySelectorAll('.page-tile').length).toBe(2);
    expect(button('Page 1, Ready')).toBeTruthy();
    expect(button('Page 2, Check corners')).toBeTruthy();
    expect(api.getPagePreview).toHaveBeenCalledWith('doc-1', 'p1');
  });

  it('does not post applyCrop to a page that is still finding edges, and applies the pending look once it is ready', async () => {
    vi.useFakeTimers();
    setup([
      doc([page('p1', 1, { cropStatus: 'Detecting' }), page('p2', 2)]),
      doc([page('p1', 1, { cropStatus: 'Detecting' }), page('p2', 2)]),
      doc([page('p1', 1, { cropRevision: 6 }), page('p2', 2)]),
    ]);
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();

    button('Bright').click();
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();
    expect(api.applyCrop.mock.calls.map((call) => call[1])).toEqual(['p2']);
    expect(button('Page 1, Look will apply when ready')).toBeTruthy();

    api.getCrop.mockImplementation((_d: string, pageId: string) =>
      Promise.resolve(pageId === 'p1' ? crop({ revision: 6, rotation: 90 }) : crop()));
    await vi.advanceTimersByTimeAsync(1500);
    fixture.detectChanges();
    expect(api.applyCrop.mock.calls.filter((call) => call[1] === 'p1')).toEqual([]);

    await vi.advanceTimersByTimeAsync(1500);
    fixture.detectChanges();
    const p1Calls = api.applyCrop.mock.calls.filter((call) => call[1] === 'p1');
    expect(p1Calls).toHaveLength(1);
    expect(p1Calls[0][2]).toEqual({ revision: 6, points: crop().points, filter: 'Bright', rotation: 90 });
    expect(api.getCrop).toHaveBeenCalledWith('doc-1', 'p1');
  });

  it('a look change keeps Check corners', async () => {
    setup(doc([page('p1', 1)]));
    api.getCrop.mockResolvedValue(crop({ source: 'FullImage', confidence: 0, diagnosticsCode: 'manual_required' }));
    await flush();
    expect(button('Page 1, Check corners')).toBeTruthy();
    api.applyCrop.mockResolvedValue(crop({ source: 'Manual', confidence: 1, filter: 'Bright', revision: 4 }));

    button('Bright').click();
    await flush();

    expect(api.applyCrop).toHaveBeenCalledTimes(1);
    expect(button('Page 1, Check corners')).toBeTruthy();
  });

  it('returning from Adjust corners with checked=<id> clears that page\'s Check corners', async () => {
    sessionStorage.setItem('superscanner:import-check:doc-1', JSON.stringify({ check: ['p1', 'p2'], decided: ['p1', 'p2'] }));
    setup(doc([page('p1', 1), page('p2', 2)]), 'up-1', { checked: 'p2' });
    api.getCrop.mockResolvedValue(crop({ source: 'Manual', confidence: 1 }));
    await flush();

    expect(button('Page 1, Check corners')).toBeTruthy();
    expect(button('Page 2, Ready')).toBeTruthy();
    expect(navigate).toHaveBeenCalledWith([], expect.objectContaining({
      queryParams: { checked: null }, queryParamsHandling: 'merge', replaceUrl: true,
    }));
    expect(JSON.parse(sessionStorage.getItem('superscanner:import-check:doc-1')!).check).toEqual(['p1']);
  });

  it('upload originals reports skipped pages and stays on the screen', async () => {
    setup(doc([page('p1', 1), page('p2', 2)]));
    await flush();
    api.applyCrop.mockImplementation((_d: string, pageId: string) => pageId === 'p2'
      ? Promise.reject(conflict('This page has text edits. Undo them before changing the crop.'))
      : Promise.resolve(crop({ filter: 'Original' })));

    button('Upload originals without changes').click();
    await flush();

    expect(statusText()).toContain('Page 2');
    expect(navigate).not.toHaveBeenCalledWith(['/documents', 'doc-1']);
  });

  it('re-clicking the look every target already has does nothing', async () => {
    setup(doc([page('p1', 1), page('p2', 2)]));
    await flush();

    button('Magic scan').click();
    await flush();

    expect(api.applyCrop).not.toHaveBeenCalled();
  });

  it('keeps Ready for a high-confidence auto-enhanced page, and a later user apply (Manual, no confidence) does not flip it', async () => {
    vi.useFakeTimers();
    setup([
      doc([page('p1', 1, { state: 'Processing', cropStatus: 'Processing', cropRevision: 2, appliedCropRevision: 0 })]),
      doc([page('p1', 1, { cropRevision: 2, appliedCropRevision: 2 })]),
      doc([page('p1', 1, { state: 'Processing', cropStatus: 'Processing', cropRevision: 3, appliedCropRevision: 2 })]),
      doc([page('p1', 1, { cropRevision: 3, appliedCropRevision: 3, filter: 'Bright', appliedFilter: 'Bright' })]),
    ]);
    // Auto-enhance keeps the detection's source/confidence on the crop it applies.
    api.getCrop.mockResolvedValue(crop({ revision: 2, appliedRevision: 2, source: 'Ai', confidence: .93, diagnosticsCode: 'ai_high_confidence' }));
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();
    expect(button('Page 1, Finding edges…')).toBeTruthy();
    await vi.advanceTimersByTimeAsync(1500);
    fixture.detectChanges();
    expect(button('Page 1, Ready')).toBeTruthy();

    const manual = crop({ revision: 3, appliedRevision: 2, status: 'Processing', source: 'Manual', confidence: null,
      modelVersion: null, diagnosticsCode: null, filter: 'Bright' });
    api.applyCrop.mockResolvedValue(manual);
    api.getCrop.mockResolvedValue({ ...manual, status: 'Ready', appliedRevision: 3 });
    button('Bright').click();
    await vi.advanceTimersByTimeAsync(0);
    await vi.advanceTimersByTimeAsync(3000);
    fixture.detectChanges();

    expect(api.applyCrop).toHaveBeenCalledTimes(1);
    expect(button('Page 1, Ready')).toBeTruthy();
  });

  it('confirm waits for looks deferred on busy pages, shows that it is applying them, then navigates', async () => {
    vi.useFakeTimers();
    setup([
      doc([page('p1', 1, { cropStatus: 'Detecting' })]),
      doc([page('p1', 1, { cropStatus: 'Detecting' })]),
      doc([page('p1', 1, { cropRevision: 6 })]),
    ]);
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();
    button('Bright').click();
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();

    button('Confirm').click();
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();
    expect(button('Applying your changes…')).toBeTruthy();
    expect(navigate).not.toHaveBeenCalledWith(['/documents', 'doc-1']);

    api.getCrop.mockResolvedValue(crop({ revision: 6 }));
    await vi.advanceTimersByTimeAsync(1500);
    expect(navigate).not.toHaveBeenCalledWith(['/documents', 'doc-1']);
    await vi.advanceTimersByTimeAsync(1500);
    fixture.detectChanges();

    expect(api.applyCrop).toHaveBeenCalledWith('doc-1', 'p1', expect.objectContaining({ revision: 6, filter: 'Bright' }));
    expect(navigate).toHaveBeenCalledWith(['/documents', 'doc-1']);
  });

  it('confirm stays and reports when a deferred change fails', async () => {
    vi.useFakeTimers();
    setup([
      doc([page('p1', 1, { cropStatus: 'Detecting' })]),
      doc([page('p1', 1, { cropRevision: 6 })]),
    ]);
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();
    button('Rotate right').click();
    button('Confirm').click();
    api.applyCrop.mockRejectedValue(new HttpErrorResponse({ status: 500 }));
    await vi.advanceTimersByTimeAsync(1500);
    fixture.detectChanges();

    expect(api.applyCrop).toHaveBeenCalledTimes(1);
    expect(navigate).not.toHaveBeenCalledWith(['/documents', 'doc-1']);
    expect(fixture.nativeElement.querySelector('[role="alert"]')!.textContent).toContain('could not be updated');
    expect(button('Confirm')).toBeTruthy();
  });

  it('Close asks before leaving while changes are still waiting for a busy page', async () => {
    setup(doc([page('p1', 1, { cropStatus: 'Detecting' })]));
    await flush();
    const ask = vi.spyOn(window, 'confirm').mockReturnValue(false);
    button('Close').click();
    await flush();
    expect(ask).not.toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith(['/documents', 'doc-1']);
    navigate.mockClear();

    button('Bright').click();
    await flush();
    button('Close').click();
    await flush();
    expect(ask).toHaveBeenCalledTimes(1);
    expect(navigate).not.toHaveBeenCalledWith(['/documents', 'doc-1']);

    ask.mockReturnValue(true);
    button('Close').click();
    await flush();
    expect(navigate).toHaveBeenCalledWith(['/documents', 'doc-1']);
    ask.mockRestore();
  });

  it('upload originals defers pages still importing and navigates once they are reset to the original', async () => {
    vi.useFakeTimers();
    setup([
      doc([page('p1', 1, { state: 'Importing', canCrop: false, cropStatus: 'None' }), page('p2', 2)]),
      doc([page('p1', 1, { cropRevision: 4 }), page('p2', 2)]),
    ]);
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();

    button('Upload originals without changes').click();
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();
    expect(api.applyCrop.mock.calls.map((call) => call[1])).toEqual(['p2']);
    expect(navigate).not.toHaveBeenCalledWith(['/documents', 'doc-1']);
    expect(button('Page 1, Look will apply when ready')).toBeTruthy();

    api.getCrop.mockResolvedValue(crop({ revision: 4, rotation: 90, filter: 'Bright' }));
    await vi.advanceTimersByTimeAsync(1500);
    fixture.detectChanges();

    expect(api.applyCrop).toHaveBeenCalledWith('doc-1', 'p1', { revision: 4, points: full, filter: 'Original', rotation: 0 });
    expect(navigate).toHaveBeenCalledWith(['/documents', 'doc-1']);
  });

  it('upload originals reports pages that end up without a crop source and stays', async () => {
    vi.useFakeTimers();
    setup([
      doc([page('p1', 1, { state: 'Importing', canCrop: false, cropStatus: 'None' }), page('p2', 2), page('p3', 3, { canCrop: false })]),
      doc([page('p1', 1, { canCrop: false, cropStatus: 'None' }), page('p2', 2), page('p3', 3, { canCrop: false })]),
    ]);
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();

    button('Upload originals without changes').click();
    await vi.advanceTimersByTimeAsync(1500);
    fixture.detectChanges();

    expect(api.applyCrop.mock.calls.map((call) => call[1])).toEqual(['p2']);
    expect(statusText()).toContain('Page 1');
    expect(statusText()).toContain('Page 3');
    expect(statusText()).not.toContain('Page 2');
    expect(navigate).not.toHaveBeenCalledWith(['/documents', 'doc-1']);
  });

  it('does not apply rotations that cancel out while a page was busy', async () => {
    vi.useFakeTimers();
    setup([
      doc([page('p1', 1, { cropStatus: 'Detecting' })]),
      doc([page('p1', 1, { cropRevision: 6 })]),
    ]);
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();
    button('Rotate right').click();
    button('Rotate left').click();
    fixture.detectChanges();
    expect(button('Page 1, Finding edges…')).toBeTruthy();

    button('Confirm').click();
    await vi.advanceTimersByTimeAsync(0);
    await vi.advanceTimersByTimeAsync(1500);

    expect(api.applyCrop).not.toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith(['/documents', 'doc-1']);
  });

  it('keeps polling with backoff after a failed refresh', async () => {
    vi.useFakeTimers();
    setup(doc([page('p1', 1, { state: 'Importing' })]));
    await vi.advanceTimersByTimeAsync(0);
    api.getDocument.mockRejectedValue(new Error('offline'));

    await vi.advanceTimersByTimeAsync(1500);
    expect(api.getDocument).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(1500);
    expect(api.getDocument).toHaveBeenCalledTimes(3);
    await vi.advanceTimersByTimeAsync(2999);
    expect(api.getDocument).toHaveBeenCalledTimes(3);
    await vi.advanceTimersByTimeAsync(1);
    expect(api.getDocument).toHaveBeenCalledTimes(4);
    await vi.advanceTimersByTimeAsync(6000);
    expect(api.getDocument).toHaveBeenCalledTimes(5);
    await vi.advanceTimersByTimeAsync(10000);
    expect(api.getDocument).toHaveBeenCalledTimes(6);
    await vi.advanceTimersByTimeAsync(10000);
    expect(api.getDocument).toHaveBeenCalledTimes(7);
  });
});
