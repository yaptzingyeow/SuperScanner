import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { PageSignatureService } from './page-signature.service';

describe('PageSignatureService', () => {
  const route = '/api/documents/doc/pages/page/signatures';
  const box = { x: .1, y: .2, width: .3, height: .15 };
  const dto = { id: 'sig', pageId: 'page', box, imageAspectRatio: 2, revision: 0, imageUrl: `${route}/sig/image` };
  function setup() {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), { provide: API_BASE_URL, useValue: '/api' }] });
    return { service: TestBed.inject(PageSignatureService), http: TestBed.inject(HttpTestingController) };
  }
  it('creates multipart PNG and keeps one idempotency key', async () => {
    const { service, http } = setup();
    const pending = service.create('doc', 'page', new Blob(['png'], { type: 'image/png' }), box, 'key');
    const request = http.expectOne(route);
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Idempotency-Key')).toBe('key');
    expect(request.request.body.get('image').type).toBe('image/png');
    expect(JSON.parse(request.request.body.get('box'))).toEqual(box);
    request.flush(dto); expect((await pending).id).toBe('sig'); http.verify();
  });
  it('loads a private image from the API rather than an untrusted response URL', async () => {
    const { service, http } = setup();
    const pending = service.list('doc', 'page');
    http.expectOne(route).flush([{ ...dto, imageUrl: 'https://evil.test/ink', assetKey: 'private/key' }]);
    const result = await pending;
    expect(result[0].imageUrl).toBe(`${route}/sig/image`);
    expect('assetKey' in result[0]).toBe(false);
    const image = service.image('doc', 'page', 'sig');
    const request = http.expectOne(`${route}/sig/image`);
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['ink'])); expect((await image).size).toBe(3); http.verify();
  });
  it('updates with expected revision and preserves 409 for the caller', async () => {
    const { service, http } = setup();
    const pending = service.update('doc', 'page', 'sig', box, 3);
    const rejected = expect(pending).rejects.toMatchObject({ status: 409 });
    const request = http.expectOne(`${route}/sig`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ box, expectedRevision: 3 });
    request.flush({ code: 'signature_revision_conflict' }, { status: 409, statusText: 'Conflict' });
    await rejected; http.verify();
  });
  it('deletes only the selected ID with its expected revision', async () => {
    const { service, http } = setup();
    const pending = service.delete('doc', 'page', 'sig', 2);
    const request = http.expectOne(`${route}/sig?expectedRevision=2`);
    expect(request.request.method).toBe('DELETE'); request.flush(null);
    await pending; http.verify();
  });
});
