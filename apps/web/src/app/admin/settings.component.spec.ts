import { SettingsComponent } from './settings.component';
import { renderAdmin, settle } from './admin.testing';

const settings = {
  phase: 'Test', enforceFromUtc: null, freeOcrPagesPerDay: 5, freeWatermarkExportsPerDay: 3,
  freeMaxDocuments: 30, freeRetentionDays: 7, usageTimeZone: 'Asia/Kuala_Lumpur', ocrCostPerThousandPages: 1.5,
};

describe('SettingsComponent', () => {
  afterEach(() => vi.restoreAllMocks());

  it('settings save confirms then PUTs', async () => {
    const { fixture, http, root } = renderAdmin(SettingsComponent);
    http.expectOne('/api/admin/settings').flush(settings);
    await settle(fixture);
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
    const ocr = root.querySelector('[data-setting="freeOcrPagesPerDay"]') as HTMLInputElement;
    ocr.value = '9';
    ocr.dispatchEvent(new Event('input'));

    (root.querySelector('[data-save-settings]') as HTMLButtonElement).click();
    http.expectNone('/api/admin/settings');
    (root.querySelector('[data-save-settings]') as HTMLButtonElement).click();

    expect(confirm).toHaveBeenCalledWith('Apply to all users now?');
    const put = http.expectOne((r) => r.method === 'PUT' && r.url === '/api/admin/settings');
    expect(put.request.body.freeOcrPagesPerDay).toBe(9);
    put.flush({ ...settings, freeOcrPagesPerDay: 9 });
    await settle(fixture);
    expect(root.textContent).toContain('Saved');
  });
});
