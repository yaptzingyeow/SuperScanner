import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { from, map, switchMap, take } from 'rxjs';
import { AuthService } from './auth.service';

/** Sends guests to log in first (with a way back here); signed-in accounts pass. */
export const signedInGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.user$.pipe(
    take(1),
    map((user) => (user && !user.isAnonymous
      ? true
      : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } }))),
  );
};

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);

  return auth.user$.pipe(
    take(1),
    switchMap((user) => (user ? from([true]) : from(auth.ensureGuest()).pipe(map(() => true)))),
  );
};
