import { expect, test } from '@playwright/test';

test.setTimeout(120_000);
test.skip(!process.env.PLANETFORGE_DEPLOY_SMOKE, 'Only run against the published GitHub Pages deployment.');

test('DeployedPlanetForge_BootsAndRendersPlanet', async ({ page }) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));

  const deployedUrl = process.env.PLANETFORGE_BASE_URL + '/?visualTest=1&legacy=1';
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

  await page.goto(process.env.PLANETFORGE_BASE_URL + '/?visualTest=1&legacy=1', { waitUntil: 'domcontentloaded' });
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
  // The smoke suite must validate each deployed release without carrying
  // a stale prior-version assertion into the next release.
  const visibleVersion = (await page.title()).match(/^PlanetForge ([0-9.]+) — Planetary Simulation$/)?.[1];
  expect(visibleVersion).toBeTruthy();
  await expect(badge).toContainText(`PF ${visibleVersion} · `);
  if (process.env.PLANETFORGE_EXPECTED_SHA) {
    await expect(badge).toContainText(process.env.PLANETFORGE_EXPECTED_SHA.slice(0, 7));
  }
});

test('PublishedRegionalRock_ShowsCanonicalPhaseAndAdvancesWithoutFakeCrust', async ({ page }) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto(process.env.PLANETFORGE_BASE_URL + '/?visualTest=1&regionalRock=1', {
    waitUntil: 'domcontentloaded'
  });

  await expect(page.getByTestId('regional-temperature-range')).toBeVisible({ timeout: 90_000 });
  await expect(page.getByTestId('regional-melt-range')).toBeVisible();
  // Verify that Blazor's isolated component styles are really served by
  // GitHub Pages, not merely referenced by the HTML document.
  const isolatedStylesLoaded = await page.evaluate(() =>
    Array.from(document.styleSheets).some(sheet =>
      sheet.href?.includes('PlanetForge.Presentation.Web.styles.css')));
  expect(isolatedStylesLoaded).toBe(true);
  const hudLayout = await page.evaluate(() => {
    const panel = document.querySelector('.foundation-clock-thermal');
    const temperature = document.querySelector('[data-testid="regional-temperature-range"]');
    const melt = document.querySelector('[data-testid="regional-melt-range"]');
    const elapsed = document.querySelector('[data-testid="airless-elapsed"]');
    const bounds = element => element.getBoundingClientRect();
    return {
      panelHeight: bounds(panel).height,
      temperatureBottom: bounds(temperature).bottom,
      meltTop: bounds(melt).top,
      meltRight: bounds(melt).right,
      elapsedLeft: bounds(elapsed).left
    };
  });
  expect(hudLayout.panelHeight).toBeLessThan(160);
  expect(hudLayout.temperatureBottom).toBeLessThanOrEqual(hudLayout.meltTop + 1);
  expect(hudLayout.meltRight).toBeLessThanOrEqual(hudLayout.elapsedLeft + 1);

  await expect(page.getByText('DECLARED THERMAL CONTRAST · NOT CRUST')).toBeVisible();
  await expect(page.getByTestId('planet-scenario-label')).toHaveText('REGIONAL ROCK · CONSERVED PHASE REFERENCE');

  const initial = await page.getByTestId('regional-temperature-range').textContent();
  await page.getByTestId('airless-advance').click();
  await expect(page.getByTestId('airless-elapsed')).toContainText('T+1h', { timeout: 90_000 });
  await expect(page.getByTestId('regional-temperature-range')).not.toHaveText(initial);
  await expect(page.getByTestId('airless-status')).toContainText('ACCEPTED');
  expect(errors).toEqual([]);
});

