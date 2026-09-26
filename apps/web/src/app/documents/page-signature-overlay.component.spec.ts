import { TestBed } from '@angular/core/testing';
import { PageSignatureOverlayComponent } from './page-signature-overlay.component';

describe('PageSignatureOverlayComponent', () => {
  const draft = { id: 'draft', box: { x: .2, y: .3, width: .3, height: .1 }, imageAspectRatio: 3, localImageUrl: 'blob:ink' };
  async function setup() {
    await TestBed.configureTestingModule({ imports: [PageSignatureOverlayComponent] }).compileComponents();
    const fixture = TestBed.createComponent(PageSignatureOverlayComponent);
    fixture.componentRef.setInput('draft', draft); fixture.componentRef.setInput('selectedId', 'draft'); fixture.detectChanges();
    const host = fixture.nativeElement;
    Object.defineProperty(host, 'getBoundingClientRect', { value: () => ({ width: 1000, height: 1000, left: 0, top: 0 }) });
    return fixture;
  }
  function pointer(target: HTMLElement, type: string, x: number, y: number) {
    const event = new Event(type, { bubbles: true, cancelable: true });
    Object.assign(event, { pointerId: 1, button: 0, clientX: x, clientY: y }); target.dispatchEvent(event); return event;
  }
  it('moves normalized geometry without starting page pan', async () => {
    const fixture = await setup();
    let box = draft.box;
    fixture.componentInstance.boxChange.subscribe(value => { box = value.box; });
    const body = fixture.nativeElement.querySelector('[data-testid="signature-body"]');
    expect(pointer(body, 'pointerdown', 200, 300).defaultPrevented).toBe(true);
    pointer(body, 'pointermove', 300, 350); pointer(body, 'pointerup', 300, 350);
    expect(box.x).toBeCloseTo(.3); expect(box.y).toBeCloseTo(.35);
    expect(box.width).toBe(.3); expect(box.height).toBe(.1);
  });
  it('resizes with locked aspect and bounds, and exposes four touch handles', async () => {
    const fixture = await setup(); let box = draft.box;
    fixture.componentInstance.boxChange.subscribe(value => { box = value.box; });
    const handles = fixture.nativeElement.querySelectorAll('[data-corner]'); expect(handles.length).toBe(4);
    const corner = fixture.nativeElement.querySelector('[data-corner="se"]');
    pointer(corner, 'pointerdown', 500, 400); pointer(corner, 'pointermove', 900, 900); pointer(corner, 'pointerup', 900, 900);
    expect(box.width / box.height).toBeCloseTo(3); expect(box.x + box.width).toBeLessThanOrEqual(1);
    expect(box.y + box.height).toBeLessThanOrEqual(1);
  });
  it('supports keyboard move/resize and restores geometry on cancelled drag', async () => {
    const fixture = await setup(); let box = draft.box;
    fixture.componentInstance.boxChange.subscribe(value => { box = value.box; });
    const body = fixture.nativeElement.querySelector('[data-testid="signature-body"]');
    body.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    expect(box.x).toBeCloseTo(.205);
    body.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', shiftKey: true, bubbles: true }));
    expect(box.width).toBeGreaterThan(.3); expect(box.width / box.height).toBeCloseTo(3);
    pointer(body, 'pointerdown', 200, 300); pointer(body, 'pointermove', 400, 400); pointer(body, 'pointercancel', 400, 400);
    expect(box).toEqual(draft.box);
  });
});
