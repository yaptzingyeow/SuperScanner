import { TestBed } from '@angular/core/testing';
import { User } from 'firebase/auth';
import { BehaviorSubject, firstValueFrom, skip } from 'rxjs';
import { AUTH_ACTIONS_SOURCE, AUTH_STATE_SOURCE, AuthService } from './auth.service';

describe('AuthService', () => {
  it('exposes Firebase authentication state changes', async () => {
    const state = new BehaviorSubject<User | null>(null);
    TestBed.configureTestingModule({
      providers: [
        AuthService,
        { provide: AUTH_STATE_SOURCE, useValue: { observe: () => state.asObservable() } },
        { provide: AUTH_ACTIONS_SOURCE, useValue: {} },
      ],
    });
    const service = TestBed.inject(AuthService);
    const expectedUser = { uid: 'user-a' } as User;
    const nextUser = firstValueFrom(service.user$.pipe(skip(1)));

    state.next(expectedUser);

    await expect(nextUser).resolves.toBe(expectedUser);
  });

  it('delegates email sign-up, sign-in, and sign-out to Firebase actions', async () => {
    const actions = {
      createUser: vi.fn().mockResolvedValue(undefined),
      signIn: vi.fn().mockResolvedValue(undefined),
      signOut: vi.fn().mockResolvedValue(undefined),
    };
    TestBed.configureTestingModule({
      providers: [
        AuthService,
        { provide: AUTH_STATE_SOURCE, useValue: { observe: () => new BehaviorSubject(null) } },
        { provide: AUTH_ACTIONS_SOURCE, useValue: actions },
      ],
    });
    const service = TestBed.inject(AuthService);

    await service.createAccount('new@example.com', 'strong-password');
    await service.signIn('user@example.com', 'secret-password');
    await service.signOut();

    expect(actions.createUser).toHaveBeenCalledWith('new@example.com', 'strong-password');
    expect(actions.signIn).toHaveBeenCalledWith('user@example.com', 'secret-password');
    expect(actions.signOut).toHaveBeenCalledOnce();
  });
});
