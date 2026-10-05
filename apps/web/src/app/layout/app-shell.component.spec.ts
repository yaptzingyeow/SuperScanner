import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { PlanService } from '../plans/plan.service';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';
import { LoginComponent } from '../core/auth/login.component';
import { AppShellComponent } from './app-shell.component';

describe('AppShellComponent', () => {
  it('renders the Arks Scanner navigation and accessible application landmarks', () => {
    TestBed.configureTestingModule({
      imports: [AppShellComponent],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            user$: of({ email: 'user@example.com' }),
            signOut: vi.fn(),
            ensureGuest: vi.fn(),
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('[data-brand]')?.textContent).toContain('Arks Scanner');
    expect(root.querySelector('[data-brand]')?.textContent).toContain('by ArkSoft');
    expect(root.querySelector('nav[aria-label="Primary"]')?.textContent).toContain('Scan');
    expect(root.querySelector('nav[aria-label="Primary"]')?.textContent).toContain('My documents');
    expect(root.querySelector('main')).not.toBeNull();
    expect(root.querySelector('main router-outlet')).not.toBeNull();
    expect(root.querySelector('button[aria-label="Open account menu"]')).not.toBeNull();
  });

  it('signs the current user out from the account menu', async () => {
    const auth = {
      user$: of({ email: 'user@example.com' }),
      signOut: vi.fn().mockResolvedValue(undefined),
      ensureGuest: vi.fn().mockResolvedValue(undefined),
    };
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
    expect(auth.ensureGuest).toHaveBeenCalledOnce();
    expect(TestBed.inject(Router).url).toBe('/');
  });

  it('account menu shows plan and coming-soon upgrade', () => {
    const plans = { label: signal('Free'), isAdmin: signal(false), refresh: vi.fn().mockResolvedValue(undefined),
      needsPrivacyConsent: signal(false) };
    TestBed.configureTestingModule({
      imports: [AppShellComponent],
      providers: [
        provideRouter([]),
        { provide: PlanService, useValue: plans },
        { provide: AuthService, useValue: { user$: of({ email: 'user@example.com', isAnonymous: false }), signOut: vi.fn(), ensureGuest: vi.fn() } },
      ],
    });
    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    (root.querySelector('button[aria-label="Open account menu"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(root.querySelector('[data-plan-label]')?.textContent?.trim()).toBe('Free');
    const upgrade = root.querySelector('[data-upgrade]') as HTMLButtonElement;
    expect(upgrade.textContent?.trim()).toBe('Pro is coming soon');
    expect(upgrade.disabled).toBe(true);
    expect(plans.refresh).toHaveBeenCalled();

    plans.label.set('Pro (forever)');
    fixture.detectChanges();
    expect(root.querySelector('[data-plan-label]')?.textContent?.trim()).toBe('Pro (forever)');
    expect(root.querySelector('[data-upgrade]')).toBeNull();
  });

  it('asks a signed-in person once to accept the current privacy notice', async () => {
    const plans = {
      label: signal('Free'), isAdmin: signal(false), refresh: vi.fn().mockResolvedValue(undefined),
      needsPrivacyConsent: signal(true), acceptPrivacy: vi.fn().mockResolvedValue(undefined),
    };
    TestBed.configureTestingModule({
      imports: [AppShellComponent],
      providers: [
        provideRouter([]),
        { provide: PlanService, useValue: plans },
        { provide: AuthService, useValue: { user$: of({ email: 'user@example.com', isAnonymous: false }), signOut: vi.fn(), ensureGuest: vi.fn() } },
      ],
    });
    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const banner = root.querySelector('[data-privacy-consent]');
    expect(banner?.textContent).toContain('Your documents are private');
    expect(banner?.querySelector('a[href="/privacy"]')).not.toBeNull();

    (banner!.querySelector('[data-accept-privacy]') as HTMLButtonElement).click();
    expect(plans.acceptPrivacy).toHaveBeenCalled();
    plans.needsPrivacyConsent.set(false);
    fixture.detectChanges();
    expect(root.querySelector('[data-privacy-consent]')).toBeNull();
  });

  it('links to Privacy & your data from the account menu', () => {
    TestBed.configureTestingModule({
      imports: [AppShellComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { user$: of({ email: 'user@example.com', isAnonymous: false }), signOut: vi.fn(), ensureGuest: vi.fn() } },
      ],
    });
    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    (root.querySelector('button[aria-label="Open account menu"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(root.querySelector('.account-menu a[href="/privacy"]')?.textContent).toContain('Privacy & your data');
  });
});
