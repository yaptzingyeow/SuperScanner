import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  it('signs in with email and password then returns to the workspace', async () => {
    const auth = { signIn: vi.fn().mockResolvedValue(undefined), createAccount: vi.fn() };
    TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [provideRouter([]), { provide: AuthService, useValue: auth }],
    });
    const fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    const formState = fixture.componentInstance as unknown as { email: string; password: string };
    formState.email = 'user@example.com';
    formState.password = 'secret-password';
    fixture.detectChanges();
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(auth.signIn).toHaveBeenCalledWith('user@example.com', 'secret-password');
    expect(TestBed.inject(Router).url).toBe('/');
  });

  it('creates an account and shows a safe duplicate-email message', async () => {
    const auth = {
      signIn: vi.fn(),
      createAccount: vi.fn().mockRejectedValue({ code: 'auth/email-already-in-use' }),
    };
    TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [provideRouter([]), { provide: AuthService, useValue: auth }],
    });
    const fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    (root.querySelector('[data-create-account]') as HTMLButtonElement).click();
    fixture.detectChanges();

    const formState = fixture.componentInstance as unknown as { email: string; password: string };
    formState.email = 'used@example.com';
    formState.password = 'strong-password';
    fixture.detectChanges();
    root.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(root.textContent).toContain('An account already exists for this email.');
    expect(root.textContent).not.toContain('auth/email-already-in-use');
  });

  it('returns the user to the protected deep link after sign in', async () => {
    const auth = { signIn: vi.fn().mockResolvedValue(undefined), createAccount: vi.fn() };
    const router = { navigateByUrl: vi.fn().mockResolvedValue(true) };
    TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        { provide: AuthService, useValue: auth },
        { provide: Router, useValue: router },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: { get: () => '/scan' } } },
        },
      ],
    });
    const fixture = TestBed.createComponent(LoginComponent);
    const formState = fixture.componentInstance as unknown as {
      email: string;
      password: string;
      submit(): Promise<void>;
    };
    formState.email = 'user@example.com';
    formState.password = 'secret-password';

    await formState.submit();

    expect(router.navigateByUrl).toHaveBeenCalledWith('/scan');
  });
});
