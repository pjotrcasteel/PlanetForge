import { expect, test } from '@playwright/test';

test.setTimeout(180_000);

test('RegionalRock_PhaseDrivenGlobe_AdvancesConservedMaterialWithoutFictionalCrust', async ({ page }, testInfo) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/?visualTest=1&regionalRock=1');
  await expect(page.getByTestId('planet-scenario-label')).toHaveText('REGIONAL ROCK · CONSERVED PHASE REFERENCE', { timeout: 45_000 });
  await expect(page.getByTestId('regional-temperature-range')).toBeVisible();
  await expect(page.getByTestId('regional-melt-range')).toBeVisible();
  await expect(page.getByText('DECLARED THERMAL CONTRAST · NOT CRUST')).toBeVisible();
  await expect(page.getByTestId('airless-elapsed')).toContainText('T+0h');
  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest) || Boolean(window.__planetForgeSurfaceError), null, { timeout: 45_000 });
  expect(await page.evaluate(() => window.__planetForgeSurfaceError ?? null)).toBeNull();

  const before = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
  expect(before.planetPixels).toBeGreaterThan(10_000);
  expect(before.glError).toBe(0);
  const initialReadout = await page.getByTestId('regional-temperature-range').textContent();
  await page.screenshot({ path: testInfo.outputPath('regional-rock-melting.png') });

  await page.getByTestId('airless-advance').click();
  await expect(page.getByTestId('airless-elapsed')).toContainText('T+1h', { timeout: 90_000 });
  await expect(page.getByTestId('airless-status')).toContainText('ACCEPTED');
  await expect(page.getByTestId('regional-temperature-range')).not.toHaveText(initialReadout);
  const after = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
  expect(after.planetPixels).toBeGreaterThan(10_000);
  expect(after.glError).toBe(0);
  await page.screenshot({ path: testInfo.outputPath('regional-rock-cooling.png') });

  await page.getByTestId('airless-reset').click();
  await expect(page.getByTestId('airless-elapsed')).toContainText('T+0h', { timeout: 45_000 });
  await expect(page.getByTestId('regional-temperature-range')).toHaveText(initialReadout);
  expect(errors).toEqual([]);
});

test('RegionalRock_ExistingOneZoneSimulation_CanSwitchWithoutChangingMainPlanet', async ({ page }) => {
  await page.goto('/?visualTest=1');
  await expect(page.getByTestId('airless-enter')).toBeVisible({ timeout: 45_000 });
  await page.getByTestId('airless-enter').click();
  await expect(page.getByTestId('airless-surface-temperature')).toHaveText('SURFACE 1600 K');
  await page.getByTestId('regional-switch').click();
  await expect(page.getByTestId('regional-temperature-range')).toBeVisible({ timeout: 45_000 });
  await expect(page.getByTestId('planet-scenario-label')).toHaveText('REGIONAL ROCK · CONSERVED PHASE REFERENCE');

  await page.getByTestId('regional-switch').click();
  await expect(page.getByTestId('airless-surface-temperature')).toHaveText('SURFACE 1600 K');
  await page.getByTestId('airless-exit').click();
  await expect(page.getByText('SNOWBALL EXPERIMENT', { exact: true })).toBeVisible();
});