test('PublishedMagmaOcean_FundedWholeSurface_CoolsAndResetsOnCanonicalGlobe', async ({ page }) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto(process.env.PLANETFORGE_BASE_URL + '/?visualTest=1&magmaOcean=1', { waitUntil: 'domcontentloaded' });
  await expect(page.getByTestId('planet-scenario-label'))
    .toHaveText('MAGMA OCEAN · CONSERVED MOLTEN REFERENCE', { timeout: 90_000 });
  await expect(page.getByTestId('regional-melt-range')).toHaveText('MELT 100%–100%');
  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest) || Boolean(window.__planetForgeSurfaceError));
  expect(await page.evaluate(() => window.__planetForgeSurfaceError ?? null)).toBeNull();
  const initial = await page.getByTestId('regional-temperature-range').textContent();
  const badge = page.locator('#planetforge-build-badge');
  if (process.env.PLANETFORGE_EXPECTED_SHA)
    await expect(badge).toContainText(process.env.PLANETFORGE_EXPECTED_SHA.slice(0, 7));

  await page.getByTestId('airless-advance').click();
  await expect(page.getByTestId('airless-elapsed')).toContainText('T+1h', { timeout: 90_000 });
  await expect(page.getByTestId('airless-status')).toContainText('ACCEPTED');
  await expect(page.getByTestId('regional-temperature-range')).not.toHaveText(initial);
  const measurements = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
  expect(measurements.planetPixels).toBeGreaterThan(10_000);
  expect(measurements.glError).toBe(0);
  await page.getByTestId('airless-reset').click();
  await expect(page.getByTestId('airless-elapsed')).toContainText('T+0h');
  await expect(page.getByTestId('regional-melt-range')).toHaveText('MELT 100%–100%');
  expect(errors).toEqual([]);
});

test('PublishedRockColumns_SurfaceAndDepth_AdvancesCanonicalTerrainAndResets', async ({ page }) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto(process.env.PLANETFORGE_BASE_URL + '/?visualTest=1&rockColumns=1', { waitUntil: 'domcontentloaded' });
  await expect(page.getByTestId('planet-scenario-label')).toHaveText('ROCK COLUMNS · SURFACE AND DEPTH', { timeout: 90_000 });
  if (process.env.PLANETFORGE_EXPECTED_SHA)
    await expect(page.locator('#planetforge-build-badge')).toContainText(process.env.PLANETFORGE_EXPECTED_SHA.slice(0, 7));
  await expect(page.getByTestId('column-solid-thickness')).toHaveText('SOLID TOP 0.00–0.00 m');
  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest) || Boolean(window.__planetForgeSurfaceError));
  expect(await page.evaluate(() => window.__planetForgeSurfaceError ?? null)).toBeNull();
  const key = await page.evaluate(() => window.__planetForgeSurfaceTest.getTerrainGeometryKey());
  await page.getByTestId('airless-advance').click();
  await expect(page.getByTestId('airless-elapsed')).toContainText('T+1h', { timeout: 90_000 });
  await expect(page.getByTestId('airless-status')).toHaveText('ACCEPTED');
  await expect(page.getByTestId('regional-melt-range')).not.toHaveText('MELT 100%–100%');
  await expect(page.getByTestId('column-deep-melt')).toHaveText('DEEP MELT 100%–100%');
  expect(await page.evaluate(() => window.__planetForgeSurfaceTest.getTerrainGeometryKey())).not.toBe(key);
  expect((await page.evaluate(() => window.__planetForgeSurfaceTest.measure())).glError).toBe(0);
  await page.getByTestId('airless-reset').click();
  await expect(page.getByTestId('column-solid-thickness')).toHaveText('SOLID TOP 0.00–0.00 m');
  await expect(page.getByTestId('regional-melt-range')).toHaveText('MELT 100%–100%');
  expect(errors).toEqual([]);
});

test('DeployedFormation_StartsMoltenOnPrimaryPageAndAdvancesSameClock', async ({ page }) => {
  await page.goto(process.env.PLANETFORGE_BASE_URL + '/?visualTest=1&seed=24061984', {waitUntil:'domcontentloaded'});
  await expect(page.getByTestId('formation-phase')).toHaveText('Molten surface',{timeout:90000});
  const visibleVersion = (await page.title()).match(/^PlanetForge ([0-9.]+) — Planetary Simulation$/)?.[1];
  expect(visibleVersion).toBeTruthy();
  await expect(page.locator('#planetforge-build-badge')).toContainText(`PF ${visibleVersion} · `);
  if(process.env.PLANETFORGE_EXPECTED_SHA) await expect(page.locator('#planetforge-build-badge')).toContainText(process.env.PLANETFORGE_EXPECTED_SHA.slice(0,7));
  await expect(page.getByTestId('formation-time')).toHaveText('T+00:00:00');
  await expect(page.getByTestId('airless-enter')).toHaveCount(0);
  await page.getByTestId('formation-step').click();
  await expect(page.getByTestId('formation-time')).toHaveText('T+00:00:01',{timeout:60000});
  await page.waitForFunction(()=>Boolean(window.__planetForgeSurfaceTest),null,{timeout:60000});
  expect((await page.evaluate(()=>window.__planetForgeSurfaceTest.measure())).glError).toBe(0);
});
