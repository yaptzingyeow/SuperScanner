import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, provideRouter, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { adminGuard } from './admin.guard';

describe('adminGuard', () => {
  function run() {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: API_BASE_URL, useValue: '/api' }],
    });
    const result = TestBed.runInInjectionContext(() =>
      adminGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)) as Promise<boolean | UrlTree>;
    return { result, http: TestBed.inject(HttpTestingController) };
  }

  it('redirects non-admins away', async () => {
    const { result, http } = run();
    http.expectOne('/api/admin/dashboard').flush('', { status: 404, statusText: 'Not Found' });
    const outcome = await result;
    expect(outcome instanceof UrlTree).toBe(true);
    expect(TestBed.inject(Router).serializeUrl(outcome as UrlTree)).toBe('/');
  });

  it('lets admins in', async () => {
    const { result, http } = run();
    http.expectOne('/api/admin/dashboard').flush({});
    expect(await result).toBe(true);
  });
});
