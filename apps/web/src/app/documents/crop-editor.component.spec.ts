import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
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

  it('requires manual adjustment for a full-image fallback', () => {
    const component = createComponent();
    component.applyCropState(state('FullImage', 0, 'manual_required'));
    expect(component.guidance()).toBe('manual');
  });
});
