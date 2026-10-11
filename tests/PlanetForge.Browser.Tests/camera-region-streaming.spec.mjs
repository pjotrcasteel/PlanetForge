import { expect, test } from '@playwright/test';

test('CameraRegionStreaming_NoArchives_PreservesCanonicalGlobeAndDoesNotBlockBoot', async ({ page }) => {
  await page.goto('/?regionalStreaming=1&visualTest=1&seed=24061984');
  await expect(page.getByTestId('planet-seed')).toContainText('24061984', { timeout: 30_000 });
  await expect(page.getByTestId('region-stream-status')).toContainText('0 SAVED', { timeout: 20_000 });
  await expect.poll(async () => page.evaluate(() => window.__planetForgeSurfaceTest?.getSeed())).toBe(24061984);
  expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(2);
});

test('CameraRegionStreaming_CatalogueIndexesOnlyCurrentSeedAndPresentEpoch', async ({ page }) => {
  await page.goto('/?regionalStreaming=1&visualTest=1&seed=24061984');
  const prefix = 'planetforge/geology/v1/';
  const keys = [
    prefix + 'G18:S24061984:F4:L0:X0:Y0/YBP:0',
    prefix + 'G18:S24061984:F4:L0:X0:Y0/YBP:90000000',
    prefix + 'G18:S123:F4:L0:X0:Y0/YBP:0',
    prefix + 'G17:S24061984:F4:L0:X0:Y0/YBP:0'
  ];
  try {
    await page.evaluate(async keys => {
      const store = await import('./js/geologicalRegionStore.js');
      for (const key of keys) await store.saveArchive(key, '{}');
    }, keys);
    const listed = await page.evaluate(async () => {
      const store = await import('./js/geologicalRegionStore.js');
      return await store.listPresentGeologicalRegions(24061984, 18);
    });
    expect(listed.truncated).toBe(false);
    expect(listed.keys).toEqual([keys[0]]);

    await page.reload();
    await expect(page.getByTestId('region-stream-status')).toContainText('1 SAVED', { timeout: 20_000 });
  } finally {
    await page.evaluate(async keys => {
      const store = await import('./js/geologicalRegionStore.js');
      for (const key of keys) await store.deleteArchive(key);
    }, keys).catch(() => {});
  }
});


test('CameraRegionStreaming_ValidArchivedGeology_ActivatesNearCameraAndClearsOnOrbitReturn', async ({ page }) => {
  test.setTimeout(120_000);
  const id = 'G18:S24061984:F4:L0:X0:Y0';
  const key = `planetforge/geology/v1/${id}/YBP:0`;
  const width = 17;
  const cells = width * width;
  const snapshot = (iteration, elevation, eroded) => ({
    SchemaVersion: 5, Seed: 24061984, RegionKey: id,
    Width: width, Height: width, CellSpacingMeters: 1000,
    Iteration: iteration, ElevationMeters: Array(cells).fill(elevation),
    CumulativeErodedVolumeCubicMeters: eroded,
    CumulativeDepositedVolumeCubicMeters: 0,
    CumulativeExportedVolumeCubicMeters: eroded
  });
  const erosion = 30 * cells * 1_000_000;
  const json = JSON.stringify({
    FormatVersion: 1, Seed: 24061984, GenerationVersion: 18,
    Face: 4, Level: 0, X: 0, Y: 0, YearsBeforePresent: 0,
    PlanetRadiusMeters: 6_371_000,
    Original: snapshot(0, 1000, 0), Evolved: snapshot(1, 970, erosion)
  });

  await page.goto('/?regionalStreaming=1&visualTest=1&seed=24061984');
  try {
    await page.evaluate(async ({ key, json }) => {
      const store = await import('./js/geologicalRegionStore.js');
      await store.saveArchive(key, json);
    }, { key, json });
    await page.reload();
    await expect(page.getByTestId('region-stream-status')).toContainText('1 SAVED', { timeout: 30_000 });
    await expect.poll(async () => page.evaluate(() => Boolean(window.__planetForgeSurfaceTest))).toBe(true);
    await page.evaluate(() => {
      const camera = window.__planetForgeSurfaceTest;
      camera.setYaw(0);
      camera.setPitch(0);
      camera.setDistance(1.20);
    });
    await expect(page.getByTestId('region-stream-status')).toContainText('SAVED GEOLOGY', { timeout: 60_000 });
    await page.evaluate(() => window.__planetForgeSurfaceTest.setDistance(4.0));
    await expect(page.getByTestId('region-stream-status')).toContainText('CANONICAL TERRAIN', { timeout: 60_000 });
  } finally {
    await page.evaluate(async key => {
      const store = await import('./js/geologicalRegionStore.js');
      await store.deleteArchive(key);
    }, key).catch(() => {});
  }
});
