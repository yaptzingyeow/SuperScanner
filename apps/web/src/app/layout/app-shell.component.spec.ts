import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';
import { LoginComponent } from '../core/auth/login.component';
import { AppShellComponent } from './app-shell.component';

describe('AppShellComponent', () => {
  it('renders the SuperScanner navigation and accessible application landmarks', () => {
    TestBed.configureTestingModule({
      imports: [AppShellComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { user$: of({ email: 'user@example.com' }), signOut: vi.fn() } },
      ],
    });
    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('[data-brand]')?.textContent).toContain('SuperScanner');
    expect(root.querySelector('nav[aria-label="Primary"]')?.textContent).toContain('Home');
    expect(root.querySelector('nav[aria-label="Primary"]')?.textContent).toContain('Scan');
    expect(root.querySelector('nav[aria-label="Primary"]')?.textContent).toContain('Edit');
    expect(root.querySelector('nav[aria-label="Primary"]')?.textContent).toContain('Export');
    expect(root.querySelector('main')).not.toBeNull();
    expect(root.querySelector('main router-outlet')).not.toBeNull();
    expect(root.querySelector('button[aria-label="Open account menu"]')).not.toBeNull();
  });

  it('signs the current user out from the account menu', async () => {
    const auth = { user$: of({ email: 'user@example.com' }), signOut: vi.fn().mockResolvedValue(undefined) };
    TestBed.configureTestingModule({
      imports: [AppShellComponent],
      providers: [
        provideRouter([{ path: 'login', component: LoginComponent }]),
        { provide: AuthService, useValue: auth },
      ],
    });
    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    (root.querySelector('button[aria-label="Open account menu"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    (root.querySelector('[data-sign-out]') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(auth.signOut).toHaveBeenCalledOnce();
    expect(TestBed.inject(Router).url).toBe('/login');
  });
});
