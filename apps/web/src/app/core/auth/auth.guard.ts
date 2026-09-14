import { inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { from, map, switchMap, take } from 'rxjs';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);

  return auth.user$.pipe(
    take(1),
    switchMap((user) => (user ? from([true]) : from(auth.ensureGuest()).pipe(map(() => true)))),
  );
};
