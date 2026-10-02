import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  provideRouter,
  RouterStateSnapshot,
  UrlTree,
} from '@angular/router';
import { User } from 'firebase/auth';
import { firstValueFrom, Observable, of } from 'rxjs';
import { AuthService } from './auth.service';
import { authGuard, signedInGuard } from './auth.guard';

describe('authGuard', () => {
  it('allows an authenticated user to enter the application', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { user$: of({ uid: 'user-a' } as User) } },
      ],
    });

    const state = { url: '/documents/document-1/uploads/upload-1' } as RouterStateSnapshot;
    const decision = TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, state),
    );

    await expect(firstValueFrom(decision as Observable<boolean | UrlTree>)).resolves.toBe(true);
  });

  it('creates a guest session for an unauthenticated user', async () => {
    const ensureGuest = vi.fn().mockResolvedValue(undefined);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { user$: of(null), ensureGuest } },
      ],
    });

    const state = { url: '/documents/document-1/uploads/upload-1' } as RouterStateSnapshot;
    const decision = TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, state),
    );
    const result = await firstValueFrom(decision as Observable<boolean | UrlTree>);

    expect(result).toBe(true);
    expect(ensureGuest).toHaveBeenCalledOnce();
  });
});

describe('signedInGuard', () => {
  function decide(user: Partial<User> | null) {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: { user$: of(user) } }],
    });
    const state = { url: '/documents/d1/pages/p1/text?tool=add' } as RouterStateSnapshot;
    return firstValueFrom(TestBed.runInInjectionContext(() =>
      signedInGuard({} as ActivatedRouteSnapshot, state)) as Observable<boolean | UrlTree>);
  }

  it('lets a signed-in account through', async () => {
    await expect(decide({ uid: 'u1', isAnonymous: false })).resolves.toBe(true);
  });

  it('sends a guest to log in and back to the page afterwards', async () => {
    const decision = await decide({ uid: 'g1', isAnonymous: true });
    expect(decision).toBeInstanceOf(UrlTree);
    expect(String(decision)).toBe('/login?returnUrl=%2Fdocuments%2Fd1%2Fpages%2Fp1%2Ftext%3Ftool%3Dadd');
  });
});
