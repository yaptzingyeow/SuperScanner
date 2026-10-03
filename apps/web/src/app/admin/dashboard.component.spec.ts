import { DashboardComponent } from './dashboard.component';
import { renderAdmin, settle } from './admin.testing';

describe('DashboardComponent', () => {
  it('renders counts and the phase banner', async () => {
    const { fixture, http, root } = renderAdmin(DashboardComponent);
    http.expectOne('/api/admin/dashboard').flush({
      users: { total: 120, signedIn: 100, guests: 20, new: { today: 3, d7: 15, d30: 60 }, active7d: 44 },
      subscribers: { total: 7, manual: 7, paid: 0 },
      documents: { total: 900, today: 12 },
      ocr: { today: 50, month: 2000, estimatedCostMonth: 3 },
      watermarkExports: { today: 4, month: 80 },
      series: Array.from({ length: 30 }, (_, i) => ({ day: '2026-09-' + String(i + 1).padStart(2, '0'), newUsers: i, ocrPages: i * 2 })),
      phase: 'Test', effectivePhase: 'Test', enforceFromUtc: '2026-12-01T00:00:00Z',
    });
    await settle(fixture);

    expect(root.querySelector('[data-metric="users"]')?.textContent).toContain('120');
    expect(root.querySelector('[data-metric="subscribers"]')?.textContent).toContain('7');
    expect(root.querySelector('[data-metric="ocr-cost"]')?.textContent).toContain('3.00');
    expect(root.querySelector('[data-phase-banner]')?.textContent).toContain('Test phase');
    expect(root.querySelectorAll('svg [data-bar]').length).toBe(30);
  });
});
