import { TestBed } from '@angular/core/testing';
import { beforeEach, afterEach, vi } from 'vitest';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { SignatureCreatorComponent } from './signature-creator.component';

describe('SignatureCreatorComponent', () => {
  // jsdom has no raster canvas; these cases verify pointer/stroke state.
  // Background removal runs on the server (SignatureBackgroundTests); here only the request is checked.
  beforeEach(() => { vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(null); });
  afterEach(() => vi.restoreAllMocks());
  async function setup() {
    await TestBed.configureTestingModule({
      imports: [SignatureCreatorComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: API_BASE_URL, useValue: '/api' }],
    }).compileComponents();
    const fixture = TestBed.createComponent(SignatureCreatorComponent);
    fixture.detectChanges();
    return fixture;
  }
  async function create() {
    const fixture = await setup();
    (fixture.nativeElement.querySelector('[data-testid="draw-mode"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    return fixture;
  }
  function pointer(canvas: HTMLCanvasElement, type: string, x = 30, y = 30) {
    const event = new MouseEvent(type, { bubbles: true, clientX: x, clientY: y, button: 0 });
    Object.defineProperty(event, 'pointerId', { value: 1 });
    canvas.dispatchEvent(event);
  }
  it('sends an uploaded photo to the server and explains when no signature is found', async () => {
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:preview');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const fixture = await setup();
    const http = TestBed.inject(HttpTestingController);
    const input = fixture.nativeElement.querySelector('input[type="file"]') as HTMLInputElement;
    const photo = new File([new Uint8Array([1, 2, 3])], 'sign.jpg', { type: 'image/jpeg' });
    Object.defineProperty(input, 'files', { value: [photo] });
    input.dispatchEvent(new Event('change'));

    const request = http.expectOne({ method: 'POST', url: '/api/signatures/prepare' });
    const form = request.request.body as FormData;
    expect(form.get('strength')).toBe('0.5');
    expect(form.get('keepOriginal')).toBe('false');
    request.flush(new Blob([JSON.stringify({ code: 'signature_no_ink' })], { type: 'application/json' }),
      { status: 422, statusText: 'Unprocessable Entity' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No signature found');
    expect(fixture.nativeElement.querySelector('[data-testid="use-signature"]').disabled).toBe(true);
    http.verify();
  });
  it('blank drawing cannot be accepted; cancel leaves no stroke', async () => {
    const fixture = await create();
    const canvas = fixture.nativeElement.querySelector('canvas');
    expect(fixture.nativeElement.querySelector('[data-testid="use-signature"]').disabled).toBe(true);
    pointer(canvas, 'pointerdown'); pointer(canvas, 'pointercancel'); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="use-signature"]').disabled).toBe(true);
  });
  it('undo removes the latest stroke and clear resets all drawing', async () => {
    const fixture = await create();
    const canvas = fixture.nativeElement.querySelector('canvas');
    const draw = () => { pointer(canvas, 'pointerdown'); pointer(canvas, 'pointermove', 60, 40); pointer(canvas, 'pointerup', 60, 40); fixture.detectChanges(); };
    draw(); draw();
    expect(fixture.nativeElement.querySelector('[data-testid="use-signature"]').disabled).toBe(false);
    fixture.nativeElement.querySelector('[data-testid="undo-stroke"]').click(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="use-signature"]').disabled).toBe(false);
    fixture.nativeElement.querySelector('[data-testid="undo-stroke"]').click(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="use-signature"]').disabled).toBe(true);
    draw(); fixture.nativeElement.querySelector('[data-testid="clear-signature"]').click(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="use-signature"]').disabled).toBe(true);
  });
  it('redraws saved ink after switching input methods', async () => {
    const surfaces = new WeakMap<HTMLCanvasElement, { ink: boolean; context: CanvasRenderingContext2D }>();
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(function(this: HTMLCanvasElement) {
      let surface = surfaces.get(this);
      if (!surface) {
        const state = { ink: false, context: null as unknown as CanvasRenderingContext2D };
        state.context = { clearRect: () => { state.ink = false; }, beginPath: () => {}, moveTo: () => {}, lineTo: () => {},
          arc: () => {}, stroke: () => { state.ink = true; }, fill: () => { state.ink = true; } } as unknown as CanvasRenderingContext2D;
        surfaces.set(this, state); surface = state;
      }
      return surface.context;
    });
    const fixture = await create();
    const canvas = fixture.nativeElement.querySelector('canvas');
    pointer(canvas, 'pointerdown'); pointer(canvas, 'pointermove', 60, 40); pointer(canvas, 'pointerup', 60, 40);
    fixture.nativeElement.querySelector('nav button').click(); fixture.detectChanges();
    fixture.nativeElement.querySelector('[data-testid="draw-mode"]').click(); fixture.detectChanges();
    const restored = fixture.nativeElement.querySelector('canvas');
    restored.getContext('2d');
    expect(surfaces.get(restored)?.ink).toBe(true);
  });
});
