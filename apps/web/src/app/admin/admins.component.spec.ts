import { AdminsComponent } from './admins.component';
import { renderAdmin, settle } from './admin.testing';

describe('AdminsComponent', () => {
  afterEach(() => vi.restoreAllMocks());

  it('remove-admin shows the last-admin error from 409', async () => {
    const { fixture, http, root } = renderAdmin(AdminsComponent);
    http.expectOne('/api/admin/admins').flush({ items: [{ uid: 'owner', email: 'owner@example.com', addedByUid: null, addedAt: '2026-10-01T00:00:00Z' }], page: 1, pageSize: 1, total: 1 });
    await settle(fixture);
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    (root.querySelector('[data-remove-admin]') as HTMLButtonElement).click();
    http.expectOne((r) => r.method === 'DELETE' && r.url === '/api/admin/admins/owner')
      .flush({ code: 'last_admin' }, { status: 409, statusText: 'Conflict' });
    await settle(fixture);

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('At least one admin must remain.');
  });

  it('adds an admin by email and explains when no account matches', async () => {
    const { fixture, http, root } = renderAdmin(AdminsComponent);
    http.expectOne('/api/admin/admins').flush({ items: [], page: 1, pageSize: 0, total: 0 });
    await settle(fixture);
    const email = root.querySelector('[data-admin-email]') as HTMLInputElement;
    email.value = 'nobody@example.com';
    email.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (root.querySelector('[data-add-admin]') as HTMLButtonElement).click();
    const post = http.expectOne((r) => r.method === 'POST' && r.url === '/api/admin/admins');
    expect(post.request.body).toEqual({ email: 'nobody@example.com' });
    post.flush('', { status: 404, statusText: 'Not Found' });
    await settle(fixture);

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('No signed-in account uses that email yet');
  });
});
