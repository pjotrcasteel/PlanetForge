import { expect, test } from '@playwright/test';

test.setTimeout(240_000);

for (const mode of ['temperature', 'melt']) {
  test(`RockDiagnostic_${mode}_UsesPhysicalAtlasWithoutWebGLErrors`, async ({ page }) => {
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(`/?visualTest=1&rockColumns=1&rockMap=${mode}`);
    await expect(page.getByTestId('planet-scenario-label')).toHaveText('ROCK COLUMNS · SURFACE AND DEPTH', { timeout: 60_000 });
    await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest) || Boolean(window.__planetForgeSurfaceError), null, { timeout: 60_000 });
    expect(await page.evaluate(() => window.__planetForgeSurfaceError ?? null)).toBeNull();
    expect((await page.evaluate(() => window.__planetForgeSurfaceTest.measure())).glError).toBe(0);
    await page.getByTestId('airless-advance').click();
    await expect(page.getByTestId('airless-elapsed')).toContainText('T+1h', { timeout: 60_000 });
    expect((await page.evaluate(() => window.__planetForgeSurfaceTest.measure())).glError).toBe(0);
    expect(errors).toEqual([]);
  });
}

test('RockDiagnostic_DefaultPhysicalAppearance_RemainsAvailable', async ({ page }) => {
  await page.goto('/?visualTest=1&rockColumns=1');
  await expect(page.getByTestId('planet-scenario-label')).toHaveText('ROCK COLUMNS · SURFACE AND DEPTH', { timeout: 60_000 });
  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest), null, { timeout: 60_000 });
  expect((await page.evaluate(() => window.__planetForgeSurfaceTest.measure())).glError).toBe(0);
});
