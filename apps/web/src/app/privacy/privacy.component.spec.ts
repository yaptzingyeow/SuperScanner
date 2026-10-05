import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { AuthService } from '../core/auth/auth.service';
import { PrivacyComponent } from './privacy.component';

describe('PrivacyComponent', () => {
  function setup() {
    const auth = { signOut: vi.fn().mockResolvedValue(undefined), ensureGuest: vi.fn().mockResolvedValue(undefined) };
    const save = vi.fn();
    TestBed.configureTestingModule({
      imports: [PrivacyComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api' }, { provide: AuthService, useValue: auth }],
    });
    const fixture = TestBed.createComponent(PrivacyComponent);
    (fixture.componentInstance as unknown as { saveFile: typeof save }).saveFile = save;
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement, http: TestBed.inject(HttpTestingController), auth, save };
  }

  it('downloads my data as a JSON file', async () => {
    const { fixture, el, http, save } = setup();
    (el.querySelector('[data-download-data]') as HTMLButtonElement).click();
    http.expectOne('/api/me/export').flush({ account: { uid: 'u1' } });
    await fixture.whenStable();
    expect(save).toHaveBeenCalledWith(expect.any(Blob), 'arks-scanner-my-data.json');
  });

  it('deletes the account only after typing DELETE, then signs out', async () => {
    const { fixture, el, http, auth } = setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const button = el.querySelector('[data-delete-account]') as HTMLButtonElement;
    expect(button.disabled).toBe(true);

    const confirm = el.querySelector('[data-delete-confirm]') as HTMLInputElement;
    confirm.value = 'DELETE';
    confirm.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(button.disabled).toBe(false);
    button.click();

    const request = http.expectOne((r) => r.method === 'DELETE' && r.url === '/api/me');
    expect(request.request.body).toEqual({ confirm: 'DELETE' });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();
    expect(auth.signOut).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith('/');
  });

  it('explains when the last admin tries to delete their account', async () => {
    const { fixture, el, http } = setup();
    const confirm = el.querySelector('[data-delete-confirm]') as HTMLInputElement;
    confirm.value = 'DELETE';
    confirm.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (el.querySelector('[data-delete-account]') as HTMLButtonElement).click();
    http.expectOne('/api/me').flush({ code: 'last_admin' }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Add another admin');
  });
});
