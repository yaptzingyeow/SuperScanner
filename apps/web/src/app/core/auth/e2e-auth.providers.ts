import { EnvironmentProviders, Injectable, makeEnvironmentProviders } from '@angular/core';
import { of } from 'rxjs';
import {
  API_BASE_URL,
  APP_CHECK_TOKEN_SOURCE,
  AppCheckTokenSource,
  IDENTITY_TOKEN_SOURCE,
  IdentityTokenSource,
} from '../api/security.interceptor';
import { AUTH_STATE_SOURCE } from './auth.service';

export interface E2eIdentityTokens {
  identityToken: string;
  appCheckToken: string;
}

@Injectable()
export class E2eIdentityStore implements IdentityTokenSource, AppCheckTokenSource {
  private tokens?: E2eIdentityTokens;

  set(tokens: E2eIdentityTokens): void {
    this.tokens = tokens;
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
    { provide: AUTH_STATE_SOURCE, useValue: { observe: () => of(null) } },
    { provide: API_BASE_URL, useValue: apiBaseUrl },
  ]);
}
