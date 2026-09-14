import { EnvironmentProviders, InjectionToken, makeEnvironmentProviders } from '@angular/core';
import { FirebaseApp, getApp, getApps, initializeApp } from 'firebase/app';
import { FirebaseError } from 'firebase/app';
import {
  AppCheck,
  getToken as getAppCheckToken,
  initializeAppCheck,
  ReCaptchaEnterpriseProvider,
} from 'firebase/app-check';
import {
  Auth,
  EmailAuthProvider,
  GoogleAuthProvider,
  createUserWithEmailAndPassword,
  getAuth,
  linkWithCredential,
  linkWithPopup,
  onAuthStateChanged,
  signInAnonymously,
  signInWithCredential,
  signInWithEmailAndPassword,
  signInWithPopup,
  signOut,
} from 'firebase/auth';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { appCheckDebugToken } from '../../../environments/app-check';
import {
  API_BASE_URL,
  APP_CHECK_TOKEN_SOURCE,
  AppCheckTokenSource,
  IDENTITY_TOKEN_SOURCE,
  IdentityTokenSource,
} from '../api/security.interceptor';
import {
  AUTH_ACTIONS_SOURCE,
  AUTH_STATE_SOURCE,
  AuthActionsSource,
  AuthStateSource,
} from './auth.service';

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
      try {
        return (await tokenGetter()).token;
      } catch (error) {
        if (environment.appCheckDebug) {
          console.error('App Check token acquisition failed:', (error as { code?: string })?.code ?? 'unknown');
        }
        throw error;
      }
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

export function createAuthActionsSource(auth: Auth): AuthActionsSource {
  return {
    ensureGuest: async () => {
      if (!auth.currentUser) await signInAnonymously(auth);
    },
    signInWithGoogle: async () => {
      const provider = new GoogleAuthProvider();
      provider.setCustomParameters({ prompt: 'select_account' });
      if (auth.currentUser?.isAnonymous) {
        try {
          await linkWithPopup(auth.currentUser, provider);
        } catch (error) {
          if (!(error instanceof FirebaseError) || error.code !== 'auth/credential-already-in-use') {
            throw error;
          }
          // Google has authenticated an existing account. Reuse that credential so
          // signing in does not require another popup or alter document ownership.
          const credential = GoogleAuthProvider.credentialFromError(error);
          if (!credential) throw error;
          await signInWithCredential(auth, credential);
        }
      } else {
        await signInWithPopup(auth, provider);
      }
    },
    registerWithEmail: async (email, password) => {
      if (auth.currentUser?.isAnonymous) {
        await linkWithCredential(auth.currentUser, EmailAuthProvider.credential(email, password));
      } else {
        await createUserWithEmailAndPassword(auth, email, password);
      }
    },
    signInWithEmail: async (email, password) => {
      await signInWithEmailAndPassword(auth, email, password);
    },
    signOut: async () => {
      await signOut(auth);
    },
  };
}

export function provideFirebaseSecurity(): EnvironmentProviders {
  if (environment.appCheckDebug) {
    (
      globalThis as typeof globalThis & {
        FIREBASE_APPCHECK_DEBUG_TOKEN?: boolean | string;
      }
    ).FIREBASE_APPCHECK_DEBUG_TOKEN = appCheckDebugToken;
  }

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
    { provide: AUTH_ACTIONS_SOURCE, deps: [FIREBASE_AUTH], useFactory: createAuthActionsSource },
    { provide: API_BASE_URL, useValue: environment.apiBaseUrl },
  ]);
}
