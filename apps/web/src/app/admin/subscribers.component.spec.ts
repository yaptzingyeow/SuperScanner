import { SubscribersComponent } from './subscribers.component';
import { renderAdmin, settle } from './admin.testing';

describe('SubscribersComponent', () => {
  afterEach(() => vi.restoreAllMocks());

  it('extends and revokes a subscription', async () => {
    const row = { id: 's1', accountUid: 'u1', email: 'f@example.com', source: 'Manual', status: 'Active',
      startsAt: '2026-10-01T00:00:00Z', endsAt: '2026-11-01T00:00:00Z', note: 'friend', grantedByUid: 'owner',
      createdAt: '2026-10-01T00:00:00Z', revokedAt: null, active: true };
    const { fixture, http, root } = renderAdmin(SubscribersComponent);
    http.expectOne('/api/admin/subscriptions?page=1').flush({ items: [row], page: 1, pageSize: 50, total: 1 });
    await settle(fixture);
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    (root.querySelector('[data-extend]') as HTMLButtonElement).click();
    const extend = http.expectOne('/api/admin/subscriptions/s1/extend');
    expect(extend.request.body).toEqual({ duration: '1m' });
    extend.flush(row);
    await settle(fixture);
    http.expectOne('/api/admin/subscriptions?page=1').flush({ items: [row], page: 1, pageSize: 50, total: 1 });
    await settle(fixture);

    (root.querySelector('[data-revoke]') as HTMLButtonElement).click();
    http.expectOne('/api/admin/subscriptions/s1/revoke').flush(null);
  });
});
