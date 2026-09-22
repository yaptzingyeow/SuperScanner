import { ComponentFixture, TestBed } from '@angular/core/testing';
import { OcrElement, PageOcr } from './document.models';
import { OcrTextOverlayComponent } from './ocr-text-overlay.component';

describe('OcrTextOverlayComponent', () => {
  let fixture: ComponentFixture<OcrTextOverlayComponent>;

  const word = (id: string, text: string, order: number, left: number, right: number): OcrElement => ({
    id,
    kind: 'Word',
    text,
    confidence: order === 2 ? .8 : 1,
    textType: order === 2 ? 'Handwritten' : 'Printed',
    readingOrder: order,
    polygon: [
      { x: left, y: .2 }, { x: right, y: .2 }, { x: right, y: .4 }, { x: left, y: .4 },
    ],
    children: [],
  });

  const ocr = (resultId = 'r1', elements?: OcrElement[]): PageOcr => ({
    resultId,
    state: 'Ready',
    elementCount: 5,
    canRetry: false,
    elements: elements ?? [{
      id: 'b1', kind: 'Block', text: 'Yap Tzing Yeow', confidence: .9,
      textType: 'Printed', readingOrder: 1, polygon: [], children: [{
        id: 'l1', kind: 'Line', text: 'Yap Tzing Yeow', confidence: .9,
        textType: 'Printed', readingOrder: 1, polygon: [], children: [
          word('w1', 'Yap', 1, .1, .25),
          word('w2', 'Tzing', 2, .3, .5),
          word('w3', 'Yeow', 3, .55, .75),
        ],
      }],
    }],
  });

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [OcrTextOverlayComponent] });
    fixture = TestBed.createComponent(OcrTextOverlayComponent);
    fixture.componentRef.setInput('pageId', 'p1');
    fixture.componentRef.setInput('ocr', ocr());
    fixture.detectChanges();
    Object.defineProperty(svg(), 'getBoundingClientRect', {
      value: () => ({ left: 100, top: 50, width: 500, height: 250, right: 600, bottom: 300 }),
      configurable: true,
    });
  });

  it('selects several words with one drag and summarizes them in reading order', () => {
    drag(145, 95, 500, 165);

    expect(selectedIds()).toEqual(['w1', 'w2', 'w3']);
    expect(fixture.nativeElement.querySelector('.selection-summary').textContent)
      .toContain('Yap Tzing Yeow');
    expect(fixture.nativeElement.querySelector('.selection-summary').textContent)
      .toContain('3 words');
    expect(fixture.nativeElement.querySelector('.selection-summary').textContent)
      .toContain('Mixed');
  });

  it('keeps OCR reading order when dragged in reverse', () => {
    drag(500, 165, 145, 95);

    expect(selectedIds()).toEqual(['w1', 'w2', 'w3']);
    expect(fixture.nativeElement.querySelector('.selected-phrase').textContent.trim())
      .toBe('Yap Tzing Yeow');
  });

  it('uses responsive SVG bounds to select one clicked word', () => {
    drag(275, 125, 277, 126);

    expect(selectedIds()).toEqual(['w2']);
  });

  it('clears selection on empty click and Escape', () => {
    drag(145, 95, 500, 165);
    drag(590, 280, 590, 280);
    expect(selectedIds()).toEqual([]);

    drag(145, 95, 500, 165);
    svg().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(selectedIds()).toEqual([]);
  });

  it('extends and contracts a keyboard selection in reading order', () => {
    key('ArrowRight');
    expect(selectedIds()).toEqual([]);
    key('ArrowRight', true);
    expect(selectedIds()).toEqual(['w1', 'w2']);
    key('ArrowRight', true);
    expect(selectedIds()).toEqual(['w1', 'w2', 'w3']);
    key('ArrowLeft', true);
    expect(selectedIds()).toEqual(['w1', 'w2']);
  });

  it('selects the focused word with Enter and exposes accessible selected state', () => {
    key('ArrowRight');
    key('ArrowRight');
    key('Enter');

    expect(selectedIds()).toEqual(['w2']);
    expect(fixture.nativeElement.querySelector('[data-word-id="w2"]').getAttribute('aria-selected'))
      .toBe('true');
    expect(fixture.nativeElement.querySelector('[data-word-id="w1"]').getAttribute('aria-selected'))
      .toBe('false');
  });

  it('keeps keyboard focus within the first and last OCR words', () => {
    key('ArrowLeft');
    key('Enter');
    expect(selectedIds()).toEqual(['w1']);

    key('ArrowRight');
    key('ArrowRight');
    key('ArrowRight');
    key('ArrowRight');
    key('Enter');
    expect(selectedIds()).toEqual(['w3']);
  });

  it('finishes safely when pointer capture is cancelled or lost', () => {
    pointer('pointerdown', 145, 95);
    pointer('pointermove', 350, 150);
    pointer('pointercancel', 350, 150);
    pointer('pointermove', 500, 165);
    expect(selectedIds()).toEqual(['w1', 'w2']);

    pointer('pointerdown', 145, 95);
    pointer('pointermove', 500, 165);
    svg().dispatchEvent(new Event('lostpointercapture', { bubbles: true }));
    pointer('pointermove', 590, 280);
    expect(selectedIds()).toEqual(['w1', 'w2', 'w3']);
  });

  it('clears an existing selection when the page or OCR result changes', () => {
    drag(145, 95, 500, 165);
    fixture.componentRef.setInput('ocr', ocr('r2'));
    fixture.detectChanges();
    expect(selectedIds()).toEqual([]);

    drag(145, 95, 500, 165);
    fixture.componentRef.setInput('pageId', 'p2');
    fixture.detectChanges();
    expect(selectedIds()).toEqual([]);
  });

  it('skips invalid words and presents a safe empty state when none remain', () => {
    fixture.componentRef.setInput('ocr', ocr('empty', [{
      ...word('bad', 'Secret', 1, .1, .2),
      polygon: [{ x: .1, y: .1 }],
    }]));
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No selectable text detected');
    expect(fixture.nativeElement.querySelector('svg')).toBeNull();
  });

  function svg(): SVGSVGElement {
    return fixture.nativeElement.querySelector('svg');
  }

  function selectedIds(): string[] {
    fixture.detectChanges();
    return [...fixture.nativeElement.querySelectorAll('polygon.selected')]
      .map((element: Element) => element.getAttribute('data-word-id') ?? '');
  }

  function drag(startX: number, startY: number, endX: number, endY: number): void {
    pointer('pointerdown', startX, startY);
    pointer('pointermove', endX, endY);
    pointer('pointerup', endX, endY);
  }

  function pointer(type: string, clientX: number, clientY: number): void {
    const event = new Event(type, { bubbles: true, cancelable: true });
    Object.defineProperties(event, {
      clientX: { value: clientX }, clientY: { value: clientY }, pointerId: { value: 7 },
    });
    svg().dispatchEvent(event);
    fixture.detectChanges();
  }

  function key(value: string, shiftKey = false): void {
    svg().dispatchEvent(new KeyboardEvent('keydown', {
      key: value,
      shiftKey,
      bubbles: true,
      cancelable: true,
    }));
    fixture.detectChanges();
  }
});
