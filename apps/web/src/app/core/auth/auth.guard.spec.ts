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
import { authGuard } from './auth.guard';

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
