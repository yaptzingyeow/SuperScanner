import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  provideRouter,
  Router,
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

    const decision = TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
    );

    await expect(firstValueFrom(decision as Observable<boolean | UrlTree>)).resolves.toBe(true);
  });

  it('redirects an unauthenticated user to sign in', async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: { user$: of(null) } }],
    });

    const decision = TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
    );
    const result = await firstValueFrom(decision as Observable<boolean | UrlTree>);

    expect(result).toEqual(TestBed.inject(Router).createUrlTree(['/sign-in']));
  });
});
