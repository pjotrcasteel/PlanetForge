import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: '.',
  timeout: 60_000,
  // Each CI shard owns a disjoint, audited set of complete spec files.
  // Keep tests in each file serial for stable WebGL resource bounds.
  fullyParallel: false,
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
