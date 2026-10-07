import { expect, test } from '@playwright/test';

test.setTimeout(240_000);

test('SeedControls_ReloadUsesShowcaseAndGenerateCreatesNewWorld', async ({ page }) => {
  await page.goto('/?visualTest=1');
  const seedLabel = page.getByTestId('planet-seed');
  await expect(seedLabel).toHaveText('SEED 24061984', { timeout: 20_000 });
  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest), null, { timeout: 15_000 });
  expect(await page.evaluate(() => window.__planetForgeSurfaceTest.getSeed())).toBe(24061984);

  await page.getByRole('button', { name: 'Generate new planet' }).click();
  await expect(seedLabel).not.toHaveText('SEED 24061984', { timeout: 30_000 });
  await page.waitForFunction(() => window.__planetForgeSurfaceTest.getSeed() !== 24061984, null, { timeout: 30_000 });
  const generatedSeed = await page.evaluate(() => window.__planetForgeSurfaceTest.getSeed());
  expect(generatedSeed).toBeGreaterThan(0);

  await page.reload();
  await expect(seedLabel).toHaveText('SEED 24061984', { timeout: 20_000 });
  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest), null, { timeout: 15_000 });
  expect(await page.evaluate(() => window.__planetForgeSurfaceTest.getSeed())).toBe(24061984);
});


