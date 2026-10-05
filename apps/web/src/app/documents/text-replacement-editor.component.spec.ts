import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { vi } from 'vitest';
import { TextEditService } from './text-edit.service';
import { FontCatalogueService } from './font-catalogue.service';
import { TextReplacementEditorComponent } from './text-replacement-editor.component';
import { TextStyleProposal } from './text-edit.models';

describe('TextReplacementEditorComponent', () => {
  const proposal: TextStyleProposal = {
    activeRevisionId: null, ocrResultId: 'ocr-1', wordIds: ['word-1'],
    originalText: 'Yap Tzing Yeow', box: { x: .1, y: .2, width: .3, height: .06 },
    style: { candidates: [
      { catalogueId: 'noto-serif', version: 'archive-main-regular', score: .8 },
      { catalogueId: 'noto-serif', version: 'archive-main-bold', score: .7 },
      { catalogueId: 'noto-sans', version: 'archive-main-regular', score: .5 },
    ], confidence: .4, colorHex: '#202020', fontSizePoints: 16,
    fontWeight: 400, letterSpacing: 0, baselineAngleDegrees: 0, alignment: 'left' },
  };

  async function setup(proposalError?: unknown, mode: 'replace' | 'delete' | 'add' = 'replace', historyError?: unknown,
    override: Partial<TextStyleProposal> = {}) {
    const fonts = { list: vi.fn().mockResolvedValue([
      { catalogueId: 'noto-sans', version: 'archive-main-regular', familyName: 'Noto Sans',
        category: 'SansSerif', weight: 400, webFamilyName: 'ArksScanner Noto Sans v1',
        webAssetUrl: '/assets/fonts/NotoSans-Regular.ttf', enabled: true },
      { catalogueId: 'noto-sans', version: 'archive-main-bold', familyName: 'Noto Sans',
        category: 'SansSerif', weight: 700, webFamilyName: 'ArksScanner Noto Sans v1',
        webAssetUrl: '/assets/fonts/NotoSans-Bold.ttf', enabled: true },
      { catalogueId: 'noto-serif', version: 'archive-main-regular', familyName: 'Noto Serif',
        category: 'Serif', weight: 400, webFamilyName: 'ArksScanner Noto Serif v1',
        webAssetUrl: '/assets/fonts/NotoSerif-Regular.ttf', enabled: true },
      { catalogueId: 'carlito', version: 'v1-regular', familyName: 'Carlito',
        category: 'SansSerif', weight: 400, webFamilyName: 'ArksScanner Carlito v1',
        webAssetUrl: '/assets/fonts/Carlito-Regular.ttf', enabled: true },
    ]), loadFace: vi.fn().mockResolvedValue(undefined) };
    const api = {
      propose: vi.fn().mockResolvedValue({ ...proposal, ...override }),
      history: vi.fn().mockResolvedValue({ activeRevisionId: null,
        canUndo: false, canRedo: false, entries: [] }),
      preview: vi.fn().mockResolvedValue(new Blob([new Uint8Array([1, 2, 3])], { type: 'image/png' })),
      apply: vi.fn().mockResolvedValue({ editId: 'edit-1', state: 'Queued', replayed: false }),
      get: vi.fn().mockResolvedValue({ id: 'edit-1', state: 'Succeeded', resultRevisionId: 'revision-2' }),
    };
    if (proposalError) api.propose.mockRejectedValue(proposalError);
    if (historyError) api.history.mockRejectedValue(historyError);
    TestBed.configureTestingModule({ imports: [TextReplacementEditorComponent],
      providers: [{ provide: TextEditService, useValue: api },
        { provide: FontCatalogueService, useValue: fonts }] });
    const fixture = TestBed.createComponent(TextReplacementEditorComponent);
    fixture.componentRef.setInput('documentId', 'document-1');
    fixture.componentRef.setInput('selection', {
      pageId: 'page-1', ocrResultId: mode === 'add' ? '00000000-0000-0000-0000-000000000000' : 'ocr-1',
      wordIds: mode === 'add' ? [] : ['word-1'],
      phrase: mode === 'add' ? '' : 'Yap Tzing Yeow', textType: 'Printed', polygon: [
        { x: .1, y: .2 }, { x: .4, y: .2 }, { x: .4, y: .26 }, { x: .1, y: .26 },
      ],
    });
    fixture.componentRef.setInput('imageUrl', 'blob:preview');
    fixture.componentRef.setInput('mode', mode);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, api, fonts };
  }

  it('offers an approved family outside the OCR recommendations', async () => {
    const { fixture } = await setup();
    const options = [...(fixture.nativeElement.querySelector('[aria-label="Font"]') as HTMLSelectElement).options];
    expect(options.map((option) => option.textContent?.trim())).toContain('Carlito');
  });

  it('deletes selected words through an empty-text preview', async () => {
    const { fixture, api } = await setup(undefined, 'delete');
    (fixture.nativeElement.querySelector('[data-testid="preview-edit"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(api.preview).toHaveBeenCalledWith('document-1', 'page-1',
      expect.objectContaining({ wordIds: ['word-1'], replacementText: '' }));
    expect((fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement).disabled).toBe(false);
  });

  it('adds text with no OCR words and exposes a movable transparent box', async () => {
    const { fixture, api } = await setup(undefined, 'add');
    expect(api.propose).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[data-testid="replacement-box"]')).toBeTruthy();
    const field = fixture.nativeElement.querySelector('[aria-label="Replacement text"]') as HTMLInputElement;
    field.value = 'New label';
    field.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('[data-testid="preview-edit"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(api.preview).toHaveBeenCalledWith('document-1', 'page-1',
      expect.objectContaining({ ocrResultId: '00000000-0000-0000-0000-000000000000', wordIds: [], replacementText: 'New label' }));
  });

  it('asks to sign in for add text only when the server says the user is signed out', async () => {
    const signedOut = await setup(undefined, 'add', new HttpErrorResponse({ status: 401 }));
    expect(signedOut.fixture.nativeElement.textContent).toContain('Sign in to add text on this page.');
    TestBed.resetTestingModule();
    const broken = await setup(undefined, 'add', new HttpErrorResponse({ status: 500 }));
    expect(broken.fixture.nativeElement.textContent).toContain('Could not start adding text. Please try again.');
    expect(broken.fixture.nativeElement.textContent).not.toContain('Sign in');
  });

  it('accepts typed letter spacing up to three before preview', async () => {
    const { fixture, api } = await setup(undefined, 'add');
    const text = fixture.nativeElement.querySelector('[aria-label="Replacement text"]') as HTMLInputElement;
    text.value = 'New label';
    text.dispatchEvent(new Event('input', { bubbles: true }));
    const spacing = fixture.nativeElement.querySelector('input[step="0.005"]') as HTMLInputElement;
    spacing.value = '1.55';
    spacing.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('[data-testid="preview-edit"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(api.preview).toHaveBeenCalledWith('document-1', 'page-1',
      expect.objectContaining({ style: expect.objectContaining({ letterSpacing: 1.55 }) }));
  });

  async function previewChange(fixture: ReturnType<typeof TestBed.createComponent<TextReplacementEditorComponent>>) {
    (fixture.nativeElement.querySelector('[data-testid="preview-edit"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('loads a proposal and starts with the recognized phrase', async () => {
    const { fixture, api } = await setup();
    expect(api.propose).toHaveBeenCalledWith('document-1', 'page-1', 'ocr-1', ['word-1']);
    expect(fixture.nativeElement.querySelector('[aria-label="Replacement text"]').value)
      .toBe('Yap Tzing Yeow');
    expect(fixture.nativeElement.textContent).toContain('Low confidence');
    expect(fixture.nativeElement.textContent).toContain('Noto Sans');
  });

  it('lists each font family once and uses Weight to choose its actual face', async () => {
    const { fixture, api } = await setup(undefined, 'add');
    const font = fixture.nativeElement.querySelector('select[aria-label="Font"]') as HTMLSelectElement;
    expect([...font.options].map((option) => option.textContent?.trim()))
      .toEqual(['Noto Sans', 'Carlito', 'Noto Serif']);
    expect(font.value).toBe('noto-sans');
    const weight = fixture.nativeElement.querySelector('select[aria-label="Weight"]') as HTMLSelectElement;
    weight.value = weight.options[1].value;
    weight.dispatchEvent(new Event('change', { bubbles: true }));
    const text = fixture.nativeElement.querySelector('[aria-label="Replacement text"]') as HTMLInputElement;
    text.value = 'Hello';
    text.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('[data-testid="preview-edit"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(api.preview.mock.calls[0][2].style.fontVersion).toBe('archive-main-bold');
    expect(font.value).toBe('noto-sans');
  });

  it('starts tidy: single spaces, a rounded size and an honest first status', async () => {
    const { fixture } = await setup(undefined, 'replace', undefined, {
      originalText: 'RM  1,500 ',
      style: { ...proposal.style, fontSizePoints: 35.250020027160 },
    });
    const el = fixture.nativeElement as HTMLElement;
    expect((el.querySelector('[aria-label="Replacement text"]') as HTMLInputElement).value).toBe('RM 1,500');
    const size = (el.querySelector('input[aria-label="Size in pixels"]') as HTMLInputElement).value;
    expect(size.replace(/^\d+\.?/, '').length).toBeLessThanOrEqual(1);
    expect(el.textContent).toContain('Edit the text, then select Preview exact result to check it.');
    expect(el.textContent).not.toContain('Preview ready');
  });

  it('does not warn about space before anything has changed', async () => {
    const { fixture } = await setup(undefined, 'replace', undefined, {
      box: { x: .1, y: .2, width: .01, height: .06 },
    });
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).not.toContain('check the exact fit');
    const field = el.querySelector('[aria-label="Replacement text"]') as HTMLInputElement;
    field.value = 'Yap Tzing Yeow Junior';
    field.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(el.textContent).toContain('check the exact fit');
  });

  it('shows a readable pixel font size and converts edits back to the stored ratio', async () => {
    const { fixture, api } = await setup(undefined, 'add');
    const size = fixture.nativeElement.querySelector('input[aria-label="Size in pixels"]') as HTMLInputElement;
    expect(Number(size.value)).toBeCloseTo(16.8);
    size.value = '24';
    size.dispatchEvent(new Event('input', { bubbles: true }));
    const text = fixture.nativeElement.querySelector('[aria-label="Replacement text"]') as HTMLInputElement;
    text.value = 'Hello';
    text.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('[data-testid="preview-edit"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(api.preview.mock.calls[0][2].style.fontSize).toBeCloseTo(24 / 1400);
  });

  it('requires a fresh exact preview before the change can be saved', async () => {
    const { fixture, api } = await setup();
    const apply = fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement;
    expect(apply.disabled).toBe(true);
    (fixture.nativeElement.querySelector('[data-testid="preview-edit"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(api.preview).toHaveBeenCalledOnce();
    expect(api.apply).not.toHaveBeenCalled();
    expect(apply.disabled).toBe(false);
    const field = fixture.nativeElement.querySelector('[aria-label="Replacement text"]') as HTMLInputElement;
    field.value = 'Tan BB';
    field.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(apply.disabled).toBe(true);
  });

  it('lets the user return from the exact preview to reposition the text', async () => {
    const { fixture } = await setup();
    await previewChange(fixture);
    (fixture.nativeElement.querySelector('[data-testid="adjust-placement"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-text-replacement-overlay')).not.toBeNull();
    expect((fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement).disabled)
      .toBe(true);
  });

  it('shows the specific safety reason returned with an image-preview error', async () => {
    const { fixture, api } = await setup();
    api.preview.mockRejectedValue(new HttpErrorResponse({ status: 422,
      error: new Blob([JSON.stringify({ code: 'text_edit_placement_overlap' })],
        { type: 'application/json' }) }));
    (fixture.nativeElement.querySelector('[data-testid="preview-edit"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('new letters would cover nearby text');
    expect((fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement).disabled)
      .toBe(true);
  });

  it('does not allow saving an old preview after a later preview attempt fails', async () => {
    const { fixture, api } = await setup();
    await previewChange(fixture);
    api.preview.mockRejectedValueOnce(new HttpErrorResponse({ status: 422,
      error: { code: 'text_edit_unsafe_background' } }));
    await previewChange(fixture);
    expect((fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement).disabled)
      .toBe(true);
  });

  it('zooms the image and editable overlay together for close inspection', async () => {
    const { fixture } = await setup();
    (fixture.nativeElement.querySelector('[data-testid="preview-zoom-in"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const surface = fixture.nativeElement.querySelector('.image-surface') as HTMLElement;
    expect(surface.style.width).toBe('150%');
    expect(surface.querySelector('app-text-replacement-overlay')).not.toBeNull();
  });

  it('pans the zoomed page without moving the text box', async () => {
    const { fixture } = await setup(undefined, 'add');
    (fixture.nativeElement.querySelector('[data-testid="preview-zoom-in"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const viewport = fixture.nativeElement.querySelector('.image-viewport') as HTMLElement;
    viewport.scrollLeft = 40;
    viewport.scrollTop = 50;
    const box = fixture.nativeElement.querySelector('[data-testid="replacement-box"]') as HTMLElement;
    const oldLeft = box.style.left;
    const down = new Event('pointerdown', { bubbles: true, cancelable: true });
    Object.defineProperties(down, { pointerId: { value: 1 }, button: { value: 0 },
      clientX: { value: 100 }, clientY: { value: 120 } });
    viewport.dispatchEvent(down);
    const move = new Event('pointermove', { bubbles: true, cancelable: true });
    Object.defineProperties(move, { pointerId: { value: 1 },
      clientX: { value: 80 }, clientY: { value: 100 } });
    viewport.dispatchEvent(move);
    fixture.detectChanges();
    expect(viewport.scrollLeft).toBe(60);
    expect(viewport.scrollTop).toBe(70);
    expect(box.style.left).toBe(oldLeft);
  });

  it('lets Space-drag pan even when starting over the editable text box', async () => {
    const { fixture } = await setup(undefined, 'add');
    (fixture.nativeElement.querySelector('[data-testid="preview-zoom-in"]') as HTMLButtonElement).click();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }));
    fixture.detectChanges();
    const overlay = fixture.nativeElement.querySelector('app-text-replacement-overlay') as HTMLElement;
    expect(overlay.style.pointerEvents).toBe('none');
    document.dispatchEvent(new KeyboardEvent('keyup', { key: ' ', bubbles: true }));
    fixture.detectChanges();
    expect(overlay.style.pointerEvents).not.toBe('none');
  });

  it('explains when text editing is disabled rather than showing a generic error', async () => {
    const { fixture } = await setup(new HttpErrorResponse({
      status: 503, error: { code: 'text_edit_disabled' },
    }));
    expect(fixture.nativeElement.textContent).toContain('Text editing is disabled on this server');
  });

  it('uses the bold font file when the user selects Bold weight', async () => {
    const { fixture, api } = await setup();
    (fixture.componentInstance as unknown as { updateStyle(field: string, value: number): void })
      .updateStyle('weight', 700);
    fixture.detectChanges();
    await previewChange(fixture);
    (fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(api.apply.mock.calls[0][2].style.weight).toBe(700);
    expect(api.apply.mock.calls[0][2].style.fontVersion).toBe('archive-main-bold');
  });

  it('keeps typing draft-only until Apply change is pressed', async () => {
    const { fixture, api } = await setup();
    const field = fixture.nativeElement.querySelector('[aria-label="Replacement text"]') as HTMLInputElement;
    field.value = 'Tan BB';
    field.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(api.apply).not.toHaveBeenCalled();
    await previewChange(fixture);
    (fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(api.apply).toHaveBeenCalledTimes(1);
    expect(api.apply.mock.calls[0][2].replacementText).toBe('Tan BB');
    expect(api.apply.mock.calls[0][2].wordIds).toEqual(['word-1']);
    expect(api.apply.mock.calls[0][2].style.fontSize).toBeCloseTo(16 / 1400);
  });

  it('cancels without calling apply', async () => {
    const { fixture, api } = await setup();
    const closed = vi.fn();
    fixture.componentInstance.closed.subscribe(closed);
    (fixture.nativeElement.querySelector('[data-testid="cancel-edit"]') as HTMLButtonElement).click();
    expect(closed).toHaveBeenCalledOnce();
    expect(api.apply).not.toHaveBeenCalled();
  });

  it('asks before discarding a changed draft', async () => {
    const { fixture, api } = await setup();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
    const closed = vi.fn();
    fixture.componentInstance.closed.subscribe(closed);
    const field = fixture.nativeElement.querySelector('[aria-label="Replacement text"]') as HTMLInputElement;
    field.value = 'Tan BB';
    field.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    const cancel = fixture.nativeElement.querySelector('[data-testid="cancel-edit"]') as HTMLButtonElement;
    cancel.click();
    expect(closed).not.toHaveBeenCalled();
    cancel.click();
    expect(closed).toHaveBeenCalledOnce();
    expect(api.apply).not.toHaveBeenCalled();
    expect(confirm).toHaveBeenCalledTimes(2);
  });

  it('adapts proposed font size to the loaded image without marking the draft changed', async () => {
    const { fixture } = await setup();
    const image = fixture.nativeElement.querySelector('.image-surface img') as HTMLImageElement;
    Object.defineProperties(image, { naturalWidth: { configurable: true, value: 600 },
      naturalHeight: { configurable: true, value: 800 } });
    image.dispatchEvent(new Event('load'));
    fixture.detectChanges();
    expect(fixture.componentInstance.isDirty()).toBe(false);
    expect(fixture.nativeElement.querySelector('[data-testid="apply-edit"]')).toBeTruthy();
  });

  it('adds pixel padding around the OCR box before applying an edit', async () => {
    const { fixture, api } = await setup();
    const image = fixture.nativeElement.querySelector('.image-surface img') as HTMLImageElement;
    Object.defineProperties(image, { naturalWidth: { configurable: true, value: 600 },
      naturalHeight: { configurable: true, value: 800 } });
    image.dispatchEvent(new Event('load'));
    fixture.detectChanges();
    expect(fixture.componentInstance.isDirty()).toBe(false);
    await previewChange(fixture);
    (fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    const submitted = api.apply.mock.calls[0][2].replacementBox;
    expect(submitted.x).toBeCloseTo(.1 - 2 / 600);
    expect(submitted.y).toBeCloseTo(.2 - 2 / 800);
    expect(submitted.width).toBeCloseTo(.3 + 4 / 600);
    expect(submitted.height).toBeCloseTo(.06 + 4 / 800);
  });

  it('does not pad a side into a neighboring recognized word', async () => {
    const { fixture, api } = await setup();
    fixture.componentRef.setInput('otherPolygons', [[
      { x: .097, y: .21 }, { x: .099, y: .21 },
      { x: .099, y: .25 }, { x: .097, y: .25 },
    ]]);
    fixture.detectChanges();
    const image = fixture.nativeElement.querySelector('.image-surface img') as HTMLImageElement;
    Object.defineProperties(image, { naturalWidth: { configurable: true, value: 600 },
      naturalHeight: { configurable: true, value: 800 } });
    image.dispatchEvent(new Event('load'));
    fixture.detectChanges();
    await previewChange(fixture);
    (fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    const submitted = api.apply.mock.calls[0][2].replacementBox;
    expect(submitted.x).toBe(.1);
    expect(submitted.width).toBeCloseTo(.3 + 2 / 600);
  });

  it('locks duplicate submit while the first request is pending', async () => {
    const { fixture, api } = await setup();
    let accept!: (value: { editId: string; state: string; replayed: boolean }) => void;
    api.apply.mockImplementation(() => new Promise((resolve) => { accept = resolve; }));
    await previewChange(fixture);
    const apply = fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement;
    apply.click();
    fixture.detectChanges();
    expect(apply.disabled).toBe(true);
    apply.click();
    expect(api.apply).toHaveBeenCalledTimes(1);
    accept({ editId: 'edit-1', state: 'Queued', replayed: false });
    await fixture.whenStable();
  });

  it('keeps approximate browser measurement advisory and lets the server check exact fit', async () => {
    const { fixture, api } = await setup();
    const field = fixture.nativeElement.querySelector('[aria-label="Replacement text"]') as HTMLInputElement;
    field.value = 'W'.repeat(300);
    field.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('check the exact fit');
    await previewChange(fixture);
    expect((fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement).disabled)
      .toBe(false);
    (fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(api.apply).toHaveBeenCalledOnce();
  });

  it('polls the accepted edit and emits completion only after server success', async () => {
    const { fixture, api } = await setup();
    const completed = vi.fn();
    fixture.componentInstance.completed.subscribe(completed);
    await previewChange(fixture);
    (fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement).click();
    expect(completed).not.toHaveBeenCalled();
    await fixture.whenStable();
    expect(api.get).toHaveBeenCalledWith('document-1', 'page-1', 'edit-1');
    expect(completed).toHaveBeenCalledOnce();
  });

  it('keeps the old page and permits a retry after render failure', async () => {
    const { fixture, api } = await setup();
    api.get.mockResolvedValue({ id: 'edit-1', state: 'Failed', failureCode: 'text_edit_unsafe_background' });
    const completed = vi.fn();
    fixture.componentInstance.completed.subscribe(completed);
    await previewChange(fixture);
    (fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(completed).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('previous page is unchanged');
    expect(fixture.nativeElement.textContent).toContain('could not clear the original ink');
    expect(fixture.nativeElement.textContent).not.toContain('Widen the replacement box');
    expect((fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement).disabled)
      .toBe(true);
  });

  it('explains an exact-fit rejection before a rendering job starts', async () => {
    const { fixture, api } = await setup();
    api.apply.mockRejectedValue(new HttpErrorResponse({ status: 422,
      error: { code: 'text_edit_overflow' } }));
    await previewChange(fixture);
    (fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('replacement box');
    expect(fixture.nativeElement.textContent).toContain('smaller font');
    expect(api.get).not.toHaveBeenCalled();
  });

  it('explains overflow instead of a generic render failure', async () => {
    const { fixture, api } = await setup();
    api.get.mockResolvedValue({ id: 'edit-1', state: 'Failed', failureCode: 'text_edit_overflow' });
    await previewChange(fixture);
    (fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('smaller font');
    expect(fixture.nativeElement.textContent).not.toContain('Rendering failed.');
  });

  it('explains when rendered letters would actually cover another recognized word', async () => {
    const { fixture, api } = await setup();
    api.get.mockResolvedValue({ id: 'edit-1', state: 'Failed',
      failureCode: 'text_edit_placement_overlap' });
    await previewChange(fixture);
    (fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('new letters would cover nearby text');
  });

  it('retries a failed first edit against the base revision created by the server', async () => {
    const { fixture, api } = await setup();
    api.get.mockResolvedValue({ id: 'edit-1', sourceRevisionId: 'revision-base',
      state: 'Failed', failureCode: 'text_edit_unsafe_background' });
    await previewChange(fixture);
    const apply = fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement;
    apply.click();
    await fixture.whenStable();
    fixture.detectChanges();

    await previewChange(fixture);
    apply.click();
    await fixture.whenStable();

    expect(api.apply).toHaveBeenCalledTimes(2);
    expect(api.apply.mock.calls[1][2].expectedRevisionId).toBe('revision-base');
  });

  it('warns when an unselected OCR word intersects the preview box', async () => {
    const { fixture } = await setup();
    fixture.componentRef.setInput('otherPolygons', [[
      { x: .2, y: .21 }, { x: .3, y: .21 }, { x: .3, y: .25 }, { x: .2, y: .25 },
    ]]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Other text overlaps');
  });
});
