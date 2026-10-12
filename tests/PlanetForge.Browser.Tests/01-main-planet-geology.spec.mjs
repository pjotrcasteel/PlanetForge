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

test('MainPlanet_MeltingWorld_PublishesPhysicallyPlacedOceanInBothGeologyModes', async ({ page }, testInfo) => {
    test.setTimeout(300_000);
    await page.goto('/?visualTest=1');
    await expect(page.locator('#planet-canvas')).toBeVisible({ timeout: 30_000 });
    await page.waitForFunction(() => window.__planetForgeSurfaceTest?.getTerrainGeometryKey(), null, { timeout: 30_000 });

    const dry = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
    expect(dry.glError).toBe(0);

    await page.getByRole('button', { name: 'NEXT' }).click();
    await expect(page.getByText('MELTING WORLD', { exact: true })).toBeVisible({ timeout: 90_000 });
    await expect(page.getByRole('button', { name: 'NEXT' })).toBeEnabled({ timeout: 90_000 });
    await page.waitForFunction(() => window.__planetForgeSurfaceTest?.measure().oceanPixels > 1_000, null, { timeout: 60_000 });
    const melted = await page.evaluate(() => ({
        geometryKey: window.__planetForgeSurfaceTest.getTerrainGeometryKey(),
        water: window.__planetForgeSurfaceTest.measure()
    }));

    expect(melted.water.glError).toBe(0);
    expect(melted.water.oceanPixels).toBeGreaterThan(dry.oceanPixels + 1_000);
    expect(melted.water.oceanFraction).toBeGreaterThan(dry.oceanFraction);
    expect(melted.water.oceanLargestConnectedFraction).toBeGreaterThan(0.20);
    // A sea should be geographically connected rather than scattered dark
    // patches. Its absolute coverage still follows physical water mass.
    // Sea coverage is determined from actual water volume and terrain, not
    // an arbitrary minimum ocean percentage or a shader-painted continent.
    await page.screenshot({ path: testInfo.outputPath('main-planet-physical-oceans-geology-off.png'), fullPage: true });

    const geology = page.getByTestId('geology-terrain-toggle');
    await geology.click();
    await expect(geology).toHaveAttribute('aria-pressed', 'true', { timeout: 150_000 });
    await page.waitForFunction(previous => window.__planetForgeSurfaceTest?.getTerrainGeometryKey() !== previous,
        melted.geometryKey, { timeout: 30_000 });
    await page.waitForFunction(() => window.__planetForgeSurfaceTest?.measure().oceanPixels > 1_000, null, { timeout: 60_000 });

    const physical = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
    expect(physical.glError).toBe(0);
    expect(physical.oceanPixels).toBeGreaterThan(dry.oceanPixels + 1_000);
    expect(physical.oceanFraction).toBeGreaterThan(dry.oceanFraction);
    expect(physical.oceanLargestConnectedFraction).toBeGreaterThan(0.20);
    await page.screenshot({ path: testInfo.outputPath('main-planet-physical-oceans-geology-on.png'), fullPage: true });
});


