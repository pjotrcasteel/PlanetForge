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
    const alignedWater = await page.evaluate(() => window.__planetForgeWaterTest?.camera());
    expect(alignedWater).not.toBeNull();
    expect(alignedWater.terrainRevision).toBe(Number(physical.geometryKey.split(':').at(-1)));
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

test('MainPlanet_WaterOverlay_CameraFollowsWebGLOrbitWithoutIndependentGestures', async ({ page }) => {
    test.setTimeout(90_000);
    await page.goto('/?visualTest=1');
    await expect(page.locator('#planet-canvas')).toBeVisible({ timeout: 30_000 });
    await page.waitForFunction(() => window.__planetForgeSurfaceTest?.getTerrainGeometryKey()
        && window.__planetForgeWaterTest?.camera(), null, { timeout: 30_000 });

    const checkAlignment = async () => page.evaluate(() => {
        const main = window.__planetForgeSurfaceTest;
        const water = window.__planetForgeWaterTest.camera();
        return { yawDifference: Math.abs(water.yaw - main.getYaw()),
            pitchDifference: Math.abs(water.pitch - main.getPitch()),
            distanceDifference: Math.abs(water.distance - main.getDistance()) };
    });

    for (const update of [
        () => page.evaluate(() => window.__planetForgeSurfaceTest.setYaw(0.6)),
        () => page.evaluate(() => window.__planetForgeSurfaceTest.setPitch(-0.38)),
        () => page.evaluate(() => window.__planetForgeSurfaceTest.setDistance(2.5))
    ]) {
        await update();
        const aligned = await checkAlignment();
        expect(aligned.yawDifference).toBeLessThan(1e-10);
        expect(aligned.pitchDifference).toBeLessThan(1e-10);
        expect(aligned.distanceDifference).toBeLessThan(1e-10);
    }
});

test('MainPlanet_ActiveWaterWorld_BuildsWaterAgainstVisibleTerrainWithoutSecondCamera', async ({ page }, testInfo) => {
    test.setTimeout(300_000);
    await page.goto('/?visualTest=1');
    await expect(page.locator('#planet-canvas')).toBeVisible({ timeout: 30_000 });
    await expect(page.getByRole('button', { name: 'NEXT' })).toBeEnabled({ timeout: 30_000 });

    await page.getByRole('button', { name: 'NEXT' }).click();
    await expect(page.getByText('MELTING WORLD', { exact: true })).toBeVisible({ timeout: 90_000 });
    await expect(page.getByRole('button', { name: 'NEXT' })).toBeEnabled({ timeout: 90_000 });
    await page.getByRole('button', { name: 'NEXT' }).click();
    await page.waitForFunction(() => window.__planetForgeWaterTest?.measure().activeRiverSegmentCount > 0, null, { timeout: 180_000 });

    const water = await page.evaluate(() => window.__planetForgeWaterTest.measure());
    expect(water.activeRiverSegmentCount).toBeGreaterThan(0);
    expect(water.riverPointCount).toBeGreaterThan(1);
    expect(water.terrainVertexCount).toBeGreaterThan(0);
    expect(water.terrainSampleCount).toBeGreaterThan(0);
    await page.screenshot({ path: testInfo.outputPath('main-planet-physical-hydrology.png'), fullPage: true });

    await page.evaluate(() => window.__planetForgeSurfaceTest.setYaw(0.95));
    const aligned = await page.evaluate(() => ({
        webGlYaw: window.__planetForgeSurfaceTest.getYaw(),
        waterYaw: window.__planetForgeWaterTest.camera().yaw,
        riverCount: window.__planetForgeWaterTest.measure().activeRiverSegmentCount
    }));
    expect(aligned.waterYaw).toBeCloseTo(aligned.webGlYaw, 10);
    expect(aligned.riverCount).toBe(water.activeRiverSegmentCount);
});
