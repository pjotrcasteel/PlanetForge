import { expect, test } from '@playwright/test';

test.setTimeout(120_000);

test('PlanetStages_ReloadUsesShowcaseAndGenerateCreatesNewWorld', async ({ page }) => {
  await page.goto('/?visualTest=1');
  const seedLabel = page.getByTestId('planet-seed');
  await expect(page.getByText('FROZEN WORLD', { exact: true })).toBeVisible({ timeout: 20_000 });
  await expect(page.getByRole('button', { name: 'NEXT' })).toBeVisible();
  await expect(seedLabel).toHaveText('SEED 24061984', { timeout: 20_000 });
  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest) || Boolean(window.__planetForgeSurfaceError), null, { timeout: 15_000 });

  const surfaceError = await page.evaluate(() => window.__planetForgeSurfaceError ?? null);
  expect(surfaceError, `Planet renderer failed to initialize: ${surfaceError}`).toBeNull();
  const showcase = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
  expect(showcase.planetPixels).toBeGreaterThan(10_000);
  expect(showcase.glError).toBe(0);

  await page.getByRole('button', { name: 'Generate new planet' }).click();
  await expect(seedLabel).not.toHaveText('SEED 24061984', { timeout: 30_000 });
  await page.waitForFunction(() => window.__planetForgeSurfaceTest.getSeed() !== 24061984, null, { timeout: 30_000 });

  await page.reload();
  await expect(seedLabel).toHaveText('SEED 24061984', { timeout: 20_000 });
});

test('GeneratorQualityGate_RendersMeltedShowcaseFromMultipleAngles', async ({ page }, testInfo) => {
  await page.goto('/?visualTest=1');
  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest), null, { timeout: 15_000 });
  await page.getByRole('button', { name: 'NEXT' }).click();
  await expect(page.getByText('MELTING WORLD', { exact: true })).toBeVisible({ timeout: 45_000 });

  const views = [
    ['equator', 0.0, 0.0],
    ['opposite', Math.PI, 0.0],
    ['north', 0.0, 1.20],
    ['south', 0.0, -1.20],
  ];

  for (const [name, yaw, pitch] of views) {
    await page.evaluate(({ yaw, pitch }) => {
      window.__planetForgeSurfaceTest.setYaw(yaw);
      window.__planetForgeSurfaceTest.setPitch(pitch);
    }, { yaw, pitch });
    await page.waitForTimeout(120);
    const metrics = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
    expect(metrics.planetPixels).toBeGreaterThan(10_000);
    expect(metrics.glError).toBe(0);
    await page.screenshot({ path: testInfo.outputPath(`generator-quality-${name}.png`), fullPage: true });
  }
});
