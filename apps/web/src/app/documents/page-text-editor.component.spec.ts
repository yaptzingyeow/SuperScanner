import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { PlanService } from '../plans/plan.service';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { DocumentDetail, DocumentPage, PageOcr } from './document.models';
import { DocumentsApiService } from './documents-api.service';
import { PageTextEditorComponent } from './page-text-editor.component';
import { PageSignatureService } from './page-signature.service';
import { PageMarkService } from './page-mark.service';
import { SignatureCreatorComponent } from './signature-creator.component';
import { By } from '@angular/platform-browser';

describe('PageTextEditorComponent', () => {
  const page: DocumentPage = {
    id: 'page-1', position: 1, pageNumber: 1, sourceUploadId: 'upload-1',
    sourcePageIndex: 1, state: 'Ready', hasPreview: true, hasOriginal: true,
    canCrop: true, cropStatus: 'Ready', cropRevision: 0, appliedCropRevision: 0,
    previewRevision: 'preview-1', filter: 'Original', appliedFilter: 'Original',
  };
  const document: DocumentDetail = {
    id: 'document-1', title: 'Agreement', status: 'Ready', revision: 1,
    pageOrderRevision: 1, pages: [page], imports: [], latestExport: null,
  };
  const notRequested: PageOcr = {
    state: 'NotRequested', elementCount: 0, canRetry: false, elements: [],
  };
  const ready: PageOcr = {
    resultId: 'ocr-1', state: 'Ready', elementCount: 1, canRetry: false,
    elements: [{ id: 'block-1', kind: 'Block', text: 'Yap', confidence: .98,
      textType: 'Printed', readingOrder: 1, polygon: [], children: [
      { id: 'line-1', kind: 'Line', text: 'Yap', confidence: .98,
        textType: 'Printed', readingOrder: 1, polygon: [], children: [
        { id: 'word-1', kind: 'Word', text: 'Yap', confidence: .98,
          textType: 'Printed', readingOrder: 1, children: [], polygon: [
            { x: .1, y: .1 }, { x: .2, y: .1 }, { x: .2, y: .2 }, { x: .1, y: .2 },
          ] },
        ] },
      ] }],
  };

  function setup(initialOcr: PageOcr = notRequested, savedSignatures: object[] = [], savedMarks: object[] = [], tool: string | null = null,
    plans: object = { ocr: signal(null), refresh: vi.fn().mockResolvedValue(undefined) }) {
    Object.defineProperty(URL, 'createObjectURL', { configurable: true,
      value: vi.fn(() => 'blob:full-page') });
    Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: vi.fn() });
    const api = {
      getDocument: vi.fn().mockResolvedValue(document),
      getPageOcr: vi.fn().mockResolvedValue(initialOcr),
      requestPageOcr: vi.fn().mockResolvedValue({ ...initialOcr, state: 'Queued' }),
    };
    const http = { get: vi.fn(() => of(new Blob(['preview'], { type: 'image/jpeg' }))) };
    const signatures = { list: vi.fn().mockResolvedValue(savedSignatures), image: vi.fn().mockResolvedValue(new Blob(['ink'])),
      create: vi.fn().mockResolvedValue({ id: 'signature-1', pageId: 'page-1', box: { x: .2, y: .2, width: .3, height: .1 }, imageAspectRatio: 3, revision: 0, imageUrl: '/api/signature' }),
      update: vi.fn(), delete: vi.fn().mockResolvedValue(undefined) };
    const marks = { list: vi.fn().mockResolvedValue(savedMarks), create: vi.fn().mockImplementation(async (_documentId, _pageId, draft) =>
      ({ ...draft, id: 'mark-1', pageId: 'page-1', revision: 0 })), update: vi.fn(), delete: vi.fn().mockResolvedValue(undefined) };
    TestBed.configureTestingModule({
      imports: [PageTextEditorComponent],
      providers: [provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api' },
        { provide: DocumentsApiService, useValue: api },
        { provide: HttpClient, useValue: http },
        { provide: PageSignatureService, useValue: signatures },
        { provide: PageMarkService, useValue: marks },
        { provide: PlanService, useValue: plans },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: {
          get: (name: string) => name === 'documentId' ? 'document-1' : 'page-1',
        }, queryParamMap: { get: (name: string) => name === 'tool' ? tool : null } } } },
      ],
    });
    const fixture = TestBed.createComponent(PageTextEditorComponent);
    fixture.detectChanges();
    return { fixture, api, http, signatures, marks };
  }

  it('places and saves a tick without OCR, then undoes it', async () => {
    const { fixture, marks } = setup();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('[data-testid="add-mark"]')).toBeTruthy(); });
    const image = fixture.nativeElement.querySelector('.full-page-image > img') as HTMLImageElement;
    Object.defineProperty(image, 'naturalWidth', { value: 1000 });
    Object.defineProperty(image, 'naturalHeight', { value: 2000 });
    fixture.nativeElement.querySelector('[data-testid="add-mark"]').click(); fixture.detectChanges();
    const component = fixture.componentInstance as unknown as { placeMark(point: { x: number; y: number }): void };
    component.placeMark({ x: .5, y: .5 }); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="save-mark"]')).toBeTruthy();
    fixture.nativeElement.querySelector('[data-testid="save-mark"]').click();
    await vi.waitFor(() => { fixture.detectChanges(); expect(marks.create).toHaveBeenCalledOnce();
      expect(fixture.nativeElement.querySelector('[data-testid="save-mark"]')).toBeNull(); });
    expect(fixture.nativeElement.querySelector('app-page-mark-overlay svg')).toBeTruthy();
    fixture.nativeElement.querySelector('[data-testid="undo-mark"]').click();
    await vi.waitFor(() => expect(marks.delete).toHaveBeenCalledOnce());
  });

  it('duplicates a selected mark with the same style, then saves its moved copy without changing the original', async () => {
    const original = { id: 'mark-original', pageId: 'page-1', kind: 'Cross',
      box: { x: .1, y: .2, width: .03, height: .04 }, color: '#DC2626', strokeWidth: .12, revision: 2 };
    const { fixture, marks } = setup(notRequested, [], [original]);
    marks.create.mockImplementation(async (_documentId, _pageId, draft) =>
      ({ ...draft, id: `mark-copy-${marks.create.mock.calls.length}`, pageId: 'page-1', revision: 0 }));
    await vi.waitFor(() => { fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('.mark-body')).toBeTruthy(); });
    fixture.nativeElement.querySelector('.mark-body').click(); fixture.detectChanges();
    fixture.nativeElement.querySelector('[data-testid="duplicate-mark"]').click(); fixture.detectChanges();

    const copy = fixture.nativeElement.querySelector('.mark.editable') as HTMLElement;
    expect(fixture.nativeElement.querySelectorAll('.mark')).toHaveLength(2);
    expect(copy.style.left).toBe('10%');
    expect(copy.style.top).toBe('20%');
    expect(copy.querySelector('path')?.getAttribute('stroke')).toBe('#DC2626');
    expect(copy.querySelector('path')?.getAttribute('stroke-width')).toBe('12');

    const component = fixture.componentInstance as unknown as {
      changeMarkBox(change: { id: string; box: { x: number; y: number; width: number; height: number } }): void;
    };
    component.changeMarkBox({ id: 'draft', box: { x: .5, y: .6, width: .03, height: .04 } });
    fixture.detectChanges();
    fixture.nativeElement.querySelector('[data-testid="save-mark"]').click();
    await vi.waitFor(() => { fixture.detectChanges();
      expect(fixture.nativeElement.querySelectorAll('.mark')).toHaveLength(2);
      expect(fixture.nativeElement.querySelector('.mark.editable')).toBeNull(); });
    expect(marks.create).toHaveBeenCalledOnce();
    expect(marks.create.mock.calls[0][2]).toMatchObject({ kind: 'Cross', color: '#DC2626',
      strokeWidth: .12, box: { x: .5, y: .6, width: .03, height: .04 } });
    const positions = [...fixture.nativeElement.querySelectorAll('.mark')].map((mark: HTMLElement) => mark.style.left);
    expect(positions).toEqual(['10%', '50%']);

    fixture.nativeElement.querySelector('[data-testid="duplicate-mark"]').click(); fixture.detectChanges();
    component.changeMarkBox({ id: 'draft', box: { x: .7, y: .3, width: .03, height: .04 } });
    fixture.detectChanges();
    fixture.nativeElement.querySelector('[data-testid="save-mark"]').click();
    await vi.waitFor(() => { fixture.detectChanges();
      expect(fixture.nativeElement.querySelectorAll('.mark')).toHaveLength(3);
      expect(fixture.nativeElement.querySelector('.mark.editable')).toBeNull(); });
    expect(marks.create).toHaveBeenCalledTimes(2);
    expect([...fixture.nativeElement.querySelectorAll('.mark')].map((mark: HTMLElement) => mark.style.left))
      .toEqual(['10%', '50%', '70%']);
  });

  it('clears stale undo history when saved marks are reloaded', async () => {
    const { fixture, marks } = setup();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('[data-testid="add-mark"]')).toBeTruthy(); });
    const image = fixture.nativeElement.querySelector('.full-page-image > img') as HTMLImageElement;
    Object.defineProperty(image, 'naturalWidth', { value: 1000 });
    Object.defineProperty(image, 'naturalHeight', { value: 2000 });
    fixture.nativeElement.querySelector('[data-testid="add-mark"]').click();
    (fixture.componentInstance as unknown as { placeMark(point: { x: number; y: number }): void }).placeMark({ x: .5, y: .5 });
    fixture.detectChanges();
    fixture.nativeElement.querySelector('[data-testid="save-mark"]').click();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('[data-testid="undo-mark"]').disabled).toBe(false); });
    await (fixture.componentInstance as unknown as { refreshMarks(): Promise<void> }).refreshMarks();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="undo-mark"]').disabled).toBe(true);
    expect(marks.delete).not.toHaveBeenCalled();
  });

  it('reconciles an uncertain create before saving edits made to its draft', async () => {
    const { fixture, marks } = setup();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('[data-testid="add-mark"]')).toBeTruthy(); });
    const image = fixture.nativeElement.querySelector('.full-page-image > img') as HTMLImageElement;
    Object.defineProperty(image, 'naturalWidth', { value: 1000 });
    Object.defineProperty(image, 'naturalHeight', { value: 2000 });
    fixture.nativeElement.querySelector('[data-testid="add-mark"]').click();
    const component = fixture.componentInstance as unknown as {
      placeMark(point: { x: number; y: number }): void;
      changeMarkColor(color: string): void;
    };
    component.placeMark({ x: .5, y: .5 }); fixture.detectChanges();
    let firstRequest: { key: string; color: string } | undefined;
    marks.create.mockImplementation(async (_documentId, _pageId, draft, key) => {
      if (!firstRequest) {
        firstRequest = { key, color: draft.color };
        throw new Error('response lost after commit');
      }
      if (key !== firstRequest.key || draft.color !== firstRequest.color) throw { status: 409 };
      return { ...draft, id: 'mark-1', pageId: 'page-1', revision: 0 };
    });
    marks.update.mockImplementation(async (_documentId, _pageId, draft) => ({ ...draft, revision: 1 }));
    fixture.nativeElement.querySelector('[data-testid="save-mark"]').click();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('draft is still here'); });
    component.changeMarkColor('#0000FF'); fixture.detectChanges();
    fixture.nativeElement.querySelector('[data-testid="save-mark"]').click();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('[data-testid="save-mark"]')).toBeNull(); });
    expect(marks.create).toHaveBeenCalledTimes(2);
    expect(marks.update).toHaveBeenCalledOnce();
    expect(marks.update.mock.calls[0][2].color).toBe('#0000FF');
    expect(fixture.nativeElement.querySelector('app-page-mark-overlay svg')).toBeTruthy();
  });

  it('requires confirmation before a preserved draft overwrites a newer server mark', async () => {
    const saved = { id: 'mark-1', pageId: 'page-1', kind: 'Check',
      box: { x: .1, y: .2, width: .03, height: .04 }, color: '#000000', strokeWidth: .08, revision: 0 };
    const { fixture, marks } = setup(notRequested, [], [saved]);
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('.mark-body')).toBeTruthy(); });
    fixture.nativeElement.querySelector('.mark-body').click(); fixture.detectChanges();
    fixture.nativeElement.querySelector('app-page-mark-tools button').click(); fixture.detectChanges();
    const component = fixture.componentInstance as unknown as { changeMarkColor(color: string): void; refreshMarks(): Promise<void> };
    component.changeMarkColor('#0000FF'); fixture.detectChanges();
    marks.update.mockRejectedValueOnce({ status: 409 }).mockImplementation(async (_documentId, _pageId, draft) => ({ ...draft, revision: 2 }));
    fixture.nativeElement.querySelector('[data-testid="save-mark"]').click();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('changed elsewhere'); });
    const prematureConfirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    fixture.nativeElement.querySelector('[data-testid="save-mark"]').click();
    await Promise.resolve(); fixture.detectChanges();
    expect(marks.update).toHaveBeenCalledTimes(1);
    prematureConfirm.mockRestore();
    marks.list.mockResolvedValue([{ ...saved, color: '#FF0000', revision: 1 }]);
    await component.refreshMarks(); fixture.detectChanges();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
    fixture.nativeElement.querySelector('[data-testid="save-mark"]').click();
    await Promise.resolve(); fixture.detectChanges();
    expect(marks.update).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.querySelector('[data-testid="save-mark"]')).toBeTruthy();
    fixture.nativeElement.querySelector('[data-testid="save-mark"]').click();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('[data-testid="save-mark"]')).toBeNull(); });
    expect(marks.update.mock.calls[1][2]).toMatchObject({ color: '#0000FF', revision: 1 });
    confirm.mockRestore();
  });

  it('does not silently overwrite a mark changed after an uncertain create', async () => {
    const { fixture, marks } = setup();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('[data-testid="add-mark"]')).toBeTruthy(); });
    const image = fixture.nativeElement.querySelector('.full-page-image > img') as HTMLImageElement;
    Object.defineProperty(image, 'naturalWidth', { value: 1000 });
    Object.defineProperty(image, 'naturalHeight', { value: 2000 });
    fixture.nativeElement.querySelector('[data-testid="add-mark"]').click();
    const component = fixture.componentInstance as unknown as { placeMark(point: { x: number; y: number }): void; changeMarkColor(color: string): void };
    component.placeMark({ x: .5, y: .5 }); fixture.detectChanges();
    marks.create.mockRejectedValueOnce(new Error('response lost'));
    fixture.nativeElement.querySelector('[data-testid="save-mark"]').click();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('draft is still here'); });
    component.changeMarkColor('#0000FF'); fixture.detectChanges();
    marks.create.mockImplementation(async (_documentId, _pageId, draft) =>
      ({ ...draft, id: 'mark-1', pageId: 'page-1', color: '#FF0000', revision: 1 }));
    fixture.nativeElement.querySelector('[data-testid="save-mark"]').click();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('changed elsewhere'); });
    expect(marks.create).toHaveBeenCalledTimes(2);
    expect(marks.update).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[data-testid="save-mark"]')).toBeTruthy();
  });

  it('keeps an uncertain draft when its original mark was deleted elsewhere', async () => {
    const { fixture, marks } = setup();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('[data-testid="add-mark"]')).toBeTruthy(); });
    const image = fixture.nativeElement.querySelector('.full-page-image > img') as HTMLImageElement;
    Object.defineProperty(image, 'naturalWidth', { value: 1000 });
    Object.defineProperty(image, 'naturalHeight', { value: 2000 });
    fixture.nativeElement.querySelector('[data-testid="add-mark"]').click();
    (fixture.componentInstance as unknown as { placeMark(point: { x: number; y: number }): void }).placeMark({ x: .5, y: .5 });
    fixture.detectChanges();
    marks.create.mockRejectedValueOnce(new Error('response lost'));
    fixture.nativeElement.querySelector('[data-testid="save-mark"]').click();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('draft is still here'); });
    marks.create.mockImplementation(async (_documentId, _pageId, draft) =>
      ({ ...draft, id: 'mark-1', pageId: 'page-1', revision: 1, isDeleted: true }));
    fixture.nativeElement.querySelector('[data-testid="save-mark"]').click();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('removed elsewhere'); });
    expect(fixture.nativeElement.querySelector('[data-testid="save-mark"]')).toBeTruthy();
    expect(marks.update).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelectorAll('.mark-body')).toHaveLength(1);
  });

  it('opens signature creation without OCR and Cancel leaves no saved overlay', async () => {
    const { fixture, signatures } = setup();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('[data-testid="add-signature"]')).toBeTruthy(); });
    fixture.nativeElement.querySelector('[data-testid="add-signature"]').click(); fixture.detectChanges();
    const creator = fixture.debugElement.query(By.directive(SignatureCreatorComponent)).componentInstance as SignatureCreatorComponent;
    creator.cancelled.emit(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-signature-creator')).toBeNull();
    expect(signatures.create).not.toHaveBeenCalled();
  });

  it('saves a placement and keeps a conflict draft with clear recovery instructions', async () => {
    const { fixture, signatures } = setup();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('[data-testid="add-signature"]')).toBeTruthy(); });
    fixture.nativeElement.querySelector('[data-testid="add-signature"]').click(); fixture.detectChanges();
    const component = fixture.componentInstance as unknown as { signatureCreated(blob: Blob): Promise<void> };
    const image = { naturalWidth: 300, naturalHeight: 100, onload: null as (() => void) | null, onerror: null, set src(_: string) { queueMicrotask(() => this.onload?.()); } };
    const imageSpy = vi.spyOn(globalThis, 'Image').mockImplementation(function() { return image as unknown as HTMLImageElement; });
    await component.signatureCreated(new Blob(['ink'], { type: 'image/png' })); fixture.detectChanges(); imageSpy.mockRestore();
    expect(fixture.nativeElement.querySelector('[data-testid="save-signature"]')).toBeTruthy();
    signatures.create.mockRejectedValueOnce({ status: 409 });
    fixture.nativeElement.querySelector('[data-testid="save-signature"]').click();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('Your draft is still here'); });
    expect(fixture.nativeElement.querySelector('[data-testid="save-signature"]')).toBeTruthy();
    const firstKey = signatures.create.mock.calls[0][4];
    fixture.nativeElement.querySelector('[data-testid="save-signature"]').click();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('[data-testid="save-signature"]')).toBeNull(); });
    expect(signatures.create.mock.calls[1][4]).toBe(firstKey);
    expect(fixture.nativeElement.querySelector('app-page-signature-overlay img')).toBeTruthy();
  });

  it('reloads saved signatures, cancels a move, and deletes only after confirmation', async () => {
    const saved = { id: 'signature-1', pageId: 'page-1', box: { x: .2, y: .2, width: .3, height: .1 }, imageAspectRatio: 3, revision: 2, imageUrl: '/api/signature' };
    const { fixture, signatures } = setup(notRequested, [saved]);
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('[data-testid="signature-body"]')).toBeTruthy(); });
    fixture.nativeElement.querySelector('[data-testid="signature-body"]').click(); fixture.detectChanges();
    fixture.nativeElement.querySelector('[data-testid="move-signature"]').click(); fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('.signature-fields input') as HTMLInputElement;
    input.value = '40'; input.dispatchEvent(new Event('change')); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.signature').style.left).toBe('40%');
    fixture.nativeElement.querySelector('[data-testid="cancel-signature"]').click(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.signature').style.left).toBe('20%');
    expect(signatures.update).not.toHaveBeenCalled();
    const confirmation = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValue(true);
    fixture.nativeElement.querySelector('[data-testid="delete-signature"]').click(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.signature')).toBeTruthy();
    expect(signatures.delete).not.toHaveBeenCalled();
    fixture.nativeElement.querySelector('[data-testid="delete-signature"]').click();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('.signature')).toBeNull(); });
    expect(signatures.delete).toHaveBeenCalledWith('document-1', 'page-1', 'signature-1', 2);
    confirmation.mockRestore();
  });

  it('shows a large page and a manual Recognize text action when OCR is missing', async () => {
    const { fixture, api, http } = setup();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('.full-page-image img')).toBeTruthy(); });
    expect(http.get).toHaveBeenCalledWith('/api/documents/document-1/pages/page-1/preview',
      { responseType: 'blob' });
    expect(fixture.nativeElement.querySelector('.full-page-image img'), fixture.nativeElement.textContent).toBeTruthy();
    expect(fixture.nativeElement.querySelector('button[data-testid="recognize-text"]')).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('Recognize text');
    expect(api.requestPageOcr).not.toHaveBeenCalled();
  });

  it('opens Add Text without first running OCR', async () => {
    const { fixture, api } = setup();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('[data-testid="add-text"]')).toBeTruthy(); });
    (fixture.nativeElement.querySelector('[data-testid="add-text"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-text-replacement-editor')).toBeTruthy();
    expect(api.requestPageOcr).not.toHaveBeenCalled();
  });

  it('starts OCR only when the user presses Recognize text', async () => {
    const { fixture, api } = setup();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('button[data-testid="recognize-text"]')).toBeTruthy(); });
    (fixture.nativeElement.querySelector('button[data-testid="recognize-text"]') as HTMLButtonElement).click();
    await Promise.resolve();
    fixture.detectChanges();
    expect(api.requestPageOcr).toHaveBeenCalledWith('document-1', 'page-1', false);
    expect(fixture.nativeElement.textContent).toContain('Recognizing text');
  });

  it('shows OCR usage and the limit message after a 429', async () => {
    const plans = { ocr: signal({ used: 5, limit: 5 }), refresh: vi.fn().mockResolvedValue(undefined) };
    const { fixture, api } = setup(notRequested, [], [], null, plans);
    api.requestPageOcr.mockRejectedValue(new HttpErrorResponse({ status: 429,
      error: { code: 'plan_limit_reached', kind: 'ocr', limit: 5, used: 5, resetsAt: null } }));
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('button[data-testid="recognize-text"]')).toBeTruthy(); });
    expect(fixture.nativeElement.textContent).toContain('OCR today: 5 / 5');

    (fixture.nativeElement.querySelector('button[data-testid="recognize-text"]') as HTMLButtonElement).click();

    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.textContent)
      .toContain("You've used today's 5 free OCR pages — upgrade to Pro or come back tomorrow."); });
    expect(plans.refresh).toHaveBeenCalled();
  });

  it('hides usage lines when limits are unlimited', async () => {
    const { fixture } = setup();
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('button[data-testid="recognize-text"]')).toBeTruthy(); });
    expect(fixture.nativeElement.textContent).not.toContain('OCR today');
  });

  it('shows instructions and the selectable overlay for an existing Ready OCR result', async () => {
    const { fixture, api } = setup(ready);
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('app-ocr-text-overlay')).toBeTruthy(); });
    expect(fixture.nativeElement.querySelector('app-ocr-text-overlay')).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('Drag across words');
    expect(fixture.nativeElement.querySelector('button[data-testid="recognize-text"]')).toBeNull();
    expect(api.requestPageOcr).not.toHaveBeenCalled();
  });

  it('zooms the image and its text overlay as one surface', async () => {
    const { fixture } = setup(ready);
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('app-ocr-text-overlay')).toBeTruthy(); });
    (fixture.nativeElement.querySelector('button[data-testid="zoom-in"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const surface = fixture.nativeElement.querySelector('.full-page-image') as HTMLElement;
    expect(surface.style.width).toBe('150%');
    expect(surface.querySelector('app-ocr-text-overlay')).toBeTruthy();
  });

  it('pans the zoomed page and lets Space-drag start over selectable words', async () => {
    const { fixture } = setup(ready);
    await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('app-ocr-text-overlay')).toBeTruthy(); });
    (fixture.nativeElement.querySelector('[data-testid="zoom-in"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const viewport = fixture.nativeElement.querySelector('.page-scroll') as HTMLElement;
    viewport.scrollLeft = 30;
    viewport.scrollTop = 40;
    const down = new Event('pointerdown', { bubbles: true });
    Object.assign(down, { pointerId: 1, button: 0, clientX: 100, clientY: 100 });
    viewport.dispatchEvent(down);
    const move = new Event('pointermove', { bubbles: true });
    Object.assign(move, { pointerId: 1, clientX: 80, clientY: 70 });
    viewport.dispatchEvent(move);
    expect(viewport.scrollLeft).toBe(50);
    expect(viewport.scrollTop).toBe(70);
    const up = new Event('pointerup', { bubbles: true });
    Object.assign(up, { pointerId: 1 });
    viewport.dispatchEvent(up);
    globalThis.document.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', code: 'Space', bubbles: true }));
    fixture.detectChanges();
    const overlay = fixture.nativeElement.querySelector('app-ocr-text-overlay') as HTMLElement;
    expect(overlay.style.pointerEvents).toBe('none');
    globalThis.document.dispatchEvent(new KeyboardEvent('keyup', { key: ' ', code: 'Space', bubbles: true }));
    fixture.detectChanges();
    expect(overlay.style.pointerEvents).not.toBe('none');
  });

  for (const action of ['edit-selection', 'delete-selection'] as const) {
    it(`keeps ${action} clickable after zooming`, async () => {
      const { fixture } = setup(ready);
      await vi.waitFor(() => { fixture.detectChanges(); expect(fixture.nativeElement.querySelector('app-ocr-text-overlay')).toBeTruthy(); });
      (fixture.nativeElement.querySelector('[data-testid="zoom-in"]') as HTMLButtonElement).click();
      fixture.detectChanges();
      const words = fixture.nativeElement.querySelector('app-ocr-text-overlay svg') as SVGSVGElement;
      words.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
      words.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
      fixture.detectChanges();
      const button = fixture.nativeElement.querySelector(`[data-testid="${action}"]`) as HTMLButtonElement;
      expect(button).toBeTruthy();
      const down = new Event('pointerdown', { bubbles: true, cancelable: true });
      Object.assign(down, { pointerId: 1, button: 0, clientX: 100, clientY: 100 });
      button.dispatchEvent(down);
      expect(down.defaultPrevented).toBe(false);
      button.click();
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('app-text-replacement-editor')).toBeTruthy();
    });
  }

  it('tool=signature opens the signature creator on load', async () => {
    const { fixture } = setup(notRequested, [], [], 'signature');
    await vi.waitFor(() => { fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('app-signature-creator')).toBeTruthy(); });
  });

  it('tool=mark starts mark placement on load', async () => {
    const { fixture } = setup(notRequested, [], [], 'mark');
    await vi.waitFor(() => { fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('app-page-mark-tools')).toBeTruthy(); });
  });

  it('tool=add opens the add-text editor on load', async () => {
    const { fixture } = setup(notRequested, [], [], 'add');
    await vi.waitFor(() => { fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('app-text-replacement-editor')).toBeTruthy(); });
  });

  it('tool=ocr starts recognition on load', async () => {
    const { fixture, api } = setup(notRequested, [], [], 'ocr');
    await vi.waitFor(() => expect(api.requestPageOcr).toHaveBeenCalledWith('document-1', 'page-1', false));
    fixture.detectChanges();
  });

  it('back link returns to the workspace edit tab for this page', async () => {
    const { fixture } = setup();
    await vi.waitFor(() => { fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('a.back')).toBeTruthy(); });
    expect(fixture.nativeElement.querySelector('a.back').getAttribute('href'))
      .toBe('/documents/document-1?tab=edit&page=page-1');
  });
});
