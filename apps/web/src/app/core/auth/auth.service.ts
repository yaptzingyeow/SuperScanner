import { inject, Injectable, InjectionToken } from '@angular/core';
import { User } from 'firebase/auth';
import { Observable, shareReplay } from 'rxjs';

export interface AuthStateSource {
  observe(): Observable<User | null>;
}

export const AUTH_STATE_SOURCE = new InjectionToken<AuthStateSource>('AUTH_STATE_SOURCE');

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly source = inject(AUTH_STATE_SOURCE);

  readonly user$ = this.source.observe().pipe(shareReplay({ bufferSize: 1, refCount: true }));
}
