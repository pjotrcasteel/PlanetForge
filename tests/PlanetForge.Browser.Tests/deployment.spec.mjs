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
  const badge = page.locator('#planetforge-build-badge');
  await expect(badge).toHaveText(/^PF \d+\.\d+\.\d+(?:\.\d+)? · [a-f0-9]{7}$/);
  const visibleVersion = (await badge.textContent()).match(/^PF ([\d.]+) · /)[1];
  await expect(page).toHaveTitle(`PlanetForge ${visibleVersion} — Planetary Simulation`);
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

test('DeployedPlanetForge_ExposesHotRockReferenceAndPhysicalCooling', async ({ page }) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));

  await page.goto(process.env.PLANETFORGE_BASE_URL + '/?visualTest=1', { waitUntil: 'domcontentloaded' });
  await expect(page.getByTestId('airless-enter')).toBeVisible({ timeout: 90_000 });
  await page.getByTestId('airless-enter').click();
  await expect(page.getByTestId('airless-reference-readout')).toBeVisible({ timeout: 45_000 });
  await expect(page.getByTestId('airless-surface-temperature')).toHaveText('SURFACE 1600 K');
  await expect(page.getByTestId('planet-scenario-label')).toHaveText('AIRLESS · ONE-ZONE PHYSICAL REFERENCE');
  await page.getByTestId('airless-advance').click();
  await expect(page.getByTestId('airless-elapsed')).toContainText('T+1h', { timeout: 45_000 });
  await expect(page.getByTestId('airless-surface-temperature')).not.toHaveText('SURFACE 1600 K');
  await expect(page.getByTestId('airless-status')).toContainText('ACCEPTED');
  await page.getByTestId('airless-exit').click();
  await expect(page.getByTestId('airless-enter')).toBeVisible();
  await expect(page.getByText('SNOWBALL EXPERIMENT', { exact: true })).toBeVisible();
  expect(errors).toEqual([]);

  const badge = page.locator('#planetforge-build-badge');
  await expect(badge).toContainText('PF 0.0.39.1 · ');
  if (process.env.PLANETFORGE_EXPECTED_SHA) {
    await expect(badge).toContainText(process.env.PLANETFORGE_EXPECTED_SHA.slice(0, 7));
  }
});
