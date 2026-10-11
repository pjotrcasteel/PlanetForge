import { expect, test } from '@playwright/test';

// The only supported visual geology workflow is the actual main planet.
// Diagnostic Lab/Archives URLs may remain for research, but the HUD must
// not send players away from the globe just to inspect its physical relief.
test('MainPlanet_GeologyToggle_PublishesActualRevisedMeshesAndRestoresCanonicalTerrain', async ({ page }, testInfo) => {
    test.setTimeout(300_000);
    await page.goto('/?visualTest=1');
    const planet = page.locator('#planet-canvas');
    await expect(planet).toBeVisible({ timeout: 30_000 });
    await page.waitForFunction(() => window.__planetForgeSurfaceTest?.getTerrainGeometryKey(), null, { timeout: 30_000 });

    const initial = await page.evaluate(() => ({
        seed: window.__planetForgeSurfaceTest.getSeed(),
        geometryKey: window.__planetForgeSurfaceTest.getTerrainGeometryKey(),
        stats: window.__planetForgeSurfaceTest.measure()
    }));
    expect(initial.stats.glError).toBe(0);
    await page.screenshot({ path: testInfo.outputPath('main-planet-canonical.png'), fullPage: true });

    const geology = page.getByTestId('geology-terrain-toggle');
    await expect(geology).toBeVisible();
    await expect(geology).toHaveAttribute('aria-pressed', 'false');
    await expect(page.getByRole('link', { name: 'Open geological deformation preview' })).toHaveCount(0);

    await geology.click();
    await expect(geology).toHaveAttribute('aria-pressed', 'true', { timeout: 150_000 });
    await expect(page.getByTestId('main-geology-status')).toContainText('REAL ELEVATION', { timeout: 150_000 });
    await page.waitForFunction(previous => window.__planetForgeSurfaceTest?.getTerrainGeometryKey() !== previous,
        initial.geometryKey, { timeout: 30_000 });

    const physical = await page.evaluate(() => ({
        seed: window.__planetForgeSurfaceTest.getSeed(),
        geometryKey: window.__planetForgeSurfaceTest.getTerrainGeometryKey(),
        stats: window.__planetForgeSurfaceTest.measure()
    }));
    expect(physical.seed).toBe(initial.seed);
    expect(physical.geometryKey).not.toBe(initial.geometryKey);
    expect(physical.stats.glError).toBe(0);
    expect(physical.stats.planetPixels).toBeGreaterThan(10_000);
    await page.screenshot({ path: testInfo.outputPath('main-planet-physical-geology.png'), fullPage: true });

    await geology.click();
    await expect(geology).toHaveAttribute('aria-pressed', 'false', { timeout: 90_000 });
    await expect(page.getByTestId('main-geology-status')).toContainText('CANONICAL BEDROCK');
    await page.waitForFunction(previous => window.__planetForgeSurfaceTest?.getTerrainGeometryKey() !== previous,
        physical.geometryKey, { timeout: 30_000 });
    expect((await page.evaluate(() => window.__planetForgeSurfaceTest.measure())).glError).toBe(0);
});

test('MainPlanet_GeologyToggle_MobileLayout_StaysOnCanvasAndFitsPhone', async ({ page }) => {
    test.setTimeout(240_000);
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/?visualTest=1');
    await expect(page.locator('#planet-canvas')).toBeVisible({ timeout: 30_000 });
    const toggle = page.getByTestId('geology-terrain-toggle');
    await expect(toggle).toBeVisible();
    await toggle.click();
    await expect(toggle).toHaveAttribute('aria-pressed', 'true', { timeout: 150_000 });

    const metrics = await page.evaluate(() => {
        const hud = document.querySelector('.foundation-clock');
        const toggle = document.querySelector('[data-testid="geology-terrain-toggle"]');
        return {
            width: document.documentElement.scrollWidth - window.innerWidth,
            hudLeft: hud.getBoundingClientRect().left,
            hudRight: hud.getBoundingClientRect().right,
            buttonHeight: toggle.getBoundingClientRect().height,
            hasCanvas: !!document.getElementById('planet-canvas')
        };
    });
    expect(metrics.width).toBeLessThanOrEqual(2);
    expect(metrics.hudLeft).toBeGreaterThanOrEqual(-2);
    expect(metrics.hudRight).toBeLessThanOrEqual(392);
    expect(metrics.buttonHeight).toBeGreaterThanOrEqual(28);
    expect(metrics.hasCanvas).toBe(true);
});

test('MainPlanet_MeltingWorld_ShowsActualSeaLevelOceansWithGeologyEnabled', async ({ page }, testInfo) => {
    test.setTimeout(300_000);
    await page.goto('/?visualTest=1');
    await expect(page.locator('#planet-canvas')).toBeVisible({ timeout: 30_000 });
    await page.waitForFunction(() => window.__planetForgeSurfaceTest?.getTerrainGeometryKey(), null, { timeout: 30_000 });

    const baseline = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
    expect(baseline.glError).toBe(0);

    await page.getByRole('button', { name: 'NEXT' }).click();
    await expect(page.getByText('MELTING WORLD', { exact: true })).toBeVisible({ timeout: 60_000 });
    await expect(page.getByRole('button', { name: 'NEXT' })).toBeEnabled({ timeout: 60_000 });
    await page.waitForFunction(() => window.__planetForgeSurfaceTest?.measure().oceanPixels > 100, null, { timeout: 45_000 });

    const melted = await page.evaluate(() => ({
        key: window.__planetForgeSurfaceTest.getTerrainGeometryKey(),
        stats: window.__planetForgeSurfaceTest.measure()
    }));
    expect(melted.stats.glError).toBe(0);
    expect(melted.stats.oceanPixels).toBeGreaterThan(100);
    // A handful of bluish terrain pixels can pass a naive colour-presence check.
    // Demand a meaningful connected-sea footprint on this deterministic seed.
    expect(melted.stats.oceanFraction).toBeGreaterThan(0.16);
    expect(melted.stats.oceanFraction).toBeGreaterThan(baseline.oceanFraction * 1.8);
    await page.screenshot({ path: testInfo.outputPath('main-planet-melted-oceans.png'), fullPage: true });

    const geology = page.getByTestId('geology-terrain-toggle');
    await geology.click();
    await expect(geology).toHaveAttribute('aria-pressed', 'true', { timeout: 150_000 });
    await page.waitForFunction(previous => window.__planetForgeSurfaceTest?.getTerrainGeometryKey() !== previous,
        melted.key, { timeout: 30_000 });
    await page.waitForFunction(() => window.__planetForgeSurfaceTest?.measure().oceanPixels > 100, null, { timeout: 30_000 });

    const physical = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
    expect(physical.glError).toBe(0);
    expect(physical.oceanPixels).toBeGreaterThan(100);
    expect(physical.oceanFraction).toBeGreaterThan(0.16);
    expect(physical.oceanFraction).toBeGreaterThan(baseline.oceanFraction * 1.8);
    await page.screenshot({ path: testInfo.outputPath('main-planet-physical-geology-oceans.png'), fullPage: true });
});
