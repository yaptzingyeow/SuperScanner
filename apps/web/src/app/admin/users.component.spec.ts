import { UsersComponent } from './users.component';
import { renderAdmin, settle } from './admin.testing';

describe('UsersComponent', () => {
  it('lists users and searches by email', async () => {
    const { fixture, http, root } = renderAdmin(UsersComponent);
    http.expectOne((r) => r.url === '/api/admin/users').flush({ items: [
      { uid: 'u1', email: 'a@example.com', provider: 'password', isGuest: false, createdAt: '2026-10-01T00:00:00Z', lastSeenAt: '2026-10-02T00:00:00Z', plan: 'Free' },
    ], page: 1, pageSize: 50, total: 1 });
    await settle(fixture);
    expect(root.textContent).toContain('a@example.com');

    const search = root.querySelector('[data-user-search]') as HTMLInputElement;
    search.value = 'friend';
    search.dispatchEvent(new Event('input'));
    (root.querySelector('[data-search]') as HTMLButtonElement).click();

    const request = http.expectOne((r) => r.url === '/api/admin/users');
    expect(request.request.params.get('query')).toBe('friend');
    request.flush({ items: [], page: 1, pageSize: 50, total: 0 });
  });
});
