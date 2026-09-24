import { expect, test } from '@playwright/test';
import { execFileSync, spawnSync } from 'node:child_process';
import { copyFileSync } from 'node:fs';

async function createSanitizedFixture(page: import('@playwright/test').Page, path: string) {
  await page.setContent(`
    <main style="box-sizing:border-box;width:900px;height:1200px;padding:120px;background:white;color:#111;font:42px Arial">
      <h1 style="font-size:48px">SANITIZED TEST FORM</h1>
      <p>Name: Yap Tzing Yeow</p><p>Reference: TEST-ONLY-2026</p>
    </main>`);
  await page.locator('main').screenshot({ path });
}

async function prepareReadyPages(page: import('@playwright/test').Page, count: number) {
  for (let index = 0; index < count; index++) {
    const card = page.locator('app-page-card').filter({ has: page.locator('[data-state="NeedsCrop"]') }).first();
    await card.getByRole('link', { name: 'Crop & filters' }).click();
    await page.getByRole('button', { name: 'Save scan' }).click();
    await expect(page.locator('[data-state="Ready"]')).toHaveCount(index + 1, { timeout: 60_000 });
  }
}

async function downloadPdf(page: import('@playwright/test').Page) {
  page.once('dialog', dialog => dialog.accept());
  await page.getByRole('button', { name: 'Generate PDF' }).click();
  await expect(page.getByRole('button', { name: 'Download PDF' })).toBeVisible({ timeout: 120_000 });
  const pending = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download PDF' }).click();
  const path = await (await pending).path();
  expect(path).not.toBeNull();
  return path!;
}

test('searchable export preserves word order, location, and rendered pixels', async ({ page }, testInfo) => {
  test.setTimeout(600_000);
  test.skip(process.env['E2E_SEARCHABLE_PDF_READY'] !== '1',
    'Requires PostgreSQL, API, Worker, R2, Google Document OCR, Poppler, and ImageMagick compare.');
  const fixture = testInfo.outputPath('sanitized-searchable-form.png');
  await createSanitizedFixture(page, fixture);
  await page.goto('/e2e-login?user=user-a');
  await page.getByLabel('Document title').fill('Sanitized searchable acceptance');
  await page.getByLabel('Choose file').setInputFiles(fixture);
  await page.getByRole('button', { name: 'Upload securely' }).click();
  await page.getByRole('link', { name: 'Open document preview' }).click();
  await prepareReadyPages(page, 1);
  await page.getByRole('button', { name: 'Recognize text' }).click();
  await expect(page.getByText(/text elements recognized/)).toBeVisible({ timeout: 180_000 });
  const pdf = await downloadPdf(page);
  await expect(page.getByText('The page has searchable text.')).toBeVisible();

  const text = execFileSync('pdftotext', ['-layout', pdf, '-'], { encoding: 'utf8' });
  expect(text).toContain('Yap Tzing Yeow');
  expect(text.indexOf('Yap')).toBeLessThan(text.indexOf('Tzing'));
  expect(text.indexOf('Tzing')).toBeLessThan(text.indexOf('Yeow'));
  const bbox = execFileSync('pdftotext', ['-bbox-layout', pdf, '-'], { encoding: 'utf8' });
  expect(bbox).toMatch(/<word xMin="[^"]+" yMin="[^"]+"[^>]*>Yap<\/word>/);

  const expected = process.env['E2E_PROCESSED_IMAGE_PATH'];
  expect(expected, 'E2E_PROCESSED_IMAGE_PATH must point to the exact processed page image').toBeTruthy();
  const renderedBase = testInfo.outputPath('rendered');
  execFileSync('pdftoppm', ['-f', '1', '-singlefile', '-r', '96', '-png', pdf, renderedBase]);
  const comparison = testInfo.outputPath('pixel-diff.png');
  const pixelComparison = spawnSync(
    'compare', ['-metric', 'RMSE', expected!, `${renderedBase}.png`, comparison], { encoding: 'utf8' });
  expect(pixelComparison.status, pixelComparison.stderr).toBe(0);
  expect(pixelComparison.stderr.trim()).toMatch(/^0(?:\.0+)?/);
});

test('mixed OCR readiness exports a partially searchable PDF', async ({ page }, testInfo) => {
  test.setTimeout(600_000);
  test.skip(process.env['E2E_SEARCHABLE_PDF_READY'] !== '1', 'Requires the searchable PDF integration stack.');
  const first = testInfo.outputPath('first.png');
  const second = testInfo.outputPath('second.png');
  await createSanitizedFixture(page, first);
  copyFileSync(first, second);
  await page.goto('/e2e-login?user=user-a');
  await page.getByLabel('Document title').fill('Mixed searchable acceptance');
  await page.getByLabel('Choose file').setInputFiles([first, second]);
  await page.getByRole('button', { name: 'Upload securely' }).click();
  await page.getByRole('link', { name: 'Open document preview' }).click();
  await prepareReadyPages(page, 2);
  await page.getByRole('button', { name: 'Recognize text' }).first().click();
  await expect(page.getByText(/text elements recognized/).first()).toBeVisible({ timeout: 180_000 });
  await downloadPdf(page);
  await expect(page.getByText('1 of 2 pages have searchable text.')).toBeVisible();
});
