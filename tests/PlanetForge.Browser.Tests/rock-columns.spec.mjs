import { expect, test } from '@playwright/test';

test.setTimeout(240_000);

test('RockColumns_PhysicalSolidTopOverMoltenDepth_UsesSharedTerrainAndResets', async ({ page }, testInfo) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/?visualTest=1&rockColumns=1');
  await expect(page.getByTestId('planet-scenario-label')).toHaveText('ROCK COLUMNS · SURFACE AND DEPTH', { timeout: 60_000 });
  await expect(page.getByTestId('regional-melt-range')).toHaveText('MELT 100%–100%', { timeout: 60_000 });
  await expect(page.getByTestId('column-solid-thickness')).toHaveText('SOLID TOP 0.00–0.00 m');
  await expect(page.getByTestId('column-deep-melt')).toHaveText('DEEP MELT 100%–100%');
  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest) || Boolean(window.__planetForgeSurfaceError), null, { timeout: 60_000 });
  expect(await page.evaluate(() => window.__planetForgeSurfaceError ?? null)).toBeNull();
  const key = await page.evaluate(() => window.__planetForgeSurfaceTest.getTerrainGeometryKey());
  expect((await page.evaluate(() => window.__planetForgeSurfaceTest.measure())).glError).toBe(0);
  await page.screenshot({ path: testInfo.outputPath('columns-molten-surface.png') });
  for (let hour = 1; hour <= 4; hour++) {
    await page.getByTestId('airless-advance').click();
    await expect(page.getByTestId('airless-elapsed')).toContainText(`T+${hour}h`, { timeout: 60_000 });
    await expect(page.getByTestId('airless-status')).toHaveText('ACCEPTED');
  }
  await expect(page.getByTestId('regional-melt-range')).toHaveText('MELT 0%–0%');
  await expect(page.getByTestId('column-solid-thickness')).not.toHaveText('SOLID TOP 0.00–0.00 m');
  await expect(page.getByTestId('column-deep-melt')).toHaveText('DEEP MELT 100%–100%');
  expect(await page.evaluate(() => window.__planetForgeSurfaceTest.getTerrainGeometryKey())).not.toBe(key);
  expect((await page.evaluate(() => window.__planetForgeSurfaceTest.measure())).glError).toBe(0);
  await expect(page.getByTestId('airless-advance')).toBeInViewport();
  await page.screenshot({ path: testInfo.outputPath('columns-solid-top-molten-depth.png') });
  await page.getByTestId('airless-reset').click();
  await expect(page.getByTestId('regional-melt-range')).toHaveText('MELT 100%–100%', { timeout: 60_000 });
  await expect(page.getByTestId('column-solid-thickness')).toHaveText('SOLID TOP 0.00–0.00 m');
  await page.getByTestId('regional-switch').click();
  await expect(page.getByTestId('airless-surface-temperature')).toHaveText('SURFACE 1600 K');
  await page.getByTestId('regional-switch').click();
  await expect(page.getByTestId('planet-scenario-label')).toHaveText('ROCK COLUMNS · SURFACE AND DEPTH');
  await expect(page.getByTestId('column-solid-thickness')).toHaveText('SOLID TOP 0.00–0.00 m');
  expect(errors).toEqual([]);
});

test('RockColumns_OpticalProjection_UsesTemperatureForSolidAndLiquidEmission', async ({ page }) => {
  await page.goto('/?visualTest=1&rockColumns=1');
  const radiance = await page.evaluate(async () => {
    const optics = await import('/js/thermalRadiance.js');
    return {
      cold: optics.emissionDisplayLinear(300),
      warm: optics.emissionDisplayLinear(1200),
      hot: optics.emissionDisplayLinear(2200),
      ratio: optics.spectralRadiance(550, 2000) / optics.spectralRadiance(550, 1000)
    };
  });
  expect(Math.max(...radiance.cold)).toBeLessThan(1e-15);
  expect(radiance.hot.every((channel, i) => channel > radiance.warm[i])).toBe(true);
  expect(radiance.ratio).toBeGreaterThan(400_000);
  expect(radiance.ratio).toBeLessThan(600_000);
});
