import { expect, test } from '@playwright/test';

test.setTimeout(120_000);

test.beforeEach(async ({ page }) => {
  page.on('pageerror', error => console.log('PlanetForge browser exception:', error.message));
  page.on('console', message => {
    if (message.type() === 'error' || message.type() === 'warning') {
      console.log('PlanetForge browser console:', message.text());
    }
  });
});

test('PlanetStages_ReloadUsesShowcaseAndGenerateCreatesNewWorld', async ({ page }) => {
  await page.goto('/?visualTest=1');
  const seedLabel = page.getByTestId('planet-seed');
  await expect(page.getByText('FROZEN WORLD', { exact: true })).toBeVisible({ timeout: 20_000 });
  await expect(page.getByRole('button', { name: 'NEXT' })).toBeVisible();
  await expect(seedLabel).toHaveText('SEED 24061984', { timeout: 20_000 });
  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest) || Boolean(window.__planetForgeSurfaceError), null, { timeout: 15_000 });

  const surfaceError = await page.evaluate(() => window.__planetForgeSurfaceError ?? null);
  expect(surfaceError, `Planet renderer failed to initialize: ${surfaceError}`).toBeNull();
  const showcase = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
  expect(showcase.planetPixels).toBeGreaterThan(10_000);
  expect(showcase.edgePlanetPixels).toBe(0);
  expect(showcase.glError).toBe(0);

  await page.getByRole('button', { name: 'Generate new planet' }).click();
  await expect(seedLabel).not.toHaveText('SEED 24061984', { timeout: 30_000 });
  await page.waitForFunction(() => window.__planetForgeSurfaceTest.getSeed() !== 24061984, null, { timeout: 30_000 });

  await page.reload();
  await expect(seedLabel).toHaveText('SEED 24061984', { timeout: 20_000 });
});

test('GeneratorQualityGate_RendersMeltedShowcaseFromMultipleAngles', async ({ page }, testInfo) => {
  await page.goto('/?visualTest=1');
  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest), null, { timeout: 15_000 });
  await page.getByRole('button', { name: 'NEXT' }).click();
  await expect(page.getByText('MELTING WORLD', { exact: true })).toBeVisible({ timeout: 45_000 });

  const views = [
    ['equator', 0.0, 0.0],
    ['opposite', Math.PI, 0.0],
    ['north', 0.0, 1.20],
    ['south', 0.0, -1.20],
  ];

  for (const [name, yaw, pitch] of views) {
    await page.evaluate(({ yaw, pitch }) => {
      window.__planetForgeSurfaceTest.setYaw(yaw);
      window.__planetForgeSurfaceTest.setPitch(pitch);
    }, { yaw, pitch });
    await page.waitForTimeout(120);
    const metrics = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
    expect(metrics.planetPixels).toBeGreaterThan(10_000);
    expect(metrics.glError).toBe(0);
    await page.screenshot({ path: testInfo.outputPath(`generator-quality-${name}.png`), fullPage: true });
  }
});


test('TerrainLab_DisplaysCanonicalGeologicalLayers', async ({ page }, testInfo) => {
  await page.goto('/?terrainLab=1');
  await expect(page.getByRole('heading', { name: /Terrain Lab/ })).toBeVisible({ timeout: 20_000 });
  await expect(page.locator('#planetforge-build-badge')).toBeHidden();
  await page.waitForFunction(() => window.__planetForgeTerrainLabReady === true, null, { timeout: 90_000 });
  await expect(page.getByRole('status')).toContainText('Seed 24061984', { timeout: 10_000 });
  for (const name of ['lab-crust', 'lab-tectonic', 'lab-mountains', 'lab-elevation']) {
    const metrics = await page.locator(`#${name}`).evaluate(canvas => {
      const data = canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height).data;
      let minimum = 255;
      let maximum = 0;
      for (let i = 0; i < data.length; i += 4) {
        minimum = Math.min(minimum, data[i]);
        maximum = Math.max(maximum, data[i]);
      }
      return { width: canvas.width, height: canvas.height, range: maximum - minimum };
    });
    expect(metrics.width).toBe(256);
    expect(metrics.height).toBe(128);
    expect(metrics.range).toBeGreaterThan(12);
  }
  await page.screenshot({ path: testInfo.outputPath('terrain-lab-showcase.png'), fullPage: true });
});

