import { PaymentsComponent } from './payments.component';
import { renderAdmin, settle } from './admin.testing';

describe('PaymentsComponent', () => {
  it('payments empty state', async () => {
    const { fixture, http, root } = renderAdmin(PaymentsComponent);
    http.expectOne('/api/admin/payments?page=1').flush({ items: [], page: 1, pageSize: 50, total: 0 });
    await settle(fixture);

    expect(root.textContent).toContain('Payments appear here once HitPay is connected.');
  });
});
