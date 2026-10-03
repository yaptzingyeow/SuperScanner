import { TestBed } from '@angular/core/testing';
import { DEFAULT_WATERMARK, normalizeWatermark, sameWatermark } from './watermark';
import { WatermarkPanelComponent } from './watermark-panel.component';
import { WatermarkStore } from './watermark.store';

describe('watermark settings', () => {
  beforeEach(() => localStorage.clear());

  it('treats settings the server would store identically as the same watermark', () => {
    expect(sameWatermark(null, undefined)).toBe(true);
    expect(sameWatermark(DEFAULT_WATERMARK, null)).toBe(false);
    expect(sameWatermark({ ...DEFAULT_WATERMARK, text: '  COPY ' }, { ...DEFAULT_WATERMARK, text: 'COPY' })).toBe(true);
    expect(sameWatermark({ ...DEFAULT_WATERMARK, color: '#c62828' }, DEFAULT_WATERMARK)).toBe(true);
    expect(sameWatermark({ ...DEFAULT_WATERMARK, opacity: .5 }, DEFAULT_WATERMARK)).toBe(false);
  });

  it('drops bold for fonts without a bold face', () => {
    expect(normalizeWatermark({ ...DEFAULT_WATERMARK, fontId: 'caveat', bold: true }).bold).toBe(false);
  });

  it('remembers the watermark on this browser and only exports it when switched on with text', () => {
    const store = TestBed.inject(WatermarkStore);
    expect(store.active()).toBeNull();
    store.setEnabled(true);
    store.update({ text: 'CONFIDENTIAL', layout: 'Tiled' });
    expect(store.active()).toEqual(expect.objectContaining({ text: 'CONFIDENTIAL', layout: 'Tiled' }));

    TestBed.resetTestingModule();
    const reloaded = TestBed.inject(WatermarkStore);
    expect(reloaded.enabled()).toBe(true);
    expect(reloaded.settings().text).toBe('CONFIDENTIAL');
    reloaded.update({ text: '   ' });
    expect(reloaded.active()).toBeNull();
  });

  it('panel: presets fill the text, Bold is unavailable for script fonts, Once shows positions', () => {
    const fixture = TestBed.createComponent(WatermarkPanelComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-slot="watermark-editor"]')).toBeNull();

    (el.querySelector('#watermark-enabled') as HTMLInputElement).click();
    fixture.detectChanges();
    [...el.querySelectorAll<HTMLButtonElement>('.chips button')].find((b) => b.textContent!.trim() === 'DRAFT')!.click();
    fixture.detectChanges();
    expect((el.querySelector('#watermark-text') as HTMLInputElement).value).toBe('DRAFT');
    expect(el.querySelector('[data-slot="watermark-preview"]')!.textContent).toContain('DRAFT');
    expect(el.querySelector('[aria-label="Position"]')).not.toBeNull();

    const font = el.querySelector('#watermark-font') as HTMLSelectElement;
    font.value = 'dancing-script';
    font.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect((el.querySelector('button.bold') as HTMLButtonElement).disabled).toBe(true);
  });
});
