import { expect, test } from '@playwright/test';
import { resolve } from 'node:path';

const photoFixture = resolve(__dirname, 'fixtures/append-photo.jpg');

test('ready page can recognize text and surface a terminal result', async ({ page }) => {
  test.setTimeout(180_000);
  test.skip(
    process.env['E2E_OCR_READY'] !== '1',
    'Requires PostgreSQL, API, Worker, private object storage, and Fake OCR configuration.',
  );

  await page.goto('/e2e-login?user=user-a');
  await page.getByLabel('Document title').fill('OCR acceptance');
  await page.getByLabel('Choose file').setInputFiles(photoFixture);
  await page.getByRole('button', { name: 'Upload securely' }).click();
  await page.getByRole('link', { name: 'Open document preview' }).click();

  const pageCard = page.locator('app-page-card').first();
  await expect(pageCard.getByRole('link', { name: 'Crop & filters' })).toBeVisible({
    timeout: 60_000,
  });
  await pageCard.getByRole('link', { name: 'Crop & filters' }).click();
  const save = page.getByRole('button', { name: 'Save scan' });
  await expect(save).toBeEnabled({ timeout: 60_000 });
  await save.click();

  await expect(page.getByText(/words recognized/)).toBeVisible({ timeout: 60_000 });
  await expect(page.getByRole('button', { name: 'Retry text recognition' })).toHaveCount(0);
});