for (const seed of [24061984, 346147916, 579460630]) {
  test(`GeneratorQualityGate_Seed${seed}_RendersMultipleOrbitalAngles`, async ({ page }, testInfo) => {
    await page.goto(`/?visualTest=1&seed=${seed}`);
    await expect(page.getByTestId('planet-seed')).toHaveText(`SEED ${seed}`, { timeout: 30_000 });
    await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest) || Boolean(window.__planetForgeSurfaceError), null, { timeout: 40_000 });
    expect(await page.evaluate(() => window.__planetForgeSurfaceError ?? null)).toBeNull();
    await page.getByRole('button', { name: 'NEXT' }).click();
    await expect(page.getByText('MELTING WORLD', { exact: true })).toBeVisible({ timeout: 50_000 });

    for (const [name, yaw, pitch] of [['front', 0, 0.1], ['back', Math.PI, -0.2], ['polar', 0.4, 1.1]]) {
      await page.evaluate(({ yaw, pitch }) => {
        window.__planetForgeSurfaceTest.setYaw(yaw);
        window.__planetForgeSurfaceTest.setPitch(pitch);
      }, { yaw, pitch });
      const metrics = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
      expect(metrics.planetPixels).toBeGreaterThan(10_000);
      expect(metrics.edgePlanetPixels).toBe(0);
      expect(metrics.glError).toBe(0);
      await page.screenshot({ path: testInfo.outputPath(`terrain-seed-${seed}-${name}.png`), fullPage: true });
    }
    await page.evaluate(() => window.__planetForgeSurfaceTest.setDistance(3.8));
    const closeMetrics = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
    expect(closeMetrics.planetPixels).toBeGreaterThan(10_000);
    expect(closeMetrics.glError).toBe(0);
    await page.screenshot({ path: testInfo.outputPath(`terrain-seed-${seed}-regional.png`), fullPage: true });
  });
}

test('OrbitalZoom_RequestsHigherLodAndRestoresCoarseGlobe', async ({ page }, testInfo) => {
  await page.goto('/?visualTest=1');
  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest), null, { timeout: 30_000 });

  const initial = await page.evaluate(() => window.__planetForgeSurfaceTest.getLodStats());
  expect(initial.mode).toBe('globe');
  expect(initial.tileCount).toBe(24);
  expect(initial.maximumLevel).toBe(1);

  await page.evaluate(() => window.__planetForgeSurfaceTest.setDistance(1.22));
  await page.waitForFunction(() => window.__planetForgeSurfaceTest.getLodStats().maximumLevel > 1, null, { timeout: 75_000 });
  const regional = await page.evaluate(() => window.__planetForgeSurfaceTest.getLodStats());
  expect(regional.tileCount).toBeLessThanOrEqual(56);

  await page.evaluate(() => window.__planetForgeSurfaceTest.setDistance(1.03));
  await page.waitForFunction(level => window.__planetForgeSurfaceTest.getLodStats().maximumLevel > level, regional.maximumLevel, { timeout: 75_000 });
  const close = await page.evaluate(() => window.__planetForgeSurfaceTest.getLodStats());
  expect(close.mode).toBe('globe');
  expect(close.tileCount).toBeLessThanOrEqual(56);
  const closePixels = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
  expect(closePixels.planetPixels).toBeGreaterThan(10_000);
  expect(closePixels.glError).toBe(0);
  await page.screenshot({ path: testInfo.outputPath('adaptive-orbital-lod-close.png'), fullPage: true });

  await page.evaluate(() => window.__planetForgeSurfaceTest.setDistance(3.2));
  await page.waitForFunction(() => window.__planetForgeSurfaceTest.getLodStats().maximumLevel === 1, null, { timeout: 75_000 });
  const restored = await page.evaluate(() => window.__planetForgeSurfaceTest.getLodStats());
  expect(restored.tileCount).toBe(24);
});
