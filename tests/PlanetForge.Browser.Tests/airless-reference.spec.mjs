import { expect, test } from '@playwright/test';

test.setTimeout(150_000);

test('AirlessReference_MainGlobe_AdvancesPhysicalSurfaceCoolingWithoutStoryStages', async ({ page }, testInfo) => {
  await page.goto('/?visualTest=1&airlessReference=1');

  await expect(page.getByTestId('planet-scenario-label')).toHaveText('AIRLESS ROCK · CONTROLLED THERMAL REFERENCE', { timeout: 30_000 });
  await expect(page.getByTestId('airless-reference-readout')).toBeVisible();
  await expect(page.getByTestId('airless-elapsed')).toContainText('00:00 PHYSICAL TIME');
  await expect(page.getByTestId('airless-surface-temperature')).toHaveText('SURFACE 1600 K');
  await expect(page.getByTestId('airless-mantle-temperature')).toContainText('MELT');
  await expect(page.getByRole('button', { name: 'ADVANCE 1 HOUR' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'NEXT', exact: true })).toHaveCount(0);
  await expect(page.getByTestId('geology-terrain-toggle')).toHaveCount(0);

  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest) || Boolean(window.__planetForgeSurfaceError), null, { timeout: 30_000 });
  const error = await page.evaluate(() => window.__planetForgeSurfaceError ?? null);
  expect(error).toBeNull();
  const initial = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
  expect(initial.planetPixels).toBeGreaterThan(10_000);
  expect(initial.glError).toBe(0);
  await page.screenshot({ path: testInfo.outputPath('airless-reference-hot-rock.png') });

  await page.getByTestId('airless-advance').click();
  await expect(page.getByTestId('airless-elapsed')).toContainText('01:00 PHYSICAL TIME', { timeout: 45_000 });
  await expect(page.getByTestId('airless-surface-temperature')).not.toHaveText('SURFACE 1600 K');
  await expect(page.getByTestId('airless-status')).toContainText('ACCEPTED');

  const cooled = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
  expect(cooled.planetPixels).toBeGreaterThan(10_000);
  expect(cooled.glError).toBe(0);
  await page.screenshot({ path: testInfo.outputPath('airless-reference-cooled-rock.png') });

  await page.getByTestId('airless-reset').click();
  await expect(page.getByTestId('airless-elapsed')).toContainText('00:00 PHYSICAL TIME', { timeout: 30_000 });
  await expect(page.getByTestId('airless-surface-temperature')).toHaveText('SURFACE 1600 K');
});

test('AirlessReference_DefaultPlanetRemainsFrozenClimateExperiment', async ({ page }) => {
  await page.goto('/?visualTest=1');
  await expect(page.getByText('SNOWBALL EXPERIMENT', { exact: true })).toBeVisible({ timeout: 30_000 });
  await expect(page.getByRole('button', { name: 'NEXT' })).toBeVisible();
  await expect(page.getByTestId('airless-reference-readout')).toHaveCount(0);
  await expect(page.getByTestId('airless-advance')).toHaveCount(0);
});
