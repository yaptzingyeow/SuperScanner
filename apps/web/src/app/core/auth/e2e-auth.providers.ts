import { EnvironmentProviders, Injectable, makeEnvironmentProviders } from '@angular/core';
import { User } from 'firebase/auth';
import { BehaviorSubject, Observable } from 'rxjs';
import {
  API_BASE_URL,
  APP_CHECK_TOKEN_SOURCE,
  AppCheckTokenSource,
  IDENTITY_TOKEN_SOURCE,
  IdentityTokenSource,
} from '../api/security.interceptor';
import { AUTH_ACTIONS_SOURCE, AUTH_STATE_SOURCE, AuthActionsSource, AuthStateSource } from './auth.service';

export interface E2eIdentityTokens {
  identityToken: string;
  appCheckToken: string;
}

@Injectable()
export class E2eIdentityStore
  implements IdentityTokenSource, AppCheckTokenSource, AuthStateSource, AuthActionsSource
{
  private tokens?: E2eIdentityTokens;
  private readonly user = new BehaviorSubject<User | null>(null);

  set(tokens: E2eIdentityTokens): void {
    this.tokens = tokens;
    this.user.next({ uid: 'e2e-user', email: 'e2e@superscanner.test' } as User);
  }

  observe(): Observable<User | null> {
    return this.user.asObservable();
  }

  createUser(): Promise<void> {
    return Promise.reject(new Error('Email authentication is unavailable in E2E mode.'));
  }

  signIn(): Promise<void> {
    return Promise.reject(new Error('Email authentication is unavailable in E2E mode.'));
  }

  signOut(): Promise<void> {
    this.tokens = undefined;
    this.user.next(null);
    return Promise.resolve();
  }

  getIdToken(): Promise<string> {
    return Promise.resolve(this.requireTokens().identityToken);
  }

  getToken(): Promise<string> {
    return Promise.resolve(this.requireTokens().appCheckToken);
  }

  private requireTokens(): E2eIdentityTokens {
    if (!this.tokens) throw new Error('E2E identity has not been selected.');
    return this.tokens;
  }
}

export function provideE2eSecurity(apiBaseUrl: string): EnvironmentProviders {
  return makeEnvironmentProviders([
    E2eIdentityStore,
    { provide: IDENTITY_TOKEN_SOURCE, useExisting: E2eIdentityStore },
    { provide: APP_CHECK_TOKEN_SOURCE, useExisting: E2eIdentityStore },
    { provide: AUTH_STATE_SOURCE, useExisting: E2eIdentityStore },
    { provide: AUTH_ACTIONS_SOURCE, useExisting: E2eIdentityStore },
    { provide: API_BASE_URL, useValue: apiBaseUrl },
  ]);
}
