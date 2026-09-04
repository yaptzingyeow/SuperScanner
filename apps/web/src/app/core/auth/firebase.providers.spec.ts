import { AppCheck } from 'firebase/app-check';
import { Auth, User } from 'firebase/auth';
import { createAppCheckTokenSource, createIdentityTokenSource } from './firebase.providers';

describe('Firebase security token adapters', () => {
  it('gets an ID token from the current authenticated user', async () => {
    const user = { getIdToken: () => Promise.resolve('fresh-id-token') } as User;
    const source = createIdentityTokenSource({ currentUser: user } as Auth);

    await expect(source.getIdToken()).resolves.toBe('fresh-id-token');
  });

  it('fails closed when no authenticated user exists', async () => {
    const source = createIdentityTokenSource({ currentUser: null } as Auth);

    await expect(source.getIdToken()).rejects.toThrow('Authentication required');
  });

  it('unwraps the Firebase App Check token result', async () => {
    const source = createAppCheckTokenSource({} as AppCheck, () =>
      Promise.resolve({ token: 'app-check-token' }),
    );

    await expect(source.getToken()).resolves.toBe('app-check-token');
  });
});
