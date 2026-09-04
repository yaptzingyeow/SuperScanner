import { EnvironmentProviders, InjectionToken, makeEnvironmentProviders } from '@angular/core';
import { FirebaseApp, getApp, getApps, initializeApp } from 'firebase/app';
import {
  AppCheck,
  getToken as getAppCheckToken,
  initializeAppCheck,
  ReCaptchaEnterpriseProvider,
} from 'firebase/app-check';
import { Auth, getAuth, onAuthStateChanged } from 'firebase/auth';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  API_BASE_URL,
  APP_CHECK_TOKEN_SOURCE,
  AppCheckTokenSource,
  IDENTITY_TOKEN_SOURCE,
  IdentityTokenSource,
} from '../api/security.interceptor';
import { AUTH_STATE_SOURCE, AuthStateSource } from './auth.service';

export const FIREBASE_APP = new InjectionToken<FirebaseApp>('FIREBASE_APP');
export const FIREBASE_AUTH = new InjectionToken<Auth>('FIREBASE_AUTH');
export const FIREBASE_APP_CHECK = new InjectionToken<AppCheck>('FIREBASE_APP_CHECK');

export function createIdentityTokenSource(auth: Auth): IdentityTokenSource {
  return {
    async getIdToken(): Promise<string> {
      if (!auth.currentUser) {
        throw new Error('Authentication required');
      }

      return auth.currentUser.getIdToken();
    },
  };
}

export function createAppCheckTokenSource(
  appCheck: AppCheck,
  tokenGetter: () => Promise<{ token: string }> = () => getAppCheckToken(appCheck),
): AppCheckTokenSource {
  return {
    async getToken(): Promise<string> {
      return (await tokenGetter()).token;
    },
  };
}

export function createAuthStateSource(auth: Auth): AuthStateSource {
  return {
    observe: () =>
      new Observable((subscriber) =>
        onAuthStateChanged(
          auth,
          subscriber.next.bind(subscriber),
          subscriber.error.bind(subscriber),
        ),
      ),
  };
}

export function provideFirebaseSecurity(): EnvironmentProviders {
  return makeEnvironmentProviders([
    {
      provide: FIREBASE_APP,
      useFactory: () => (getApps().length > 0 ? getApp() : initializeApp(environment.firebase)),
    },
    {
      provide: FIREBASE_AUTH,
      deps: [FIREBASE_APP],
      useFactory: (app: FirebaseApp) => getAuth(app),
    },
    {
      provide: FIREBASE_APP_CHECK,
      deps: [FIREBASE_APP],
      useFactory: (app: FirebaseApp) =>
        initializeAppCheck(app, {
          provider: new ReCaptchaEnterpriseProvider(environment.appCheckEnterpriseSiteKey),
          isTokenAutoRefreshEnabled: true,
        }),
    },
    {
      provide: IDENTITY_TOKEN_SOURCE,
      deps: [FIREBASE_AUTH],
      useFactory: createIdentityTokenSource,
    },
    {
      provide: APP_CHECK_TOKEN_SOURCE,
      deps: [FIREBASE_APP_CHECK],
      useFactory: createAppCheckTokenSource,
    },
    {
      provide: AUTH_STATE_SOURCE,
      deps: [FIREBASE_AUTH],
      useFactory: createAuthStateSource,
    },
    { provide: API_BASE_URL, useValue: environment.apiBaseUrl },
  ]);
}
