import { expect, test } from '@playwright/test';

test('real canvas signature is transparent, movable, saved and editable after reopening', async ({ page }) => {
  const documentId = '00000000-0000-0000-0000-000000000001';
  const pageId = '00000000-0000-0000-0000-000000000002';
  const signatureId = '00000000-0000-0000-0000-000000000003';
  const documentPath = `/documents/${documentId}`;
  let signature: object | undefined;
  let signatureBytes = Buffer.alloc(0);
  let savedBox: { x: number; y: number; width: number; height: number } | undefined;
  const scan = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jh1QAAAAASUVORK5CYII=', 'base64');
  await page.route('**/api/**', async route => {
    const url = new URL(route.request().url());
    const method = route.request().method();
    const fulfill = (body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    if (url.pathname === '/api/e2e/identity') return fulfill({ identityToken: 'local-test', appCheckToken: 'local-test' });
    if (url.pathname.endsWith('/signatures') && method === 'POST') {
      const body = route.request().postDataBuffer()!;
      const start = body.indexOf(Buffer.from([137,80,78,71,13,10,26,10]));
      const endMarker = Buffer.from([73,69,78,68,174,66,96,130]);
      const end = body.indexOf(endMarker, start);
      expect(start).toBeGreaterThan(0); expect(end).toBeGreaterThan(start);
      signatureBytes = body.subarray(start, end + endMarker.length);
      const match = body.toString('latin1').match(/\{"x":.*?\}/);
      savedBox = JSON.parse(match![0]);
      signature = { id: signatureId, pageId, box: savedBox, imageAspectRatio: 3, revision: 0, imageUrl: `${url.pathname}/${signatureId}/image` };
      return fulfill(signature, 201);
    }
    if (url.pathname.endsWith('/signatures')) return fulfill(signature ? [signature] : []);
    if (url.pathname.endsWith(`/signatures/${signatureId}/image`)) return route.fulfill({ contentType: 'image/png', body: signatureBytes });
    if (url.pathname.endsWith('/preview') || url.pathname.endsWith('/thumbnail')) return route.fulfill({ contentType: 'image/png', body: scan });
    if (url.pathname.endsWith('/ocr')) return fulfill({ state: 'NotRequested', elements: [], elementCount: 0, canRetry: false });
    if (url.pathname === '/api/documents') return fulfill([{ id: documentId, title: 'Signature test', status: 'Ready', pageCount: 1, updatedAt: new Date().toISOString() }]);
    if (url.pathname === `/api${documentPath}`) return fulfill({ id: documentId, title: 'Signature test', status: 'Ready', revision: 1, pageOrderRevision: 1,
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
  await page.getByTestId('add-signature').click();
  await expect.poll(() => page.evaluate(() => !!document.activeElement?.closest('[role="dialog"]'))).toBe(true);
  await page.getByTestId('draw-mode').click();
  const canvas = page.locator('app-signature-creator canvas');
  const bounds = (await canvas.boundingBox())!;
  await page.mouse.move(bounds.x + 60, bounds.y + 80); await page.mouse.down();
  await page.mouse.move(bounds.x + 160, bounds.y + 40, { steps: 8 });
  await page.mouse.move(bounds.x + 260, bounds.y + 90, { steps: 8 }); await page.mouse.up();
  await page.getByTestId('use-signature').click();
  await expect(page.getByTestId('save-signature')).toBeVisible();
  const alpha = await page.locator('app-page-signature-overlay img').evaluate(async (image: HTMLImageElement) => {
    await image.decode(); const canvas = document.createElement('canvas'); canvas.width = image.naturalWidth; canvas.height = image.naturalHeight;
    const context = canvas.getContext('2d')!; context.drawImage(image, 0, 0);
    const data = context.getImageData(0, 0, canvas.width, canvas.height).data;
    return { transparent: data.some((value, index) => index % 4 === 3 && value === 0), visible: data.some((value, index) => index % 4 === 3 && value > 0) };
  });
  expect(alpha).toEqual({ transparent: true, visible: true });
  await page.getByLabel('Left (%)').fill('25'); await page.getByLabel('Left (%)').press('Tab');
  await page.getByTestId('save-signature').click();
  await expect(page.getByText('Signature saved. Export a new PDF to include it.')).toBeVisible();
  expect(savedBox?.x).toBe(.25);
  await open();
  await page.getByTestId('signature-body').click();
  await expect(page.getByTestId('move-signature')).toBeVisible();
  await page.getByTestId('move-signature').click();
  await expect(page.getByLabel('Left (%)')).toHaveValue('25');
  await page.getByTestId('cancel-signature').click();
  await page.screenshot({ path: '../../.task-tools/signature-ui-desktop.png', fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByTestId('add-signature').click();
  await page.screenshot({ path: '../../.task-tools/signature-ui-mobile.png', fullPage: true });
  const upload = await page.evaluate(() => {
    const canvas = document.createElement('canvas'); canvas.width = 200; canvas.height = 80;
    const context = canvas.getContext('2d')!; context.fillStyle = '#ffffff'; context.fillRect(0, 0, 200, 80);
    context.fillStyle = '#172033'; context.fillRect(30, 30, 140, 5); return canvas.toDataURL('image/png').split(',')[1];
  });
  await page.locator('app-signature-creator input[type="file"]').setInputFiles({ name: 'signature.png', mimeType: 'image/png', buffer: Buffer.from(upload, 'base64') });
  const cornerAlpha = () => page.getByAltText('Signature after background removal').evaluate(async (image: HTMLImageElement) => {
    await image.decode(); const canvas = document.createElement('canvas'); canvas.width = image.naturalWidth; canvas.height = image.naturalHeight;
    const context = canvas.getContext('2d')!; context.drawImage(image, 0, 0); return context.getImageData(0, 0, 1, 1).data[3];
  });
  await expect.poll(cornerAlpha).toBe(0);
  await page.getByLabel('Keep original background').check(); await expect.poll(cornerAlpha).toBe(255);
  await page.getByLabel('Keep original background').uncheck(); await expect.poll(cornerAlpha).toBe(0);
});
