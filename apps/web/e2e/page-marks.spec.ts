import { expect, test } from '@playwright/test';

test('place, style, save, reload and undo a transparent tick without OCR', async ({ page }) => {
  const documentId = '00000000-0000-0000-0000-000000000011';
  const pageId = '00000000-0000-0000-0000-000000000012';
  const markId = '00000000-0000-0000-0000-000000000013';
  const documentPath = `/documents/${documentId}`;
  const scan = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jh1QAAAAASUVORK5CYII=', 'base64');
  let mark: Record<string, unknown> | null = null;
  await page.route('**/api/**', async route => {
    const url = new URL(route.request().url());
    const method = route.request().method();
    const fulfill = (body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    if (url.pathname === '/api/e2e/identity') return fulfill({ identityToken: 'local-test', appCheckToken: 'local-test' });
    if (url.pathname.endsWith('/marks') && method === 'POST') {
      mark = { ...route.request().postDataJSON(), id: markId, pageId, revision: 0 };
      return fulfill(mark, 201);
    }
    if (url.pathname.endsWith(`/marks/${markId}`) && method === 'DELETE') { mark = null; return route.fulfill({ status: 204 }); }
    if (url.pathname.endsWith('/marks')) return fulfill(mark ? [mark] : []);
    if (url.pathname.endsWith('/signatures')) return fulfill([]);
    if (url.pathname.endsWith('/preview') || url.pathname.endsWith('/thumbnail')) return route.fulfill({ contentType: 'image/png', body: scan });
    if (url.pathname.endsWith('/ocr')) return fulfill({ state: 'NotRequested', elements: [], elementCount: 0, canRetry: false });
    if (url.pathname === '/api/documents') return fulfill([{ id: documentId, title: 'Mark test', status: 'Ready', pageCount: 1, updatedAt: new Date().toISOString() }]);
    if (url.pathname === `/api${documentPath}`) return fulfill({ id: documentId, title: 'Mark test', status: 'Ready', revision: 1, pageOrderRevision: 1,
      pages: [{ id: pageId, position: 1, pageNumber: 1, sourceUploadId: pageId, sourcePageIndex: 1, state: 'Ready', hasPreview: true,
        hasOriginal: true, canCrop: false, cropStatus: 'Ready', cropRevision: 0, appliedCropRevision: 0, previewRevision: 'base', filter: 'Original', appliedFilter: 'Original' }], imports: [], latestExport: null });
    return fulfill({}, 404);
  });
  const open = async () => {
    await page.goto('/e2e-login?user=user-a');
    await page.getByRole('link', { name: 'My documents' }).click();
    await page.locator(`a[href="${documentPath}"]`).click();
    await page.getByTestId('open-text-editor').click();
  };
  await open();
  await page.getByTestId('zoom-in').click();
  await page.getByTestId('add-mark').click();
  await expect(page.getByText('Click a checkbox on the page to place your mark.')).toBeVisible();
  const surface = page.locator('app-page-mark-overlay .mark-surface');
  const surfaceBox = (await surface.boundingBox())!;
  await surface.click({ position: { x: 25, y: 25 } });
  await expect(page.getByTestId('save-mark')).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.getByTestId('mark-color-blue')).toBeVisible();
  await page.screenshot({ path: '../../.task-tools/page-marks-mobile-draft.png', fullPage: true });
  await page.setViewportSize({ width: 1280, height: 720 });
  await page.locator('app-page-mark-tools input[type="color"]').fill('#0000ff');
  await page.getByTestId('save-mark').click();
  await expect(page.getByText('Mark saved. Export a new PDF to include it.')).toBeVisible();
  expect(mark?.['color']).toBe('#0000ff');
  expect((mark?.['box'] as { x: number }).x).toBeCloseTo(Math.max(0, 25 / surfaceBox.width - .0125), 2);
  await open();
  await expect(page.locator('app-page-mark-overlay svg path')).toHaveAttribute('stroke', '#0000ff');
  await page.screenshot({ path: '../../.task-tools/page-marks-desktop.png', fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.getByTestId('add-mark')).toBeVisible();
  await page.screenshot({ path: '../../.task-tools/page-marks-mobile.png', fullPage: true });
  await page.locator('app-page-mark-overlay button.mark-body').click();
  await page.getByRole('button', { name: 'Delete selected mark' }).click();
  await expect(page.getByTestId('undo-mark')).toBeEnabled();
});
