import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { DocumentsApiService } from './documents-api.service';
import { HttpSignedUploadClient, SignedUploadClient, UploadService } from './upload.service';

describe('UploadService', () => {
  const pdf = () => new File(['%PDF-1.7'], 'form.pdf', { type: 'application/pdf' });

  function setup(overrides?: { expiresAt?: string; put?: SignedUploadClient['put'] }) {
    const events: string[] = [];
    const requests: unknown[] = [];
    const api = {
      async createDocument(title: string) {
        events.push('create-document');
        requests.push({ title });
        return {
          id: 'doc-1',
          title,
          status: 'Draft',
          pageCount: 0,
          updatedAt: '2026-09-04T02:00:00Z',
        };
      },
      async createUploadIntent(documentId: string, request: unknown) {
        events.push('create-intent');
        requests.push({ documentId, request });
        return {
          uploadId: 'up-1',
          pageId: 'page-1',
          putUrl: 'https://uploads.example.test/opaque',
          expiresAt: overrides?.expiresAt ?? '2099-09-04T02:10:00Z',
        };
      },
      async completeUpload(documentId: string, uploadId: string) {
        events.push('complete');
        requests.push({ documentId, uploadId });
        return { uploadId, state: 'PendingValidation' as const };
      },
    } as DocumentsApiService;
    const uploader: SignedUploadClient = {
      put:
        overrides?.put ??
        (async (_url, _file, _mediaType, onProgress) => {
          events.push('put');
          onProgress(64);
        }),
    };

    return { service: new UploadService(api, uploader), events, requests };
  }

  it('creates, uploads, and completes in order with a real SHA-256 digest', async () => {
    const { service, events, requests } = setup();

    const result = await service.upload('Application form', pdf());

    expect(events).toEqual(['create-document', 'create-intent', 'put', 'complete']);
    expect(requests[1]).toEqual({
      documentId: 'doc-1',
      request: {
        fileName: 'form.pdf',
        mediaType: 'application/pdf',
        sizeBytes: 8,
        sha256Hex: '86edbaa24831badfa0a8b04bb410141e2ee4182b6d0014493fe262a7a331c20b',
      },
    });
    expect(result).toEqual({ documentId: 'doc-1', uploadId: 'up-1', state: 'PendingValidation' });
    expect(service.progress()).toEqual({ stage: 'validating', percent: 100 });
  });

  it('rejects unsupported files before creating a document', async () => {
    const { service, events } = setup();
    const file = new File(['hello'], 'notes.txt', { type: 'text/plain' });

    await expect(service.upload('Notes', file)).rejects.toMatchObject({ code: 'unsupported_type' });
    expect(events).toEqual([]);
  });

  it('rejects files over 25 MiB before hashing or API calls', async () => {
    const { service, events } = setup();
    const file = {
      name: 'large.pdf',
      type: 'application/pdf',
      size: 25 * 1024 * 1024 + 1,
      arrayBuffer: () => Promise.reject(new Error('must not hash')),
    } as File;

    await expect(service.upload('Large file', file)).rejects.toMatchObject({
      code: 'file_too_large',
    });
    expect(events).toEqual([]);
  });

  it('does not PUT when the signed intent has expired', async () => {
    const { service, events } = setup({ expiresAt: '2000-01-01T00:00:00Z' });

    await expect(service.upload('Expired', pdf())).rejects.toMatchObject({
      code: 'intent_expired',
    });
    expect(events).toEqual(['create-document', 'create-intent']);
  });

  it('does not complete after a failed signed PUT', async () => {
    const { service, events } = setup({
      put: async () => {
        events.push('put');
        throw new Error('storage provider detail');
      },
    });

    await expect(service.upload('Failed upload', pdf())).rejects.toMatchObject({
      code: 'upload_failed',
    });
    expect(events).toEqual(['create-document', 'create-intent', 'put']);
  });

  it('rejects a duplicate submission while an upload is active', async () => {
    let finishPut!: () => void;
    const pendingPut = new Promise<void>((resolve) => {
      finishPut = resolve;
    });
    const { service } = setup({ put: () => pendingPut });
    const first = service.upload('First', pdf());
    await Promise.resolve();

    await expect(service.upload('Duplicate', pdf())).rejects.toMatchObject({
      code: 'upload_active',
    });
    finishPut();
    await first;
  });
});

describe('HttpSignedUploadClient', () => {
  it('sends only the signed content type to object storage', async () => {
    TestBed.configureTestingModule({
      providers: [HttpSignedUploadClient, provideHttpClient(), provideHttpClientTesting()],
    });
    const client = TestBed.inject(HttpSignedUploadClient);
    const http = TestBed.inject(HttpTestingController);
    const result = client.put(
      'https://uploads.example.test/opaque',
      new File(['image'], 'scan.png', { type: 'image/png' }),
      'image/png',
      () => undefined,
    );

    const request = http.expectOne('https://uploads.example.test/opaque');
    expect(request.request.method).toBe('PUT');
    expect(request.request.headers.keys()).toEqual(['Content-Type']);
    expect(request.request.headers.get('Content-Type')).toBe('image/png');
    request.flush(null);

    await expect(result).resolves.toBeUndefined();
    http.verify();
  });
});
