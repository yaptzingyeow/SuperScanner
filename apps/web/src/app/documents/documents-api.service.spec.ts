import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { DocumentsApiService } from './documents-api.service';

describe('DocumentsApiService organizer APIs', () => {
  let service: DocumentsApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        DocumentsApiService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: '/api/' },
      ],
    });
    service = TestBed.inject(DocumentsApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads detail and sends the exact reorder payload', async () => {
    const detailPromise = service.getDocument('doc-1');
    http.expectOne('/api/documents/doc-1').flush({ id: 'doc-1', pages: [], imports: [] });
    await detailPromise;

    const reorder = service.reorderPages('doc-1', {
      expectedPageOrderRevision: 4,
      pageIds: ['p3', 'p1', 'p2'],
    });
    const request = http.expectOne('/api/documents/doc-1/page-order');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      expectedPageOrderRevision: 4,
      pageIds: ['p3', 'p1', 'p2'],
    });
    request.flush({ revision: 8, pageOrderRevision: 5, pages: [] });
    await Promise.resolve();
    http.expectOne('/api/documents/doc-1').flush({ id: 'doc-1', pages: [], imports: [] });
    await reorder;
  });

  it('removes pages and supports export create, status, and binary download', async () => {
    const removal = service.removePage('doc-1', 'page-2');
    const deleteRequest = http.expectOne('/api/documents/doc-1/pages/page-2');
    expect(deleteRequest.request.method).toBe('DELETE');
    deleteRequest.flush(null);
    await removal;

    const creation = service.createExport('doc-1');
    const createRequest = http.expectOne('/api/documents/doc-1/exports');
    expect(createRequest.request.method).toBe('POST');
    createRequest.flush({ id: 'export-1', state: 'Queued' });
    await creation;

    const status = service.getExport('doc-1', 'export-1');
    http.expectOne('/api/documents/doc-1/exports/export-1').flush({
      id: 'export-1',
      state: 'Ready',
    });
    await status;

    const download = service.downloadExport('doc-1', 'export-1');
    const downloadRequest = http.expectOne('/api/documents/doc-1/exports/export-1/download');
    expect(downloadRequest.request.responseType).toBe('blob');
    downloadRequest.flush(new Blob(['pdf'], { type: 'application/pdf' }));
    await expect(download).resolves.toBeInstanceOf(Blob);
  });

  it('gets and requests OCR for an exact document page', async () => {
    const status = service.getPageOcr('doc-1', 'page-2');
    const get = http.expectOne('/api/documents/doc-1/pages/page-2/ocr');
    expect(get.request.method).toBe('GET');
    get.flush({ state: 'NotRequested', elementCount: 0, canRetry: false, elements: [] });
    await status;

    const request = service.requestPageOcr('doc-1', 'page-2', true);
    const post = http.expectOne('/api/documents/doc-1/pages/page-2/ocr');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ retryFailed: true });
    post.flush({ state: 'Queued', elementCount: 0, canRetry: false, elements: [] });
    await request;
  });
});
