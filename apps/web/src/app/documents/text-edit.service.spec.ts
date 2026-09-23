import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { TextEditService } from './text-edit.service';

describe('TextEditService', () => {
  it('sends selected word IDs to the owner-scoped style proposal route', async () => {
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(), provideHttpClientTesting(),
      { provide: API_BASE_URL, useValue: '/api' },
    ] });
    const service = TestBed.inject(TextEditService);
    const http = TestBed.inject(HttpTestingController);
    const pending = service.propose('doc-1', 'page-1', 'ocr-1', ['word-1']);
    const request = http.expectOne('/api/documents/doc-1/pages/page-1/text-edits/style-proposal');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ ocrResultId: 'ocr-1', wordIds: ['word-1'] });
    request.flush({ activeRevisionId: null, ocrResultId: 'ocr-1', wordIds: ['word-1'],
      originalText: 'Yap', box: { x: .1, y: .2, width: .2, height: .1 },
      style: { candidates: [], confidence: .5, colorHex: '#000000', fontSizePoints: 12,
        fontWeight: 400, letterSpacing: 0, baselineAngleDegrees: 0, alignment: 'left' } });
    expect((await pending).originalText).toBe('Yap');
    http.verify();
  });

  it('posts one explicit apply request and reads its status by ID', async () => {
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(), provideHttpClientTesting(),
      { provide: API_BASE_URL, useValue: '/api' },
    ] });
    const service = TestBed.inject(TextEditService);
    const http = TestBed.inject(HttpTestingController);
    const payload = { ocrResultId: 'ocr-1', expectedRevisionId: null, wordIds: ['word-1'],
      replacementText: 'Tan BB', replacementBox: { x: .1, y: .2, width: .3, height: .1 },
      style: { fontId: 'noto-sans', fontVersion: 'archive-main-regular', fontSize: .04,
        weight: 400, colorHex: '#000000', letterSpacing: 0, baseline: .25,
        angleDegrees: 0, alignment: 0 }, idempotencyKey: 'key-1' };
    const applied = service.apply('doc-1', 'page-1', payload);
    const applyRequest = http.expectOne('/api/documents/doc-1/pages/page-1/text-edits');
    expect(applyRequest.request.method).toBe('POST');
    expect(applyRequest.request.body).toEqual(payload);
    applyRequest.flush({ editId: 'edit-1', state: 'Queued', replayed: false });
    expect((await applied).editId).toBe('edit-1');
    const status = service.get('doc-1', 'page-1', 'edit-1');
    const statusRequest = http.expectOne('/api/documents/doc-1/pages/page-1/text-edits/edit-1');
    expect(statusRequest.request.method).toBe('GET');
    statusRequest.flush({ id: 'edit-1', state: 'Succeeded' });
    expect((await status).state).toBe('Succeeded');
    http.verify();
  });
});
