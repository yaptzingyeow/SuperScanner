import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { FontFaceEntry } from './font-catalogue.models';
import { FontCatalogueService } from './font-catalogue.service';

const face = (url: string, id = 'noto-sans-sc'): FontFaceEntry => ({
  catalogueId: id, version: 'v1-regular', displayName: id, familyName: id, category: 'SansSerif', weight: 400,
  style: 'Normal', webFamilyName: `ArksScanner ${id} v1`, webAssetUrl: url, enabled: true, scripts: ['Hans', 'Hani', 'Latn'],
});

describe('FontCatalogueService', () => {
  const added: unknown[] = [];
  beforeEach(() => {
    added.length = 0;
    class FakeFontFace { constructor(readonly family: string, readonly source: unknown) {} load() { return Promise.resolve(this); } }
    vi.stubGlobal('FontFace', FakeFontFace);
    Object.defineProperty(document, 'fonts', { configurable: true, value: { add: (f: unknown) => added.push(f) } });
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: API_BASE_URL, useValue: '/api' }],
    });
  });
  afterEach(() => vi.unstubAllGlobals());

  it('keeps world-script fonts served by the API and rejects anything else', async () => {
    const service = TestBed.inject(FontCatalogueService);
    const list = service.list();
    TestBed.inject(HttpTestingController).expectOne('/api/text-edit-fonts').flush([
      face('/assets/fonts/Lato-Regular.ttf', 'lato'), face('/api/fonts/NotoSansSC-Regular.ttf'),
      face('https://evil.example/x.ttf', 'evil'), face('/api/fonts/../secret.ttf', 'escape'),
    ]);
    expect((await list).map((f) => f.catalogueId)).toEqual(['lato', 'noto-sans-sc']);
  });

  it('loads an API-served font with the signed-in request and registers it', async () => {
    const service = TestBed.inject(FontCatalogueService);
    const loading = service.loadFace(face('/api/fonts/NotoSansSC-Regular.ttf'));
    const request = TestBed.inject(HttpTestingController).expectOne('/api/fonts/NotoSansSC-Regular.ttf');
    expect(request.request.responseType).toBe('arraybuffer');
    request.flush(new ArrayBuffer(8));
    await loading;
    expect(added).toHaveLength(1);
    expect((added[0] as { source: unknown }).source).toBeInstanceOf(ArrayBuffer);
  });
});
