import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: '.',
  timeout: 60_000,
  // Allow Playwright to distribute independent test cases, not just whole files,
  // across CI shards. Each runner still uses one browser worker.
  fullyParallel: true,
  workers: 1,
  use: {
    baseURL: process.env.PLANETFORGE_BASE_URL ?? 'http://127.0.0.1:4173',
    viewport: { width: 390, height: 844 },
    deviceScaleFactor: 1,
    colorScheme: 'dark',
    screenshot: 'only-on-failure'
  },
  reporter: [['line']],
  outputDir: 'artifacts/test-results'
});
