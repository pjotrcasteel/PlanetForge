import { chromium } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';

const OUTPUT = process.env.PLANETFORGE_PERF_OUTPUT ?? 'artifacts/performance-baseline.json';
const BASE_URL = process.env.PLANETFORGE_BASE_URL ?? 'https://pjotrcasteel.github.io/PlanetForge/';
const SEED = 24061984;
const SAMPLE_SECONDS = 90;
const SNAPSHOT_SECONDS = 15;
const MIN_INTERPRETABLE_P95_SAMPLES = 40;
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
      return report?.durations?.visibleFrameIntervalMs?.count >= 2;
    }, null, { timeout: 90_000, polling: 500 });
    const readyAt = Date.now();
    const startupReport = await page.evaluate(() => window.__planetForgePerf?.report() ?? null);

    // A slow software-GPU startup can already be in the lid era. Restart from
    // the exact same seed for EACH profile, pause, clear warmup measurements,
    // and then resume the real physics; never manipulate the simulation clock.
    const startRegime = await page.evaluate(async seed => {
      const renderer = await import(new URL('js/planetRenderer.js', document.baseURI).href);
      const metrics = await import(new URL('js/performanceMetrics.js', document.baseURI).href);
      renderer.setEvolutionPlaying(false);
      const start = renderer.restartEvolution(seed);
      if (start?.regime !== 'magma-ocean') throw new Error('Failed to reset canonical magma-ocean state');
      metrics.configurePerformanceMetrics(true);
      renderer.setEvolutionPlaying(true);
      return start.regime;
    }, SEED);

    // Record chronological snapshots, including phase and sample count. The
    // p95 of 16 samples is not useful; 90 s offers a longer same-seed window.
    // Snapshots are cumulative and MUST NOT be interpreted as per-era timings.
    const observedAt = Date.now();
    const observations = [];
    for (let elapsed = SNAPSHOT_SECONDS; elapsed <= SAMPLE_SECONDS; elapsed += SNAPSHOT_SECONDS) {
      await page.waitForTimeout(SNAPSHOT_SECONDS * 1000);
      const report = await page.evaluate(() => window.__planetForgePerf?.report() ?? null);
      if (!report?.enabled) throw new Error('Profiler switched off during collection');
      observations.push({
        wallElapsedMs: Date.now() - observedAt,
        regime: report.geologyRegime,
        frameSamples: report.durations?.visibleFrameIntervalMs?.count ?? 0,
        frameP50Ms: report.durations?.visibleFrameIntervalMs?.p50Ms ?? null,
        frameP95Ms: report.durations?.visibleFrameIntervalMs?.p95Ms ?? null,
        sceneSubmissionSamples: report.durations?.sceneDrawCpuSubmissionMs?.count ?? 0,
      });
    }
    const report = await page.evaluate(() => window.__planetForgePerf?.report() ?? null);
    if (!report?.enabled || !report.durations?.visibleFrameIntervalMs?.count) throw new Error('Missing active profiler frame samples');
    const frames = report.durations.visibleFrameIntervalMs.count;
    const observedRegimes = [...new Set([startRegime, ...observations.map(o => o.regime).filter(Boolean)])];
    const buildBadge = await page.locator('#planetforge-build-badge').textContent({ timeout: 10_000 }).catch(() => null);
    const result = {
      id: profile.id, status: 'success', runner: 'GitHub Actions ubuntu-latest Chromium SwiftShader',
      actualIphoneSafari: false, viewport: profile.viewport, deviceScaleFactor: profile.deviceScaleFactor,
      browserVersion: browser.version(), seed: SEED,
      buildBadge: buildBadge?.trim() ?? null, startupToReadyWallMs: readyAt - navigationStart,
      startupReport, startRegime, observedRegimes, observations,
      sampleQuality: frames >= MIN_INTERPRETABLE_P95_SAMPLES ? 'exploratory' : 'insufficient-frame-samples',
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
  methodology: 'Published GitHub Pages application; boot, pause, deterministic same-seed restart, opt-in metric reset, 90s continuous physics; cumulative snapshots at 15s intervals',
  environment: 'GitHub Actions hosted Ubuntu; Chromium with software WebGL; mobile viewport emulation is NOT iPhone Safari',
  measurements,
};
await mkdir(join(OUTPUT, '..'), { recursive: true });
await writeFile(OUTPUT, JSON.stringify(bundle, null, 2) + '\n', 'utf8');
console.log('Wrote ' + OUTPUT);
if (measurements.some(m => m.status !== 'success')) process.exitCode = 1;
