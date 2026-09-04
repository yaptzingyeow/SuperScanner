import { TestBed } from '@angular/core/testing';
import { User } from 'firebase/auth';
import { BehaviorSubject, firstValueFrom, skip } from 'rxjs';
import { AUTH_STATE_SOURCE, AuthService } from './auth.service';

describe('AuthService', () => {
  it('exposes Firebase authentication state changes', async () => {
    const state = new BehaviorSubject<User | null>(null);
    TestBed.configureTestingModule({
      providers: [
        AuthService,
        { provide: AUTH_STATE_SOURCE, useValue: { observe: () => state.asObservable() } },
      ],
    });
    const service = TestBed.inject(AuthService);
    const expectedUser = { uid: 'user-a' } as User;
    const nextUser = firstValueFrom(service.user$.pipe(skip(1)));

    state.next(expectedUser);

    await expect(nextUser).resolves.toBe(expectedUser);
  });
});
