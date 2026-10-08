import { expect, test } from '@playwright/test';

test.setTimeout(120_000);
test.skip(!process.env.PLANETFORGE_DEPLOY_SMOKE, 'Only run against the published GitHub Pages deployment.');

test('DeployedPlanetForge_BootsAndRendersPlanet', async ({ page }) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));

  const deployedUrl = process.env.PLANETFORGE_BASE_URL + '/?visualTest=1';
  await page.goto(deployedUrl, { waitUntil: 'domcontentloaded' });
  await expect(page.getByTestId('planet-seed')).toBeVisible({ timeout: 90_000 });
  await expect(page.getByRole('button', { name: 'NEXT' })).toBeVisible();
  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest) || Boolean(window.__planetForgeSurfaceError), null, { timeout: 60_000 });

  const runtimeError = await page.evaluate(() => window.__planetForgeSurfaceError ?? null);
  expect(runtimeError).toBeNull();
  expect(errors).toEqual([]);

  const measurements = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
  expect(measurements.planetPixels).toBeGreaterThan(10_000);
  expect(measurements.glError).toBe(0);

  const expectedSha = process.env.PLANETFORGE_EXPECTED_SHA;
  if (expectedSha) {
    await expect(page.locator('#planetforge-build-badge')).toContainText(expectedSha.slice(0, 7));
  }
});