test('MainPlanet_CrustResearchEpochs_ChangePhysicalGeometryAndKeepWaterCameraAlignedOnPhone', async ({ page }, testInfo) => {
    test.setTimeout(420_000);
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/?visualTest=1');
    await expect(page.locator('#planet-canvas')).toBeVisible({ timeout: 30_000 });
    await page.waitForFunction(() => window.__planetForgeSurfaceTest?.getTerrainGeometryKey(), null, { timeout: 30_000 });
    const canonical = await page.evaluate(() => window.__planetForgeSurfaceTest.getTerrainGeometryKey());

    await page.getByTestId('geology-terrain-toggle').click();
    await expect(page.getByTestId('main-geology-epochs')).toBeVisible({ timeout: 150_000 });
    await expect(page.getByTestId('main-geology-epoch')).toHaveText('0 Ma AGO');
    const present = await page.evaluate(() => window.__planetForgeSurfaceTest.getTerrainGeometryKey());
    expect(present).not.toBe(canonical);
    const focusedHud = page.locator('.foundation-clock-geology');
    await expect(focusedHud).toBeVisible();
    expect((await focusedHud.boundingBox()).height).toBeLessThan(200);
    await expect(page.getByRole('button', { name: 'Generate new planet' })).toBeHidden();
    await expect(page.getByText('YEAR 0', { exact: true })).toBeHidden();
    await expect(page.getByRole('button', { name: 'NEXT', exact: true })).toHaveCount(0);

    for (const [expected, filename] of [
        ['1 Ma AGO', 'crust-research-1ma.png'], ['2 Ma AGO', 'crust-research-2ma.png'],
        ['3 Ma AGO', null], ['4 Ma AGO', null], ['5 Ma AGO', null],
        ['6 Ma AGO', 'crust-research-6ma.png']
    ]) {
        const before = await page.evaluate(() => window.__planetForgeSurfaceTest.getTerrainGeometryKey());
        await page.getByTestId('main-geology-previous').click();
        await expect(page.getByTestId('main-geology-epoch')).toHaveText(expected, { timeout: 150_000 });
        await page.waitForFunction(old => window.__planetForgeSurfaceTest?.getTerrainGeometryKey() !== old,
            before, { timeout: 30_000 });
        const aligned = await page.evaluate(() => ({
            geometryKey: window.__planetForgeSurfaceTest.getTerrainGeometryKey(),
            mainYaw: window.__planetForgeSurfaceTest.getYaw(),
            water: window.__planetForgeWaterTest?.camera(),
            glError: window.__planetForgeSurfaceTest.measure().glError,
            overflow: document.documentElement.scrollWidth - window.innerWidth,
            buttons: [...document.querySelectorAll('.foundation-geology-epochs button')].map(x => x.getBoundingClientRect().height)
        }));
        expect(aligned.glError).toBe(0);
        expect(aligned.overflow).toBeLessThanOrEqual(2);
        expect(aligned.water?.terrainRevision).toBe(Number(aligned.geometryKey.split(':').at(-1)));
        expect(aligned.water?.yaw).toBeCloseTo(aligned.mainYaw, 10);
        expect(Math.min(...aligned.buttons)).toBeGreaterThanOrEqual(36);
        if (filename) await page.screenshot({ path: testInfo.outputPath(filename), fullPage: true });
    }

    await expect(page.getByTestId('main-geology-previous')).toBeDisabled();
    await page.getByTestId('main-geology-next').click();
    await expect(page.getByTestId('main-geology-epoch')).toHaveText('5 Ma AGO', { timeout: 150_000 });
    await expect(page.getByTestId('main-geology-previous')).toBeEnabled();

    await page.getByTestId('geology-terrain-toggle').click();
    await expect(page.getByTestId('main-geology-epochs')).toHaveCount(0, { timeout: 90_000 });
    await expect(page.getByText('YEAR 0', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Generate new planet' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'NEXT', exact: true })).toBeVisible();
    const restored = await page.evaluate(() => window.__planetForgeSurfaceTest.getTerrainGeometryKey());
    expect(restored).not.toBe(present);
    expect((await page.evaluate(() => window.__planetForgeSurfaceTest.measure())).glError).toBe(0);
});


test('MainPlanet_VisualTestMeasurement_InLocalModeActuallyRendersVisibleGround', async ({ page }, testInfo) => {
    test.setTimeout(180_000);
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/?visualTest=1');
    await expect(page.locator('#planet-canvas')).toBeVisible({ timeout: 30_000 });
    await page.waitForFunction(() => window.__planetForgeSurfaceTest?.getLodStats(), null, { timeout: 30_000 });

    await page.evaluate(() => window.__planetForgeSurfaceTest.setDistance(1.002));
    await page.waitForFunction(() => {
        const lod = window.__planetForgeSurfaceTest?.getLodStats();
        return lod?.mode === 'local' && !lod.refining;
    }, null, { timeout: 90_000 });

    // Previously measure() ALWAYS called renderGlobe even in local mode.
    // The empty orbit tile collection then cleared the canvas to black,
    // falsely passing WebGL tests that asserted only glError == 0.
    const visual = await page.evaluate(() => ({
        lod: window.__planetForgeSurfaceTest.getLodStats(),
        screen: window.__planetForgeSurfaceTest.measure(),
        geometry: window.__planetForgeSurfaceTest.getTerrainGeometryKey()
    }));
    expect(visual.lod.mode).toBe('local');
    expect(visual.lod.localCellSpacingMeters).toBeGreaterThan(0);
    expect(visual.screen.glError).toBe(0);
    expect(visual.screen.planetPixels).toBeGreaterThan(10_000);
    expect(visual.geometry).toBeTruthy();
    await page.screenshot({ path: testInfo.outputPath('local-terrain-actually-visible.png'), fullPage: true });

    // Moving back to the globe must use the orbit renderer and leave no
    // incorrectly retained local measurement state or WebGL errors.
    await page.evaluate(() => window.__planetForgeSurfaceTest.setDistance(1.06));
    await page.waitForFunction(() => {
        const lod = window.__planetForgeSurfaceTest?.getLodStats();
        return lod?.mode === 'globe' && !lod.refining;
    }, null, { timeout: 60_000 });
    expect((await page.evaluate(() => window.__planetForgeSurfaceTest.measure())).glError).toBe(0);
});


test('MainPlanet_PhysicalLocalDetail_ResolvesRegionalBedrockWithinMobileMeshBudget', async ({ page }, testInfo) => {
    test.setTimeout(180_000);
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/?visualTest=1');
    await expect(page.locator('#planet-canvas')).toBeVisible({ timeout: 30_000 });
    await page.waitForFunction(() => window.__planetForgeSurfaceTest?.getLodStats(), null, { timeout: 30_000 });

    await page.evaluate(() => window.__planetForgeSurfaceTest.setDistance(1.0 + 3_000 / 6_371_000));
    await page.waitForFunction(() => {
        const detail = window.__planetForgeSurfaceTest?.getLodStats();
        return detail?.mode === 'local' && !detail.refining;
    }, null, { timeout: 90_000 });
    const sample = await page.evaluate(() => ({
        detail: window.__planetForgeSurfaceTest.getLodStats(),
        pixels: window.__planetForgeSurfaceTest.measure(),
        overflow: document.documentElement.scrollWidth - window.innerWidth
    }));
    expect(sample.detail.mode).toBe('local');
    expect(sample.detail.localCellSpacingMeters).toBeGreaterThan(0);
    expect(sample.detail.localCellSpacingMeters).toBeLessThan(500);
    expect(sample.detail.localElevationRangeMeters).toBeGreaterThan(0);
    expect(sample.pixels.planetPixels).toBeGreaterThan(10_000);
    expect(sample.pixels.glError).toBe(0);
    expect(sample.overflow).toBeLessThanOrEqual(2);
    await page.screenshot({ path: testInfo.outputPath('3km-physical-regional-bedrock-24cells.png'), fullPage: true });

    await page.evaluate(() => window.__planetForgeSurfaceTest.setDistance(1.0 + 100 / 6_371_000));
    await page.waitForFunction(() => {
        const detail = window.__planetForgeSurfaceTest?.getLodStats();
        return detail?.mode === 'local' && !detail.refining && detail.localCellSpacingMeters < 25;
    }, null, { timeout: 90_000 });
    const close = await page.evaluate(() => ({
        detail: window.__planetForgeSurfaceTest.getLodStats(),
        pixels: window.__planetForgeSurfaceTest.measure()
    }));
    expect(close.detail.localCellSpacingMeters).toBeLessThan(25);
    expect(close.pixels.planetPixels).toBeGreaterThan(10_000);
    expect(close.pixels.glError).toBe(0);
    await page.screenshot({ path: testInfo.outputPath('100m-physical-ground-bedrock-32cells.png'), fullPage: true });
});
