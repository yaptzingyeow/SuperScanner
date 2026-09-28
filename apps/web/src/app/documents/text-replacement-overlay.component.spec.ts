import { TestBed } from '@angular/core/testing';
import { TextReplacementOverlayComponent } from './text-replacement-overlay.component';

describe('TextReplacementOverlayComponent', () => {
  function setup() {
    TestBed.configureTestingModule({ imports: [TextReplacementOverlayComponent] });
    const fixture = TestBed.createComponent(TextReplacementOverlayComponent);
    fixture.componentRef.setInput('box', { x: .2, y: .3, width: .3, height: .1 });
    fixture.componentRef.setInput('text', 'Tan BB');
    fixture.componentRef.setInput('style', { fontId: 'noto-sans', fontVersion: 'archive-main-regular',
      fontSize: .04, weight: 400, colorHex: '#000000', letterSpacing: 0,
      baseline: .75, angleDegrees: 0, alignment: 0 });
    fixture.componentRef.setInput('originalPolygon', [
      { x: .2, y: .3 }, { x: .5, y: .3 }, { x: .5, y: .4 }, { x: .2, y: .4 },
    ]);
    fixture.detectChanges();
    const root = fixture.nativeElement.querySelector('[data-testid="replacement-overlay"]') as HTMLElement;
    Object.defineProperty(root, 'getBoundingClientRect', { configurable: true,
      value: () => ({ left: 100, top: 100, width: 400, height: 600 }) });
    const boxes: Array<{ x: number; y: number; width: number; height: number }> = [];
    fixture.componentInstance.boxChange.subscribe((box) => boxes.push(box));
    return { fixture, root, boxes };
  }

  function pointer(target: Element, type: string, x: number, y: number): void {
    const event = new Event(type, { bubbles: true, cancelable: true });
    Object.defineProperties(event, { clientX: { value: x }, clientY: { value: y },
      pointerId: { value: 1 } });
    target.dispatchEvent(event);
  }

  it('moves in normalized coordinates and clamps to page bounds', () => {
    const { fixture, root, boxes } = setup();
    const box = root.querySelector('[data-testid="replacement-box"]')!;
    pointer(box, 'pointerdown', 200, 310);
    pointer(root, 'pointermove', 700, 310);
    pointer(root, 'pointerup', 700, 310);
    expect(boxes.at(-1)?.x).toBe(.7);
    expect(boxes.at(-1)?.width).toBeCloseTo(.3);
    fixture.detectChanges();
  });

  it('has eight resize handles and enforces minimum size', () => {
    const { root, boxes } = setup();
    expect(root.querySelectorAll('[data-handle]')).toHaveLength(8);
    const handle = root.querySelector('[data-handle="se"]')!;
    pointer(handle, 'pointerdown', 300, 340);
    pointer(root, 'pointermove', 160, 260);
    pointer(root, 'pointerup', 160, 260);
    expect(boxes.at(-1)!.width).toBeGreaterThanOrEqual(.04);
    expect(boxes.at(-1)!.height).toBeGreaterThanOrEqual(.02);
  });

  it('supports arrow move, Shift+arrow resize, and Escape rollback', () => {
    const { root, boxes } = setup();
    const box = root.querySelector('[data-testid="replacement-box"]')!;
    box.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    expect(boxes.at(-1)?.x).toBeCloseTo(.205);
    box.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', shiftKey: true, bubbles: true }));
    expect(boxes.at(-1)?.width).toBeCloseTo(.305);
    pointer(box, 'pointerdown', 200, 310);
    pointer(root, 'pointermove', 250, 310);
    box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    expect(boxes.at(-1)?.x).toBeCloseTo(.205);
  });

  it('shows collision warning when the new box overlaps other recognized content', () => {
    const { fixture } = setup();
    fixture.componentRef.setInput('otherPolygons', [[
      { x: .35, y: .32 }, { x: .4, y: .32 }, { x: .4, y: .38 }, { x: .35, y: .38 },
    ]]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Other text overlaps');
    expect(fixture.nativeElement.textContent).toContain('Placement guide');
  });

  it('masks the original word area without changing the source image', () => {
    const { root } = setup();
    const mask = root.querySelector('[data-testid="source-mask"]') as HTMLElement;
    expect(mask).toBeTruthy();
    expect(mask.style.left).toBe('20%');
    expect(mask.style.width).toBe('30%');
  });

  it('positions guide letters at the same vertical fraction as the exact renderer', () => {
    const { root } = setup();
    const letters = root.querySelector('.replacement-text') as HTMLElement;
    expect(letters.style.top).toBe('75%');
    expect(letters.style.transform).toBe('translateY(-75%)');
  });

  it('uses the current surface size for the next drag after a responsive resize', () => {
    const { root, boxes } = setup();
    Object.defineProperty(root, 'getBoundingClientRect', { configurable: true,
      value: () => ({ left: 100, top: 100, width: 200, height: 300 }) });
    const box = root.querySelector('[data-testid="replacement-box"]')!;
    pointer(box, 'pointerdown', 150, 200);
    pointer(root, 'pointermove', 170, 200);
    pointer(root, 'pointerup', 170, 200);
    expect(boxes.at(-1)?.x).toBeCloseTo(.3);
  });
});
