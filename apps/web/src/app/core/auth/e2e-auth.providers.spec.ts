import { firstValueFrom, skip } from 'rxjs';
import { E2eIdentityStore } from './e2e-auth.providers';

describe('E2eIdentityStore', () => {
  it('authenticates the selected fixture identity and clears it on sign out', async () => {
    const store = new E2eIdentityStore();
    const authenticated = firstValueFrom(store.observe().pipe(skip(1)));

    store.set({ identityToken: 'identity-token', appCheckToken: 'app-check-token' });

    await expect(authenticated).resolves.toMatchObject({ uid: 'e2e-user' });
    const signedOut = firstValueFrom(store.observe().pipe(skip(1)));
    await store.signOut();
    await expect(signedOut).resolves.toBeNull();
  });
});
