import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import {
  API_BASE_URL,
  APP_CHECK_TOKEN_SOURCE,
  IDENTITY_TOKEN_SOURCE,
  securityInterceptor,
} from './security.interceptor';

describe('securityInterceptor', () => {
  let http: HttpClient;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([securityInterceptor])),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: '/api' },
        {
          provide: IDENTITY_TOKEN_SOURCE,
          useValue: { getIdToken: () => Promise.resolve('id-token') },
        },
        {
          provide: APP_CHECK_TOKEN_SOURCE,
          useValue: { getToken: () => Promise.resolve('app-token') },
        },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('adds Firebase identity and App Check headers to API requests', async () => {
    const response = firstValueFrom(http.get('/api/documents'));
    await Promise.resolve();
    await Promise.resolve();

    const request = httpTesting.expectOne('/api/documents');
    expect(request.request.headers.get('Authorization')).toBe('Bearer id-token');
    expect(request.request.headers.get('X-Firebase-AppCheck')).toBe('app-token');
    request.flush([]);

    await expect(response).resolves.toEqual([]);
  });

  it('does not leak security headers to signed storage requests', () => {
    const response = firstValueFrom(http.put('https://storage.example/upload', 'document'));
    const request = httpTesting.expectOne('https://storage.example/upload');

    expect(request.request.headers.has('Authorization')).toBe(false);
    expect(request.request.headers.has('X-Firebase-AppCheck')).toBe(false);
    request.flush(null);

    return expect(response).resolves.toBeNull();
  });
});
