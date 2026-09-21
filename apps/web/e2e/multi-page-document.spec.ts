import { expect, test } from '@playwright/test';
import { execFileSync } from 'node:child_process';

test('multi-page document can append, reorder, export, and download', async ({ page }) => {
  test.setTimeout(600_000);
  test.skip(
    process.env['E2E_IMPORT_EXPORT_READY'] !== '1',
    'Requires PostgreSQL, Worker, Poppler, private object storage, and the three-page fixture.',
  );
  await page.goto('/e2e-login?user=user-a');
  await page.getByLabel('Document title').fill('Four page acceptance');
  await page.getByLabel('Choose file').setInputFiles('e2e/fixtures/three-page.pdf');
  await page.getByRole('button', { name: 'Upload securely' }).click();
  await page.getByRole('link', { name: 'Open document preview' }).click();
  await expect(page.locator('app-page-card')).toHaveCount(3, { timeout: 60_000 });
  await page.getByRole('button', { name: 'Add pages' }).click();
  await page.getByLabel('Choose photos or PDFs').setInputFiles('e2e/fixtures/append-photo.jpg');
  await page.getByRole('button', { name: 'Add selected files' }).click();
  await expect(page.locator('app-page-card')).toHaveCount(4, { timeout: 180_000 });

  for (let pageNumber = 0; pageNumber < 4; pageNumber++) {
    const nextPage = page
      .locator('app-page-card')
      .filter({ has: page.locator('[data-state="NeedsCrop"]') })
      .first();
    await expect(nextPage).toBeVisible({ timeout: 180_000 });
    await nextPage.getByRole('link', { name: 'Crop & filters' }).click();
    await page.getByRole('button', { name: 'Save scan' }).click();
    await expect(page.locator('[data-state="Ready"]')).toHaveCount(pageNumber + 1, {
      timeout: 60_000,
    });
  }

  const cropLinks = page.getByRole('link', { name: 'Crop & filters' });
  const orderBefore = await cropLinks.evaluateAll((links) => links.map((link) => link.getAttribute('href')));
  const dragHandle = page
    .locator('app-page-card')
    .nth(3)
    .getByRole('button', { name: 'Drag page to reorder' });
  const source = await dragHandle.boundingBox();
  const target = await page.locator('app-page-card').nth(1).boundingBox();
  expect(source).not.toBeNull();
  expect(target).not.toBeNull();
  await page.mouse.move(source!.x + source!.width / 2, source!.y + source!.height / 2);
  await page.mouse.down();
  await page.mouse.move(target!.x + target!.width / 2, target!.y + target!.height / 2, {
    steps: 12,
  });
  await page.mouse.up();
  await expect(page.getByText('Page order saved.')).toBeVisible();
  await expect.poll(() => cropLinks.evaluateAll((links) => links.map((link) => link.getAttribute('href'))))
    .toEqual([orderBefore[0], orderBefore[3], orderBefore[1], orderBefore[2]]);
  page.once('dialog', (dialog) => dialog.accept());
  await page.getByRole('button', { name: 'Generate PDF' }).click();
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download PDF' }).click();
  const exportedPdf = await download;
  expect(exportedPdf.suggestedFilename()).toBe('Four page acceptance.pdf');
  const exportedPath = await exportedPdf.path();
  expect(exportedPath).not.toBeNull();
  const metadata = execFileSync('pdfinfo', [exportedPath!], { encoding: 'utf8' });
  expect(metadata).toMatch(/^Pages:\s+4$/m);
});
