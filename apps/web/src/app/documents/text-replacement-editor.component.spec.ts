import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { TextEditService } from './text-edit.service';
import { TextReplacementEditorComponent } from './text-replacement-editor.component';
import { TextStyleProposal } from './text-edit.models';

describe('TextReplacementEditorComponent', () => {
  const proposal: TextStyleProposal = {
    activeRevisionId: null, ocrResultId: 'ocr-1', wordIds: ['word-1'],
    originalText: 'Yap Tzing Yeow', box: { x: .1, y: .2, width: .3, height: .06 },
    style: { candidates: [
      { catalogueId: 'noto-serif', version: 'archive-main-regular', score: .8 },
      { catalogueId: 'noto-sans', version: 'archive-main-regular', score: .5 },
    ], confidence: .4, colorHex: '#202020', fontSizePoints: 16,
    fontWeight: 400, letterSpacing: 0, baselineAngleDegrees: 0, alignment: 'left' },
  };

  async function setup() {
    const api = {
      propose: vi.fn().mockResolvedValue(proposal),
      apply: vi.fn().mockResolvedValue({ editId: 'edit-1', state: 'Queued', replayed: false }),
      get: vi.fn().mockResolvedValue({ id: 'edit-1', state: 'Succeeded', resultRevisionId: 'revision-2' }),
    };
    TestBed.configureTestingModule({ imports: [TextReplacementEditorComponent],
      providers: [{ provide: TextEditService, useValue: api }] });
    const fixture = TestBed.createComponent(TextReplacementEditorComponent);
    fixture.componentRef.setInput('documentId', 'document-1');
    fixture.componentRef.setInput('selection', {
      pageId: 'page-1', ocrResultId: 'ocr-1', wordIds: ['word-1'],
      phrase: 'Yap Tzing Yeow', textType: 'Printed', polygon: [
        { x: .1, y: .2 }, { x: .4, y: .2 }, { x: .4, y: .26 }, { x: .1, y: .26 },
      ],
    });
    fixture.componentRef.setInput('imageUrl', 'blob:preview');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, api };
  }

  it('loads a proposal and starts with the recognized phrase', async () => {
    const { fixture, api } = await setup();
    expect(api.propose).toHaveBeenCalledWith('document-1', 'page-1', 'ocr-1', ['word-1']);
    expect(fixture.nativeElement.querySelector('[aria-label="Replacement text"]').value)
      .toBe('Yap Tzing Yeow');
    expect(fixture.nativeElement.textContent).toContain('Low confidence');
    expect(fixture.nativeElement.textContent).toContain('Noto Sans');
  });

  it('keeps typing draft-only until Apply change is pressed', async () => {
    const { fixture, api } = await setup();
    const field = fixture.nativeElement.querySelector('[aria-label="Replacement text"]') as HTMLInputElement;
    field.value = 'Tan BB';
    field.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(api.apply).not.toHaveBeenCalled();
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

  it('locks duplicate submit while the first request is pending', async () => {
    const { fixture, api } = await setup();
    let accept!: (value: { editId: string; state: string; replayed: boolean }) => void;
    api.apply.mockImplementation(() => new Promise((resolve) => { accept = resolve; }));
    const apply = fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement;
    apply.click();
    fixture.detectChanges();
    expect(apply.disabled).toBe(true);
    apply.click();
    expect(api.apply).toHaveBeenCalledTimes(1);
    accept({ editId: 'edit-1', state: 'Queued', replayed: false });
    await fixture.whenStable();
  });

  it('warns and blocks Apply when the replacement cannot fit the box', async () => {
    const { fixture, api } = await setup();
    const field = fixture.nativeElement.querySelector('[aria-label="Replacement text"]') as HTMLInputElement;
    field.value = 'W'.repeat(300);
    field.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Text does not fit');
    expect((fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement).disabled)
      .toBe(true);
    expect(api.apply).not.toHaveBeenCalled();
  });

  it('polls the accepted edit and emits completion only after server success', async () => {
    const { fixture, api } = await setup();
    const completed = vi.fn();
    fixture.componentInstance.completed.subscribe(completed);
    (fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement).click();
    expect(completed).not.toHaveBeenCalled();
    await fixture.whenStable();
    expect(api.get).toHaveBeenCalledWith('document-1', 'page-1', 'edit-1');
    expect(completed).toHaveBeenCalledOnce();
  });

  it('keeps the old page and permits a retry after render failure', async () => {
    const { fixture, api } = await setup();
    api.get.mockResolvedValue({ id: 'edit-1', state: 'Failed', failureCode: 'unsafe_background' });
    const completed = vi.fn();
    fixture.componentInstance.completed.subscribe(completed);
    (fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(completed).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('previous page is unchanged');
    expect((fixture.nativeElement.querySelector('[data-testid="apply-edit"]') as HTMLButtonElement).disabled)
      .toBe(false);
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
