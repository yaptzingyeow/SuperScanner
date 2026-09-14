import { inject, Injectable, InjectionToken } from '@angular/core';
import { User } from 'firebase/auth';
import { Observable, shareReplay } from 'rxjs';

export interface AuthStateSource {
  observe(): Observable<User | null>;
}

export interface AuthActionsSource {
  ensureGuest(): Promise<void>;
  signInWithGoogle(): Promise<void>;
  registerWithEmail(email: string, password: string): Promise<void>;
  signInWithEmail(email: string, password: string): Promise<void>;
  signOut(): Promise<void>;
}

export const AUTH_STATE_SOURCE = new InjectionToken<AuthStateSource>('AUTH_STATE_SOURCE');
export const AUTH_ACTIONS_SOURCE = new InjectionToken<AuthActionsSource>('AUTH_ACTIONS_SOURCE');

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly source = inject(AUTH_STATE_SOURCE);
  private readonly actions = inject(AUTH_ACTIONS_SOURCE);

  readonly user$ = this.source.observe().pipe(shareReplay({ bufferSize: 1, refCount: true }));

  ensureGuest(): Promise<void> {
    return this.actions.ensureGuest();
  }

  signInWithGoogle(): Promise<void> {
    return this.actions.signInWithGoogle();
  }

  registerWithEmail(email: string, password: string): Promise<void> {
    return this.actions.registerWithEmail(email.trim(), password);
  }

  signInWithEmail(email: string, password: string): Promise<void> {
    return this.actions.signInWithEmail(email.trim(), password);
  }

  signOut(): Promise<void> {
    return this.actions.signOut();
  }
}
