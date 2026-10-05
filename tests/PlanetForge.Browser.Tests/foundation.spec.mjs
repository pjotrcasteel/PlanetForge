import { expect, test } from '@playwright/test';

test('FrozenToMelting_PreservesFragmentedPolarIceAndThawsEquator', async ({ page }, testInfo) => {
  await page.goto('/?visualTest=1');
  await expect(page.getByText('FROZEN WORLD')).toBeVisible();
  await page.waitForFunction(() => Boolean(window.__planetForgeCryosphereTest));
  await page.waitForFunction(() => Boolean(window.__planetForgeOceanTest));

  await orientPitch(page, 0.0);
  const frozenEquatorCoverage = await measureCenterCoverage(page);
  await page.screenshot({ path: testInfo.outputPath('year-0-frozen.png'), fullPage: true });

  await page.getByRole('button', { name: 'NEXT' }).click();
  await expect(page.getByText('MELTING WORLD', { exact: true })).toBeVisible({ timeout: 20_000 });
  await expect(page.getByRole('button', { name: 'NEXT' })).toBeEnabled({ timeout: 20_000 });

  await orientPitch(page, 0.0);
  const meltedEquatorCoverage = await measureCenterCoverage(page);
  const oceanDepthVariation = await page.evaluate(() => window.__planetForgeOceanTest.measureDepthVariation());
  await page.screenshot({ path: testInfo.outputPath('year-50-equator.png'), fullPage: true });

  await orientPitch(page, 1.25);
  const northPoleCoverage = await measureCenterCoverage(page);
  const northPolarRegionCoverage = await measurePolarRegionCoverage(page);
  const northFragmentation = await measureFragmentation(page);
  await page.screenshot({ path: testInfo.outputPath('year-50-north.png'), fullPage: true });

  await orientPitch(page, -1.25);
  const southPoleCoverage = await measureCenterCoverage(page);
  const southPolarRegionCoverage = await measurePolarRegionCoverage(page);
  const southFragmentation = await measureFragmentation(page);
  await page.screenshot({ path: testInfo.outputPath('year-50-south.png'), fullPage: true });

  const metrics = {
    frozenEquatorCoverage,
    meltedEquatorCoverage,
    oceanDepthVariation,
    northPoleCoverage,
    southPoleCoverage,
    northPolarRegionCoverage,
    southPolarRegionCoverage,
    northFragmentation,
    southFragmentation
  };
  console.log(`Foundation metrics: ${JSON.stringify(metrics)}`);
  await testInfo.attach('foundation-metrics', { body: JSON.stringify(metrics, null, 2), contentType: 'application/json' });

  expect(frozenEquatorCoverage).toBeGreaterThan(0.80);
  expect(meltedEquatorCoverage).toBeLessThan(0.15);

  expect(oceanDepthVariation.sampledPixels).toBeGreaterThan(1000);
  expect(oceanDepthVariation.luminanceRange).toBeGreaterThan(0.08);

  expect(northPolarRegionCoverage).toBeGreaterThan(0.04);
  expect(southPolarRegionCoverage).toBeGreaterThan(0.08);
  expect(northPolarRegionCoverage).toBeLessThan(0.55);
  expect(southPolarRegionCoverage).toBeLessThan(0.55);

  expect(northFragmentation.transitions).toBeGreaterThan(20);
  expect(southFragmentation.transitions).toBeGreaterThan(20);
  expect(northFragmentation.occupiedFraction).toBeGreaterThan(0.04);
  expect(southFragmentation.occupiedFraction).toBeGreaterThan(0.08);
  expect(northFragmentation.occupiedFraction).toBeLessThan(0.60);
  expect(southFragmentation.occupiedFraction).toBeLessThan(0.60);

  expect(northPoleCoverage).toBeLessThan(0.80);
  expect(southPoleCoverage).toBeLessThan(0.80);
});

async function orientPitch(page, targetPitch) {
  const currentPitch = await page.evaluate(() => window.__planetForgeCryosphereTest.getPitch());
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

  const actualPitch = await page.evaluate(() => window.__planetForgeCryosphereTest.getPitch());
  expect(Math.abs(actualPitch - targetPitch)).toBeLessThan(0.05);
}

async function measureCenterCoverage(page) {
  return page.evaluate(() => window.__planetForgeCryosphereTest.measureCenterCoverage());
}

async function measurePolarRegionCoverage(page) {
  return page.evaluate(() => window.__planetForgeCryosphereTest.measurePolarRegionCoverage());
}

async function measureFragmentation(page) {
  return page.evaluate(() => window.__planetForgeCryosphereTest.measureFragmentation());
}
