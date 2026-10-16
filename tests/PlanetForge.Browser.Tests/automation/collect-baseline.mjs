import { chromium } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';

const OUTPUT = process.env.PLANETFORGE_PERF_OUTPUT ?? 'artifacts/performance-baseline.json';
const BASE_URL = process.env.PLANETFORGE_BASE_URL ?? 'https://pjotrcasteel.github.io/PlanetForge/';
const SEED = 24061984;
const profiles = [
  { id: 'desktop-chromium-swiftshader', viewport: { width: 960, height: 600 }, deviceScaleFactor: 1, isMobile: false, hasTouch: false },
  { id: 'mobile-emulated-chromium-swiftshader', viewport: { width: 390, height: 844 }, deviceScaleFactor: 1, isMobile: true, hasTouch: true },
];

async function capture(profile) {
  let browser;
  try {
    browser = await chromium.launch({ headless: true, args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-webgl', '--enable-unsafe-swiftshader'] });
    const context = await browser.newContext({
      viewport: profile.viewport,
      deviceScaleFactor: profile.deviceScaleFactor,
      isMobile: profile.isMobile,
      hasTouch: profile.hasTouch,
      colorScheme: 'dark',
    });
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => { if (errors.length < 5) errors.push(error.message.slice(0, 200)); });
    const url = new URL(BASE_URL);
    url.searchParams.set('seed', String(SEED));
    url.searchParams.set('pfPerf', '1');
    const navigationStart = Date.now();
    await page.goto(url.href, { waitUntil: 'domcontentloaded', timeout: 90_000 });
    await page.getByTestId('formation-play').waitFor({ state: 'visible', timeout: 120_000 });
    await page.waitForFunction(() => {
      const report = window.__planetForgePerf?.report();
      return report?.durations?.visibleFrameIntervalMs?.count >= 5;
    }, null, { timeout: 90_000, polling: 500 });
    // Hold a stable, fixed-seed run. Production physics continues at its real
    // rate; no mocks, time skipping, synthetic frames or extra readPixels.
    await page.waitForTimeout(20_000);
    const report = await page.evaluate(() => window.__planetForgePerf?.report() ?? null);
    if (!report?.enabled || !report.durations?.visibleFrameIntervalMs?.count) throw new Error('Missing active profiler frame samples');
    const buildBadge = await page.locator('#planetforge-build-badge').textContent({ timeout: 10_000 }).catch(() => null);
    const result = {
      id: profile.id, status: 'success', runner: 'GitHub Actions ubuntu-latest Chromium SwiftShader',
      actualIphoneSafari: false, viewport: profile.viewport, deviceScaleFactor: profile.deviceScaleFactor,
      browserVersion: browser.version(), seed: SEED,
      buildBadge: buildBadge?.trim() ?? null, startupToReadyWallMs: Date.now() - navigationStart - 20_000,
      report, pageErrors: errors,
    };
    await context.close();
    return result;
  } catch (error) {
    return {
      id: profile.id, status: 'failed', runner: 'GitHub Actions ubuntu-latest Chromium SwiftShader',
      actualIphoneSafari: false, viewport: profile.viewport, seed: SEED, error: String(error).slice(0, 600),
    };
  } finally {
    if (browser) await browser.close();
  }
}

const createdAt = new Date().toISOString();
const measurements = [];
for (const profile of profiles) {
  console.log('Collecting performance baseline: ' + profile.id);
  const measurement = await capture(profile);
  console.log(profile.id + ': ' + measurement.status);
  measurements.push(measurement);
}
const bundle = {
  schemaVersion: 1, collectedAt: createdAt, collectorSourceSha: process.env.GITHUB_SHA ?? null,
  methodology: 'Published GitHub Pages application, opt-in JS metrics, 20s per profile after renderer ready',
  environment: 'GitHub Actions hosted Ubuntu; Chromium with software WebGL; mobile viewport emulation is NOT iPhone Safari',
  measurements,
};
await mkdir(join(OUTPUT, '..'), { recursive: true });
await writeFile(OUTPUT, JSON.stringify(bundle, null, 2) + '\n', 'utf8');
console.log('Wrote ' + OUTPUT);
if (measurements.some(m => m.status !== 'success')) process.exitCode = 1;
