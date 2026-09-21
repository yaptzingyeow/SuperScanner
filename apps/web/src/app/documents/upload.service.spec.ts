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
      async getUploadStatus(_documentId: string, uploadId: string) {
        return {
          uploadId,
          state: 'Accepted' as const,
          discoveredPageCount: 1,
          createdPageCount: 1,
          failedPageCount: 0,
          errorCode: null,
        };
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
    expect(result).toEqual({ documentId: 'doc-1', uploadId: 'up-1', state: 'Accepted' });
    expect(service.progress()).toEqual({ stage: 'accepted', percent: 100, errorCode: undefined });
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

  it('adds files sequentially and retains an independent failed item', async () => {
    const order: string[] = [];
    let nextId = 0;
    const api = {
      createUploadIntent: async (_documentId: string, request: { fileName: string }) => {
        const id = ++nextId;
        order.push(`intent:${request.fileName}`);
        return {
          uploadId: `up-${id}`,
          putUrl: `https://upload/${id}`,
          expiresAt: '2099-01-01T00:00:00Z',
        };
      },
      completeUpload: async (_documentId: string, uploadId: string) => ({
        uploadId,
        state: 'PendingValidation',
      }),
      getUploadStatus: async (_documentId: string, uploadId: string) => ({
        uploadId,
        state: uploadId === 'up-1' ? 'Accepted' : 'Rejected',
        discoveredPageCount: uploadId === 'up-1' ? 1 : 0,
        createdPageCount: uploadId === 'up-1' ? 1 : 0,
        failedPageCount: 0,
        errorCode: uploadId === 'up-1' ? null : 'pdf_invalid',
      }),
    } as unknown as DocumentsApiService;
    const uploader: SignedUploadClient = {
      put: async (_url, file) => {
        order.push(`put:${file.name}`);
      },
    };
    const service = new UploadService(api, uploader, 0);

    const items = await service.addFiles('doc-1', [
      new File(['photo'], 'photo.jpg', { type: 'image/jpeg' }),
      new File(['%PDF'], 'broken.pdf', { type: 'application/pdf' }),
    ]);

    expect(order).toEqual([
      'intent:photo.jpg',
      'put:photo.jpg',
      'intent:broken.pdf',
      'put:broken.pdf',
    ]);
    expect(items.map((item) => item.fileName)).toEqual(['photo.jpg', 'broken.pdf']);
    expect(items.map((item) => item.stage)).toEqual(['accepted', 'rejected']);
    expect(items[1].errorCode).toBe('pdf_invalid');
  });

  it('polls expansion until all discovered pages are accounted for', async () => {
    let polls = 0;
    const api = {
      createUploadIntent: async () => ({
        uploadId: 'up-1',
        putUrl: 'https://upload/1',
        expiresAt: '2099-01-01T00:00:00Z',
      }),
      completeUpload: async () => ({ uploadId: 'up-1', state: 'PendingValidation' }),
      getUploadStatus: async () => {
        polls++;
        return polls === 1
          ? {
              uploadId: 'up-1',
              state: 'Accepted',
              discoveredPageCount: 3,
              createdPageCount: 1,
              failedPageCount: 0,
            }
          : {
              uploadId: 'up-1',
              state: 'Accepted',
              discoveredPageCount: 3,
              createdPageCount: 3,
              failedPageCount: 0,
            };
      },
    } as unknown as DocumentsApiService;
    const service = new UploadService(api, { put: async () => undefined }, 0);

    const [item] = await service.addFiles('doc-1', [pdf()]);

    expect(polls).toBe(2);
    expect(item).toMatchObject({ stage: 'accepted', discoveredPageCount: 3, createdPageCount: 3 });
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
