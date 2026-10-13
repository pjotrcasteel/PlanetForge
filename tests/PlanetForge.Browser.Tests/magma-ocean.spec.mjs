import { expect, test } from '@playwright/test';

test.setTimeout(180_000);

test('MagmaOcean_InitialGlobalMelt_UsesCanonicalPhaseAndAdvancesOnSameMainGlobe', async ({ page }, testInfo) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/?visualTest=1&magmaOcean=1');
  await expect(page.getByTestId('planet-scenario-label'))
    .toHaveText('MAGMA OCEAN · CONSERVED MOLTEN REFERENCE', { timeout: 45_000 });
  await expect(page.getByTestId('regional-melt-range')).toHaveText('MELT 100%–100%');
  await expect(page.getByText('HYPOTHETICAL MOLTEN SURFACE · NOT CRUST')).toBeVisible();
  await expect(page.getByTestId('airless-elapsed')).toContainText('T+0h');
  await expect(page.getByTestId('airless-advance')).toBeVisible();
  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest) || Boolean(window.__planetForgeSurfaceError), null, { timeout: 45_000 });
  expect(await page.evaluate(() => window.__planetForgeSurfaceError ?? null)).toBeNull();

  const initial = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
  expect(initial.glError).toBe(0);
  expect(initial.planetPixels).toBeGreaterThan(10_000);
  const firstTemperature = await page.getByTestId('regional-temperature-range').textContent();
  await page.screenshot({ path: testInfo.outputPath('magma-ocean-initial-molten.png') });

  await page.getByTestId('airless-advance').click();
  await expect(page.getByTestId('airless-elapsed')).toContainText('T+1h', { timeout: 90_000 });
  await expect(page.getByTestId('airless-status')).toContainText('ACCEPTED');
  await expect(page.getByTestId('regional-temperature-range')).not.toHaveText(firstTemperature);
  const after = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
  expect(after.glError).toBe(0);
  expect(after.planetPixels).toBeGreaterThan(10_000);
  await page.screenshot({ path: testInfo.outputPath('magma-ocean-after-one-hour.png') });

  await page.getByTestId('airless-reset').click();
  await expect(page.getByTestId('regional-melt-range')).toHaveText('MELT 100%–100%');
  await expect(page.getByTestId('airless-elapsed')).toContainText('T+0h');
  expect(errors).toEqual([]);
});

test('MagmaOcean_SwitchUniformAndBack_PreservesMoltenScenarioChoice', async ({ page }) => {
  await page.goto('/?visualTest=1&magmaOcean=1');
  await expect(page.getByTestId('regional-melt-range')).toHaveText('MELT 100%–100%', { timeout: 45_000 });
  await page.getByTestId('regional-switch').click();
  await expect(page.getByTestId('airless-surface-temperature')).toHaveText('SURFACE 1600 K');
  await expect(page.getByTestId('regional-switch')).toHaveText('MAGMA OCEAN REFERENCE');
  await page.getByTestId('regional-switch').click();
  await expect(page.getByTestId('regional-melt-range')).toHaveText('MELT 100%–100%');
  await page.getByTestId('airless-exit').click();
  await expect(page.getByText('SNOWBALL EXPERIMENT', { exact: true })).toBeVisible();
});
