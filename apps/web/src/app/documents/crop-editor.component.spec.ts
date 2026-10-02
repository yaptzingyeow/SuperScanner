import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter, Router } from '@angular/router';
import { vi } from 'vitest';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { CropEditorComponent, CropState } from './crop-editor.component';


describe('CropEditorComponent guidance', () => {
  function createComponent(): CropEditorComponent {
    TestBed.configureTestingModule({
      imports: [CropEditorComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api' },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: { get: (name: string) => name === 'documentId' ? 'document-1' : 'page-1' },
            },
          },
        },
      ],
    });
    return TestBed.createComponent(CropEditorComponent).componentInstance;
  }

  function state(source: string, confidence: number, diagnosticsCode: string): CropState {
    return {
      revision: 1,
      appliedRevision: 0,
      status: 'NeedsCrop',
      confidence,
      source,
      modelVersion: source === 'Ai' ? 'test-model' : null,
      diagnosticsCode,
      points: null,
      filter: 'Document',
      appliedFilter: 'Document',
      rotation: 0,
    };
  }

  it('marks a high-confidence AI boundary as accurate', () => {
    const component = createComponent();
    component.applyCropState(state('Ai', .84, 'ai_high_confidence'));
    expect(component.guidance()).toBe('accurate');
  });

  it('asks for verification when AI confidence is medium', () => {
    const component = createComponent();
    component.applyCropState(state('Ai', .66, 'ai_review_recommended'));
    expect(component.guidance()).toBe('verify');
  });

  it('offers Magic scan first as the photo default', () => {
    const component = createComponent() as unknown as { filters: { id: string; label: string }[] };
    expect(component.filters[0]).toEqual(expect.objectContaining({ id: 'Magic', label: 'Magic scan' }));
  });

  it('requires manual adjustment for a full-image fallback', () => {
    const component = createComponent();
    component.applyCropState(state('FullImage', 0, 'manual_required'));
    expect(component.guidance()).toBe('manual');
  });

  it('keeps the source visible while the real grayscale result is pending', () => {
    createComponent();
    const fixture = TestBed.createComponent(CropEditorComponent);
    const component = fixture.componentInstance as unknown as {
      sourceUrl: { set(value: string): void };
      state: { set(value: CropState): void };
      selectFilter(value: string): void;
    };
    component.sourceUrl.set('blob:source');
    component.state.set(state('Manual', 1, 'manual'));
    component.selectFilter('Grayscale');
    fixture.detectChanges();
    const image = fixture.nativeElement.querySelector('.scan-result') as HTMLImageElement;
    expect(image.getAttribute('src')).toBe('blob:source');
    expect(image.style.filter).toBe('');
    expect(fixture.nativeElement.textContent).toContain('Preparing your changes');
    fixture.destroy();
  });

  it('makes clear that cropping a Ready scan is optional', () => {
    createComponent();
    const fixture = TestBed.createComponent(CropEditorComponent);
    const component = fixture.componentInstance as unknown as { state: { set(value: CropState): void } };
    component.state.set({ ...state('Manual', 1, 'manual'), status: 'Ready' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.confirm').disabled).toBe(false);
    expect(fixture.nativeElement.querySelector('.stage')).toBeNull();
  });
});

describe('CropEditorComponent rotation', () => {
  const pageUrl = '/api/documents/document-1/pages/page-1';
  const points = [{ x: .1, y: .1 }, { x: .9, y: .1 }, { x: .9, y: .9 }, { x: .1, y: .9 }];

  async function open(rotation: number) {
    Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: vi.fn(() => 'blob:source') });
    Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: vi.fn() });
    TestBed.configureTestingModule({
      imports: [CropEditorComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api' },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: (name: string) => name === 'documentId' ? 'document-1' : 'page-1' } } },
        },
      ],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(CropEditorComponent);
    fixture.detectChanges();
    http.expectOne(`${pageUrl}/crop-source`).flush(new Blob(['img']));
    await fixture.whenStable();
    const ready: CropState = {
      revision: 4, appliedRevision: 4, status: 'Ready', confidence: .9, source: 'Ai', modelVersion: 'm',
      diagnosticsCode: 'ai_high_confidence', points, filter: 'Bright', appliedFilter: 'Bright', rotation,
    };
    http.expectOne(`${pageUrl}/crop`).flush(ready);
    await fixture.whenStable();
    http.match(() => true).forEach((request) => request.flush(new Blob(['img'])));
    fixture.detectChanges();
    return { fixture, http };
  }

  function button(fixture: { nativeElement: HTMLElement }, text: string): HTMLButtonElement {
    const found = Array.from(fixture.nativeElement.querySelectorAll('button'))
      .find((b) => b.textContent!.trim() === text);
    if (!found) throw new Error(`No button named ${text}`);
    return found as HTMLButtonElement;
  }

  it('shows the current rotation and posts rotation ±90 with the current points, filter and revision', async () => {
    const { fixture, http } = await open(90);
    expect(fixture.nativeElement.textContent).toContain('Rotation: 90°');

    button(fixture, 'Rotate right').click();
    const apply = http.expectOne(`${pageUrl}/crop/apply`);
    expect(apply.request.method).toBe('POST');
    expect(apply.request.body).toEqual({ revision: 4, points, filter: 'Bright', rotation: 180 });
    apply.flush({ revision: 5, appliedRevision: 4, status: 'Processing', confidence: .9, source: 'Ai', modelVersion: 'm',
      diagnosticsCode: 'ai_high_confidence', points, filter: 'Bright', appliedFilter: 'Bright', rotation: 180 });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Rotation: 180°');
    fixture.destroy();
  });

  it('rotating left from 0 posts 270', async () => {
    const { fixture, http } = await open(0);

    button(fixture, 'Rotate left').click();

    expect(http.expectOne(`${pageUrl}/crop/apply`).request.body).toEqual({ revision: 4, points, filter: 'Bright', rotation: 270 });
    fixture.destroy();
  });
});

describe('CropEditorComponent return target', () => {
  it('returns to the import review with its uploads when opened from import', async () => {
    TestBed.configureTestingModule({
      imports: [CropEditorComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api' },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: { get: (name: string) => name === 'documentId' ? 'document-1' : 'page-1' },
              queryParamMap: { get: (name: string) => ({ returnTo: 'import', uploads: 'up-1,up-2' } as Record<string, string>)[name] ?? null },
            },
          },
        },
      ],
    });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const component = TestBed.createComponent(CropEditorComponent).componentInstance as unknown as { finish(): Promise<void> };
    await component.finish();
    expect(navigate).toHaveBeenCalledWith(['/documents', 'document-1', 'import'], { queryParams: { uploads: 'up-1,up-2', checked: 'page-1' } });
  });
});
