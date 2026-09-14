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

  it('delegates guest, Google, email, and sign-out actions to Firebase', async () => {
    const actions = {
      ensureGuest: vi.fn().mockResolvedValue(undefined),
      signInWithGoogle: vi.fn().mockResolvedValue(undefined),
      registerWithEmail: vi.fn().mockResolvedValue(undefined),
      signInWithEmail: vi.fn().mockResolvedValue(undefined),
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

    await service.ensureGuest();
    await service.signInWithGoogle();
    await service.registerWithEmail(' new@example.com ', 'strong-password');
    await service.signInWithEmail(' user@example.com ', 'secret-password');
    await service.signOut();

    expect(actions.ensureGuest).toHaveBeenCalledOnce();
    expect(actions.signInWithGoogle).toHaveBeenCalledOnce();
    expect(actions.registerWithEmail).toHaveBeenCalledWith('new@example.com', 'strong-password');
    expect(actions.signInWithEmail).toHaveBeenCalledWith('user@example.com', 'secret-password');
    expect(actions.signOut).toHaveBeenCalledOnce();
  });
});
