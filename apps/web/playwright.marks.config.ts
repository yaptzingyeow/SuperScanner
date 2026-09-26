import { defineConfig, devices } from '@playwright/test';

// Isolated UI-flow config: API responses are mocked by page-marks.spec.ts.
export default defineConfig({
  testDir: './e2e',
  testMatch: 'page-marks.spec.ts',
  timeout: 60_000,
  reporter: 'list',
  use: { baseURL: 'http://127.0.0.1:4300', trace: 'off' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
