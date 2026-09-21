import { expect, test } from '@playwright/test';

test('multi-page document can append, reorder, export, and download', async ({ page }) => {
  test.skip(
    process.env['E2E_IMPORT_EXPORT_READY'] !== '1',
    'Requires PostgreSQL, Worker, Poppler, private object storage, and the three-page fixture.',
  );
  await page.goto('/e2e-login?user=user-a');
  await page.getByRole('button', { name: 'New Scan' }).click();
  await page.getByLabel('Document title').fill('Four page acceptance');
  await page.getByLabel('Choose file').setInputFiles('e2e/fixtures/three-page.pdf');
  await page.getByRole('button', { name: 'Upload securely' }).click();
  await expect(page.locator('app-page-card')).toHaveCount(3, { timeout: 60_000 });
  await page.getByRole('button', { name: 'Add pages' }).click();
  await page.getByLabel('Choose photos or PDFs').setInputFiles('e2e/fixtures/append-photo.jpg');
  await page.getByRole('button', { name: 'Add selected files' }).click();
  await expect(page.locator('app-page-card')).toHaveCount(4, { timeout: 60_000 });
  await page.locator('app-page-card').nth(3).dragTo(page.locator('app-page-card').nth(1));
  page.once('dialog', (dialog) => dialog.accept());
  await page.getByRole('button', { name: 'Generate PDF' }).click();
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download PDF' }).click();
  expect((await download).suggestedFilename()).toBe('Four page acceptance.pdf');
});
