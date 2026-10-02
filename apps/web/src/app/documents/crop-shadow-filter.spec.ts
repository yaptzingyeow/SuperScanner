import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { CropEditorComponent, CropState } from './crop-editor.component';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

describe('Crop filter autosave', () => {
  const url = '/api/documents/doc/pages/page';
  const ready: CropState = { revision: 0, appliedRevision: 0, status: 'Ready', confidence: null,
    source: null, modelVersion: null, diagnosticsCode: null, points: null, filter: 'Original', appliedFilter: 'Original', rotation: 0 };
  let http: HttpTestingController;
  let fixture: ReturnType<typeof TestBed.createComponent<CropEditorComponent>>;
  const settle = async () => { await Promise.resolve(); await Promise.resolve(); fixture.detectChanges(); };
  const click = (label: string) => {
    const button = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find(b => b.textContent?.trim() === label)!;
    expect(button).toBeTruthy(); expect(button.disabled).toBe(false); button.click(); fixture.detectChanges();
  };
  beforeEach(async () => {
    vi.useFakeTimers();
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:test');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    TestBed.configureTestingModule({ imports: [CropEditorComponent], providers: [provideHttpClient(),
      provideHttpClientTesting(), provideRouter([]), { provide: API_BASE_URL, useValue: '/api' },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ documentId: 'doc', pageId: 'page' }) } } }] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CropEditorComponent); fixture.detectChanges();
    http.expectOne(`${url}/crop-source`).flush(new Blob()); await settle();
    http.expectOne(`${url}/crop`).flush(ready); await settle();
  });
  afterEach(() => { fixture.destroy(); http.verify(); vi.useRealTimers(); vi.restoreAllMocks(); });
  const option = (label: string) => Array.from(fixture.nativeElement.querySelectorAll('.filter-option') as NodeListOf<HTMLButtonElement>)
    .find(button => button.textContent?.includes(label));
  it('debounces look selection and saves without an extra save button', async () => {
    option('Grayscale')!.click(); fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(250);
    const request = http.expectOne(`${url}/crop/apply`);
    expect(request.request.body.filter).toBe('Grayscale');
    request.flush({ ...ready, revision: 1, status: 'Processing', filter: 'Grayscale' }); await settle();
    http.expectOne(`${url}/crop`).flush({ ...ready, revision: 1, status: 'Processing', filter: 'Grayscale' }); await settle();
    option('Bright')!.click(); fixture.detectChanges();
    http.expectNone(`${url}/crop/apply`);
  });
  it('retains the draft after a failed save and offers retry', async () => {
    option('Black & White')!.click(); fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(250);
    http.expectOne(`${url}/crop/apply`).flush({}, { status: 503, statusText: 'Unavailable' }); await settle();
    expect(fixture.nativeElement.textContent).toContain('Retry changes');
    expect(option('Black & White')!.getAttribute('aria-pressed')).toBe('true');
  });
  it('no longer offers Smart clean or Clean content', () => {
    expect(option('Smart clean')).toBeUndefined();
    expect(option('Clean content')).toBeUndefined();
    expect(fixture.nativeElement.querySelector('.strength')).toBeNull();
  });
});
