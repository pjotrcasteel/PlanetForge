import { expect, test } from '@playwright/test';

test('FrozenToMelting_PreservesPolarCapsAndThawsEquator', async ({ page }, testInfo) => {
  await page.goto('/?visualTest=1');
  await expect(page.getByText('FROZEN WORLD')).toBeVisible();
  await page.waitForFunction(() => Boolean(window.__planetForgeCryosphereTest));

  await setCryospherePitch(page, 0.0);
  const frozenEquatorCoverage = await measureCenterCoverage(page);
  expect(frozenEquatorCoverage).toBeGreaterThan(0.80);
  await page.screenshot({ path: testInfo.outputPath('year-0-frozen.png'), fullPage: true });

  await page.getByRole('button', { name: 'NEXT' }).click();
  await expect(page.getByText('MELTING WORLD', { exact: true })).toBeVisible({ timeout: 20_000 });
  await expect(page.getByRole('button', { name: 'NEXT' })).toBeEnabled({ timeout: 20_000 });

  await setCryospherePitch(page, 0.0);
  const meltedEquatorCoverage = await measureCenterCoverage(page);
  expect(meltedEquatorCoverage).toBeLessThan(0.35);
  await page.screenshot({ path: testInfo.outputPath('year-50-equator.png'), fullPage: true });

  await setCryospherePitch(page, 1.25);
  const northPoleCoverage = await measureCenterCoverage(page);
  expect(northPoleCoverage).toBeGreaterThan(0.70);

  await setCryospherePitch(page, -1.25);
  const southPoleCoverage = await measureCenterCoverage(page);
  expect(southPoleCoverage).toBeGreaterThan(0.70);

  expect(northPoleCoverage - meltedEquatorCoverage).toBeGreaterThan(0.40);
  expect(southPoleCoverage - meltedEquatorCoverage).toBeGreaterThan(0.40);

  await testInfo.attach('cryosphere-metrics', {
    body: JSON.stringify({ frozenEquatorCoverage, meltedEquatorCoverage, northPoleCoverage, southPoleCoverage }, null, 2),
    contentType: 'application/json'
  });
});

async function setCryospherePitch(page, pitch) {
  await page.evaluate(value => window.__planetForgeCryosphereTest.setPitch(value), pitch);
  await page.waitForTimeout(50);
}

async function measureCenterCoverage(page) {
  return page.evaluate(() => window.__planetForgeCryosphereTest.measureCenterCoverage());
}
