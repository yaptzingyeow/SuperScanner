import { HttpInterceptorFn } from '@angular/common/http';
import { inject, InjectionToken } from '@angular/core';
import { from, switchMap } from 'rxjs';

export interface IdentityTokenSource {
  getIdToken(): Promise<string>;
}

export interface AppCheckTokenSource {
  getToken(): Promise<string>;
}

export const IDENTITY_TOKEN_SOURCE = new InjectionToken<IdentityTokenSource>(
  'IDENTITY_TOKEN_SOURCE',
);

export const APP_CHECK_TOKEN_SOURCE = new InjectionToken<AppCheckTokenSource>(
  'APP_CHECK_TOKEN_SOURCE',
);

export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL');

export const securityInterceptor: HttpInterceptorFn = (request, next) => {
  const apiBaseUrl = inject(API_BASE_URL).replace(/\/+$/, '');
  if (request.url !== apiBaseUrl && !request.url.startsWith(`${apiBaseUrl}/`)) {
    return next(request);
  }

  const identity = inject(IDENTITY_TOKEN_SOURCE);
  const attestation = inject(APP_CHECK_TOKEN_SOURCE);

  return from(Promise.all([identity.getIdToken(), attestation.getToken()])).pipe(
    switchMap(([idToken, appCheckToken]) =>
      next(
        request.clone({
          setHeaders: {
            Authorization: `Bearer ${idToken}`,
            'X-Firebase-AppCheck': appCheckToken,
          },
        }),
      ),
    ),
  );
};
