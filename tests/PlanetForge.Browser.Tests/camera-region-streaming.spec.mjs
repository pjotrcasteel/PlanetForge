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
