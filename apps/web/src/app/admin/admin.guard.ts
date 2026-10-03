import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AdminApiService } from './admin-api.service';

/** Only admins reach /admin; the API answers 404 for everyone else, so any failure sends them home. */
export const adminGuard: CanActivateFn = async () => {
  const router = inject(Router);
  try {
    await inject(AdminApiService).dashboard();
    return true;
  } catch {
    return router.parseUrl('/');
  }
};