test('FrozenToWaterCycle_PreservesUnifiedPhysicalSurface', async ({ page }, testInfo) => {
  await page.goto('/?visualTest=1');
  await expect(page.getByText('FROZEN WORLD')).toBeVisible({ timeout: 20_000 });
  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest) || Boolean(window.__planetForgeSurfaceError), null, { timeout: 15_000 });
  const surfaceError = await page.evaluate(() => window.__planetForgeSurfaceError ?? null);
  expect(surfaceError, `Unified surface renderer failed to initialize: ${surfaceError}`).toBeNull();
  await page.waitForFunction(() => Boolean(window.__planetForgeWaterTest), null, { timeout: 15_000 });

  await orientPitch(page, 0.0);
  const frozenSurface = await measureSurface(page);
  const frozenWater = await measureWater(page);
  await page.screenshot({ path: testInfo.outputPath('year-0-frozen.png'), fullPage: true });

  await page.getByRole('button', { name: 'NEXT' }).click();
  await expect(page.getByText('MELTING WORLD', { exact: true })).toBeVisible({ timeout: 20_000 });
  await expect(page.getByRole('button', { name: 'NEXT' })).toBeEnabled({ timeout: 20_000 });

  await orientPitch(page, 0.0);
  const meltedSurface = await measureSurface(page);
  await page.screenshot({ path: testInfo.outputPath('year-50-equator.png'), fullPage: true });

  const equatorYaw = await page.evaluate(() => window.__planetForgeSurfaceTest.getYaw());
  await page.evaluate(yaw => window.__planetForgeSurfaceTest.setYaw(yaw + Math.PI), equatorYaw);
  await page.waitForTimeout(100);
  const oppositeSurface = await measureSurface(page);
  await page.screenshot({ path: testInfo.outputPath('year-50-opposite-limb.png'), fullPage: true });
  await page.evaluate(yaw => window.__planetForgeSurfaceTest.setYaw(yaw), equatorYaw);
  await page.waitForTimeout(100);

  await orientPitch(page, 1.25);
  const northSurface = await measureSurface(page);
  await page.screenshot({ path: testInfo.outputPath('year-50-north.png'), fullPage: true });

  await orientPitch(page, -1.25);
  const southSurface = await measureSurface(page);
  await page.screenshot({ path: testInfo.outputPath('year-50-south.png'), fullPage: true });

  await orientPitch(page, 0.0);
  await page.getByRole('button', { name: 'NEXT' }).click();
  await expect(page.getByText('ACTIVE WATER CYCLE', { exact: true })).toBeVisible({ timeout: 90_000 });
  await expect(page.getByRole('button', { name: 'NEXT' })).toBeEnabled({ timeout: 90_000 });
  await page.waitForTimeout(150);
  const activeSurface = await measureSurface(page);
  const activeWater = await measureWater(page);
  await page.screenshot({ path: testInfo.outputPath('active-water-cycle.png'), fullPage: true });
  await page.evaluate(() => {
    const overlay = document.getElementById('water-overlay-canvas');
    if (overlay) overlay.style.visibility = 'hidden';
  });
  await page.screenshot({ path: testInfo.outputPath('active-water-cycle-terrain-only.png'), fullPage: true });
  await page.evaluate(() => {
    const overlay = document.getElementById('water-overlay-canvas');
    if (overlay) overlay.style.visibility = '';
  });

  const metrics = { frozenSurface, meltedSurface, oppositeSurface, northSurface, southSurface, activeSurface, frozenWater, activeWater };
  console.log(`Foundation metrics: ${JSON.stringify(metrics)}`);
  await testInfo.attach('foundation-metrics', { body: JSON.stringify(metrics, null, 2), contentType: 'application/json' });

  expect(frozenSurface.planetPixels).toBeGreaterThan(10_000);
  expect(frozenSurface.iceFraction).toBeGreaterThan(0.45);
  expect(frozenSurface.glError).toBe(0);

  expect(meltedSurface.planetPixels).toBeGreaterThan(10_000);
  expect(meltedSurface.oceanPixels).toBeGreaterThan(1_000);
  expect(meltedSurface.oceanFraction).toBeGreaterThan(0.12);
  expect(meltedSurface.oceanLuminanceRange).toBeGreaterThan(0.04);
  expect(meltedSurface.iceFraction).toBeLessThan(frozenSurface.iceFraction);
  expect(meltedSurface.glError).toBe(0);

  expect(oppositeSurface.planetPixels).toBeGreaterThan(10_000);
  expect(oppositeSurface.glError).toBe(0);
  expect(northSurface.glError).toBe(0);
  expect(southSurface.glError).toBe(0);

  expect(frozenWater.activeRiverSegmentCount).toBe(0);
  expect(frozenWater.lakeCellCount).toBe(0);
  expect(frozenWater.visiblePixels).toBe(0);

  expect(activeSurface.oceanPixels).toBeGreaterThan(1_000);
  expect(activeSurface.glError).toBe(0);
  expect(activeWater.activeRiverSegmentCount).toBeGreaterThan(0);
  expect(activeWater.riverPointCount).toBeGreaterThan(activeWater.activeRiverSegmentCount * 4);
  expect(activeWater.terrainVertexCount).toBeGreaterThan(50_000);
  expect(activeWater.terrainSampleCount).toBeGreaterThan(activeWater.riverPointCount);
  expect(activeWater.terrainFallbackCount).toBeLessThanOrEqual(Math.max(1, Math.floor(activeWater.terrainSampleCount * 0.01)));
  expect(activeWater.visiblePixels).toBeGreaterThan(100);
  expect(activeWater.maximumAlpha).toBeGreaterThan(0.40);
  expect(activeWater.minimumRiverElevationMeters).not.toBeNull();
  expect(activeWater.maximumRiverElevationMeters).not.toBeNull();
  expect(activeWater.maximumRiverElevationMeters).toBeGreaterThan(activeWater.minimumRiverElevationMeters);
});

async function orientPitch(page, targetPitch) {
  const currentPitch = await page.evaluate(() => window.__planetForgeSurfaceTest.getPitch());
  const deltaPixels = (targetPitch - currentPitch) / 0.008;
  if (Math.abs(deltaPixels) < 1.0) return;

  const canvas = page.locator('#planet-canvas');
  const box = await canvas.boundingBox();
  if (!box) throw new Error('Planet canvas is not visible.');

  const x = box.x + (box.width * 0.5);
  const y = box.y + (box.height * 0.5);
  const targetY = Math.max(box.y + 20, Math.min(box.y + box.height - 20, y + deltaPixels));

  await page.mouse.move(x, y);
  await page.mouse.down();
  await page.mouse.move(x, targetY, { steps: 12 });
  await page.mouse.up();
  await page.waitForTimeout(100);

  const actualPitch = await page.evaluate(() => window.__planetForgeSurfaceTest.getPitch());
  expect(Math.abs(actualPitch - targetPitch)).toBeLessThan(0.05);
}

async function measureSurface(page) {
  return page.evaluate(() => window.__planetForgeSurfaceTest.measure());
}

async function measureWater(page) {
  return page.evaluate(() => window.__planetForgeWaterTest.measure());
}
