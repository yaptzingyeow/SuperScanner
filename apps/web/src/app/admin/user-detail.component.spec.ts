import { UserDetailComponent } from './user-detail.component';
import { renderAdmin, settle } from './admin.testing';

const detail = {
  uid: 'u1', email: 'friend@example.com', provider: 'google.com', isGuest: false,
  createdAt: '2026-09-01T00:00:00Z', lastSeenAt: '2026-10-01T00:00:00Z', plan: 'Free', isAdmin: false,
  documentCount: 4, usage: { day: '2026-10-04', ocrPages: 1, bonusOcrPages: 0, watermarkExports: 0, resetsAt: '2026-10-04T16:00:00Z' },
  subscriptions: [],
};

describe('UserDetailComponent', () => {
  it('give-Pro posts the chosen duration and note', async () => {
    const { fixture, http, root } = renderAdmin(UserDetailComponent, { uid: 'u1' });
    http.expectOne('/api/admin/users/u1').flush(detail);
    await settle(fixture);

    const select = root.querySelector('[data-grant-duration]') as HTMLSelectElement;
    select.value = '3m';
    select.dispatchEvent(new Event('change'));
    const note = root.querySelector('[data-grant-note]') as HTMLInputElement;
    note.value = 'Friend from school';
    note.dispatchEvent(new Event('input'));
    (root.querySelector('[data-grant]') as HTMLButtonElement).click();

    const request = http.expectOne('/api/admin/users/u1/subscriptions');
    expect(request.request.body).toEqual({ duration: '3m', note: 'Friend from school' });
    request.flush({ id: 's1' });
    await settle(fixture);
    http.expectOne('/api/admin/users/u1').flush({ ...detail, plan: 'Pro' });
    await settle(fixture);
    expect(root.querySelector('[data-user-plan]')?.textContent).toContain('Pro');
  });
});
