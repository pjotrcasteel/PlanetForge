import { expect, test } from '@playwright/test';

test.setTimeout(120_000);

test('GeneratorLab_ReloadUsesShowcaseAndGenerateCreatesNewWorld', async ({ page }) => {
  await page.goto('/?visualTest=1');
  const seedLabel = page.getByTestId('planet-seed');
  await expect(page.getByText('GENERATOR LAB', { exact: true })).toBeVisible({ timeout: 20_000 });
  await expect(seedLabel).toHaveText('SEED 24061984', { timeout: 20_000 });
  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest) || Boolean(window.__planetForgeSurfaceError), null, { timeout: 15_000 });

  const surfaceError = await page.evaluate(() => window.__planetForgeSurfaceError ?? null);
  expect(surfaceError, `Generator renderer failed to initialize: ${surfaceError}`).toBeNull();
  expect(await page.evaluate(() => window.__planetForgeSurfaceTest.getSeed())).toBe(24061984);

  const showcase = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
  expect(showcase.planetPixels).toBeGreaterThan(10_000);
  expect(showcase.oceanPixels).toBeGreaterThan(1_000);
  expect(showcase.oceanFraction).toBeGreaterThan(0.10);
  expect(showcase.glError).toBe(0);

  await page.getByRole('button', { name: 'Generate new planet' }).click();
  await expect(seedLabel).not.toHaveText('SEED 24061984', { timeout: 30_000 });
  await page.waitForFunction(() => window.__planetForgeSurfaceTest.getSeed() !== 24061984, null, { timeout: 30_000 });

  const generatedSeed = await page.evaluate(() => window.__planetForgeSurfaceTest.getSeed());
  expect(generatedSeed).toBeGreaterThan(0);
  const generated = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
  expect(generated.planetPixels).toBeGreaterThan(10_000);
  expect(generated.glError).toBe(0);

  await page.reload();
  await expect(seedLabel).toHaveText('SEED 24061984', { timeout: 20_000 });
  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest), null, { timeout: 15_000 });
  expect(await page.evaluate(() => window.__planetForgeSurfaceTest.getSeed())).toBe(24061984);
});

test('GeneratorLab_RendersSameStaticWorldFromMultipleAngles', async ({ page }, testInfo) => {
  await page.goto('/?visualTest=1');
  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest), null, { timeout: 15_000 });

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
    await page.screenshot({ path: testInfo.outputPath(`generator-${name}.png`), fullPage: true });
  }
});
