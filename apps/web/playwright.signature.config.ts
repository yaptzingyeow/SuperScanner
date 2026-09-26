import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e', testMatch: 'signature-ui.spec.ts', timeout: 60_000,
  use: { baseURL: 'http://127.0.0.1:4302', trace: 'off' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: { command: `"${process.execPath}" e2e/server.mjs`, url: 'http://127.0.0.1:4302',
    env: { E2E_WEB_ROOT: '../../.task-tools/signature-ui-web/browser', E2E_WEB_PORT: '4302' }, reuseExistingServer: false },
});
