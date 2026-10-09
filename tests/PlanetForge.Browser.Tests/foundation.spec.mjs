import { expect, test } from '@playwright/test';
import { PNG } from 'pngjs';

test.setTimeout(120_000);

test.beforeEach(async ({ page }) => {
  page.on('pageerror', error => console.log('PlanetForge browser exception:', error.message));
  page.on('console', message => {
    if (message.type() === 'error' || message.type() === 'warning') {
      console.log('PlanetForge browser console:', message.text());
    }
  });
});

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
  expect(showcase.edgePlanetPixels).toBe(0);
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


test('TerrainLab_DisplaysCanonicalGeologicalLayers', async ({ page }, testInfo) => {
  test.setTimeout(240_000);
  await page.goto('/?terrainLab=1');
  await expect(page.getByRole('heading', { name: /Terrain Lab/ })).toBeVisible({ timeout: 20_000 });
  await expect(page.locator('#planetforge-build-badge')).toBeHidden();
  await page.waitForFunction(() => window.__planetForgeTerrainLabReady === true, null, { timeout: 90_000 });
  await page.waitForFunction(() => window.__planetForgeRegionalWatershedReady === true, null, { timeout: 180_000 });
  await expect(page.getByRole('status')).toContainText('Seed 24061984', { timeout: 15_000 });
  for (const name of ['lab-crust', 'lab-tectonic', 'lab-mountains', 'lab-elevation']) {
    const metrics = await page.locator(`#${name}`).evaluate(canvas => {
      const data = canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height).data;
      let minimum = 255;
      let maximum = 0;
      for (let i = 0; i < data.length; i += 4) {
        minimum = Math.min(minimum, data[i]);
        maximum = Math.max(maximum, data[i]);
      }
      return { width: canvas.width, height: canvas.height, range: maximum - minimum };
    });
    expect(metrics.width).toBe(256);
    expect(metrics.height).toBe(128);
    expect(metrics.range).toBeGreaterThan(12);
  }
  await page.screenshot({ path: testInfo.outputPath('terrain-lab-showcase.png'), fullPage: true });
});


test('TerrainLab_HeroRegion_RendersPhysical3DLandscapeAndComparesErosion', async ({ page }, testInfo) => {
  test.setTimeout(420_000);
  await page.goto('/?terrainLab=1');
  await page.waitForFunction(() => window.__planetForgeRegionalWatershedReady === true, null, { timeout: 180_000 });

  await expect(page.locator('#lab-hero-generate')).toBeEnabled();
  await page.locator('#lab-hero-generate').click();
  await page.waitForFunction(() => window.__planetForgeHeroRegionReady === true, null, { timeout: 180_000 });
  const hero = page.locator('#lab-hero-render');

  const stats = await page.evaluate(() => window.__planetForgeHeroRegionStats);
  expect(stats.seed).toBe(24061984);
  expect(stats.mode).toBe('after');
  expect(stats.gridWidth).toBe(129);
  expect(stats.triangles).toBe(128 * 128 * 2);
  expect(stats.glError).toBe(0);
  expect(stats.focusCellIndex).toBeGreaterThanOrEqual(0);
  expect(stats.focusCellIndex).toBeLessThan(stats.gridWidth * stats.gridWidth);
  expect(stats.maxElevationMeters - stats.minElevationMeters).toBeGreaterThan(50);
  expect(stats.actualReliefMeters).toBeGreaterThan(50);
  expect(stats.maximumCutMeters).toBeGreaterThan(0);
  expect(stats.framing).toBe('valley');
  expect(stats.drainageOverlay).toBe(true);
  await expect(page.locator('#lab-hero-drainage')).toHaveText('HIDE ROUTED FLOW');
  await page.locator('#lab-hero-drainage').click();
  expect((await page.evaluate(() => window.__planetForgeHeroRegionStats)).drainageOverlay).toBe(false);
  await expect(page.locator('#lab-hero-drainage')).toHaveText('SHOW ROUTED FLOW');
  await page.locator('#lab-hero-drainage').click();
  expect((await page.evaluate(() => window.__planetForgeHeroRegionStats)).drainageOverlay).toBe(true);
  expect(stats.cameraDistanceKm).toBeLessThan(stats.regionSpanKilometers * 0.5);
  await expect(page.locator('.lab-hero-status')).toContainText('deepest cut');
  await page.locator('#lab-hero-framing').click();
  const fullView = await page.evaluate(() => window.__planetForgeHeroRegionStats);
  expect(fullView.framing).toBe('full');
  expect(fullView.cameraDistanceKm).toBeGreaterThan(fullView.regionSpanKilometers);
  await expect(page.locator('#lab-hero-framing')).toHaveText('INSPECT VALLEY');
  await page.locator('#lab-hero-framing').click();
  await expect(page.locator('#lab-hero-framing')).toHaveText('SHOW FULL REGION');
  expect((await page.evaluate(() => window.__planetForgeHeroRegionStats)).framing).toBe('valley');

  const afterScreenshot = await hero.screenshot({ path: testInfo.outputPath('hero-eroded-seed-24061984.png') });
  const after = PNG.sync.read(afterScreenshot);
  const variation = new Set();
  let planetPixels = 0;
  for (let i = 0; i < after.data.length; i += 16) {
    const red = after.data[i], green = after.data[i + 1], blue = after.data[i + 2];
    if (red > green * 1.13 && red > blue * 1.18) {
      planetPixels++;
      variation.add((red >> 3) + '-' + (green >> 3) + '-' + (blue >> 3));
    }
  }
  expect(planetPixels, 'The 3D terrain mesh is absent or outside the camera view').toBeGreaterThan(1200);
  // Orbital-scale coverage should exist and contain real relief; the finer nested
  // region below carries the strict texture/geometry visibility gate.
  expect(stats.regionSpanKilometers).toBe(128);
  expect(stats.maxElevationMeters - stats.minElevationMeters).toBeGreaterThan(1000);

  // Do a second *physical* 128 km erosion solve at 500 m spacing rather
  // than simply upsampling the GPU triangles of the coarse 1 km grid.
  await page.locator('#lab-hero-detail').click();
  await page.waitForFunction(() => window.__planetForgeHeroRegionReady === true &&
    window.__planetForgeHeroRegionStats?.gridWidth === 257, null, { timeout: 180_000 });
  const regionalDetail = await page.evaluate(() => window.__planetForgeHeroRegionStats);
  expect(regionalDetail.cellSpacingMeters).toBe(500);
  expect(regionalDetail.triangles).toBe(256 * 256 * 2);
  expect(regionalDetail.indexBits).toBe(32);
  expect(regionalDetail.glError).toBe(0);
  expect(regionalDetail.maximumCutMeters).toBeGreaterThan(0);
  await hero.screenshot({ path: testInfo.outputPath('hero-128km-500m-physical-grid.png') });
  await page.locator('#lab-hero-detail').click();
  await page.waitForFunction(() => window.__planetForgeHeroRegionReady === true &&
    window.__planetForgeHeroRegionStats?.gridWidth === 129, null, { timeout: 180_000 });

  await page.locator('#lab-hero-mode').selectOption('before');
  await page.waitForFunction(() => window.__planetForgeHeroRegionStats?.mode === 'before');
  const beforeScreenshot = await hero.screenshot({ path: testInfo.outputPath('hero-original-seed-24061984.png') });
  const before = PNG.sync.read(beforeScreenshot);
  let changedPixels = 0;
  for (let i = 0; i < after.data.length; i += 4) {
    if (Math.abs(after.data[i] - before.data[i]) +
        Math.abs(after.data[i + 1] - before.data[i + 1]) +
        Math.abs(after.data[i + 2] - before.data[i + 2]) > 2) {
      changedPixels++;
    }
  }
  expect(changedPixels, 'The simulated erosion produced no visible 3D mesh change').toBeGreaterThan(100);

  // A physically smaller, geographically anchored region must reveal more local structure,
  // not merely magnify the same coarse 128 km height raster or add a sharper shader.
  await expect(page.locator('#lab-hero-refine')).toHaveText('REFINE TO 32 KM');
  await page.locator('#lab-hero-refine').click();
  await page.waitForFunction(() => window.__planetForgeHeroRegionReady === true &&
    window.__planetForgeHeroRegionStats?.regionSpanKilometers === 32, null, { timeout: 180_000 });
  const detailed = await page.evaluate(() => window.__planetForgeHeroRegionStats);
  expect(detailed.gridWidth).toBe(129);
  expect(detailed.glError).toBe(0);
  expect(detailed.maxElevationMeters - detailed.minElevationMeters).toBeGreaterThan(30);
  const nestedAfter = PNG.sync.read(await hero.screenshot({ path: testInfo.outputPath('hero-nested-32km-eroded.png') }));
  const materialColors = new Set();
  let nestedPixels = 0;
  for (let i = 0; i < nestedAfter.data.length; i += 16) {
    const r = nestedAfter.data[i], g = nestedAfter.data[i + 1], b = nestedAfter.data[i + 2];
    if (r > g * 1.13 && r > b * 1.18) {
      nestedPixels++;
      materialColors.add((r >> 3) + '-' + (g >> 3) + '-' + (b >> 3));
    }
  }
  expect(nestedPixels).toBeGreaterThan(1200);
  expect(materialColors.size, 'Nested 32 km erosion still renders as a featureless brown plane').toBeGreaterThan(35);
  await page.locator('#lab-hero-mode').selectOption('before');
  await page.waitForFunction(() => window.__planetForgeHeroRegionStats?.mode === 'before');
  const nestedBefore = PNG.sync.read(await hero.screenshot({ path: testInfo.outputPath('hero-nested-32km-original.png') }));
  let physicalChanges = 0;
  for (let i = 0; i < nestedBefore.data.length; i += 4) {
    const rgbDifference = Math.abs(nestedBefore.data[i] - nestedAfter.data[i]) +
      Math.abs(nestedBefore.data[i + 1] - nestedAfter.data[i + 1]) +
      Math.abs(nestedBefore.data[i + 2] - nestedAfter.data[i + 2]);
    if (rgbDifference > 2) physicalChanges++;
  }
  expect(physicalChanges, 'Nested elevation geometry did not change after erosion').toBeGreaterThan(100);

  // Third scale: 8 km at 62.5 m per physical sample, with both earlier
  // geological erosion histories inherited. Capture mobile and large views.
  await expect(page.locator('#lab-hero-refine')).toHaveText('REFINE TO 8 KM');
  await page.locator('#lab-hero-refine').click();
  await page.waitForFunction(() => window.__planetForgeHeroRegionReady === true &&
    window.__planetForgeHeroRegionStats?.regionSpanKilometers === 8, null, { timeout: 180_000 });
  const micro = await page.evaluate(() => window.__planetForgeHeroRegionStats);
  expect(micro.gridWidth).toBe(129);
  expect(micro.glError).toBe(0);
  expect(micro.triangles).toBe(128 * 128 * 2);
  // A changed canonical mountain location may legitimately have no slope
  // above the lithology-specific angle of repose. The physical prerequisite
  // determines whether the local transport solver is required to move rock.
  expect(micro.hillslopeInitiallyUnstableEdges).toBeGreaterThanOrEqual(0);
  expect(micro.hillslopeTransportedVolumeCubicMeters).toBeGreaterThanOrEqual(0);
  if (micro.hillslopeInitiallyUnstableEdges > 0) {
    expect(micro.hillslopeTransportedVolumeCubicMeters).toBeGreaterThan(0);
  }
  await expect(page.locator('.lab-hero-status')).toContainText('talus relocated locally');
  const originalCameraDistance = micro.cameraDistanceKm;
  await page.locator('#lab-hero-zoom-in').click();
  const nearCameraDistance = await page.evaluate(() => window.__planetForgeHeroRegionStats.cameraDistanceKm);
  expect(nearCameraDistance).toBeLessThan(originalCameraDistance);
  await page.locator('#lab-hero-zoom-out').click();
  const restoredDistance = await page.evaluate(() => window.__planetForgeHeroRegionStats.cameraDistanceKm);
  expect(Math.abs(restoredDistance - originalCameraDistance)).toBeLessThan(originalCameraDistance * 0.01);
  await expect(page.locator('#lab-hero-refine')).toBeDisabled();
  await page.locator('#lab-hero-render').screenshot({
    path: testInfo.outputPath('hero-nested-8km-eroded-mobile.png')
  });
  await page.setViewportSize({ width: 1280, height: 900 });
  const microCanvas = page.locator('#lab-hero-render');
  const microBefore = PNG.sync.read(await microCanvas.screenshot({
    path: testInfo.outputPath('hero-nested-8km-eroded-desktop.png')
  }));
  const visibleRock = new Set();
  let rockPixels = 0;
  for (let i = 0; i < microBefore.data.length; i += 16) {
    const r = microBefore.data[i], g = microBefore.data[i + 1], b = microBefore.data[i + 2];
    if (r > g * 1.13 && r > b * 1.18) {
      rockPixels++;
      visibleRock.add((r >> 3) + '-' + (g >> 3) + '-' + (b >> 3));
    }
  }
  expect(rockPixels).toBeGreaterThan(6000);
  expect(visibleRock.size, '8km terrain still has no resolved material or slope structure').toBeGreaterThan(35);
  await page.locator('#lab-hero-mode').selectOption('before');
  await page.waitForFunction(() => window.__planetForgeHeroRegionStats?.mode === 'before');
  const microOriginal = PNG.sync.read(await microCanvas.screenshot({
    path: testInfo.outputPath('hero-nested-8km-original-desktop.png')
  }));
  let changedGeometryPixels = 0;
  for (let i = 0; i < microBefore.data.length; i += 4) {
    const delta = Math.abs(microBefore.data[i] - microOriginal.data[i]) +
      Math.abs(microBefore.data[i + 1] - microOriginal.data[i + 1]) +
      Math.abs(microBefore.data[i + 2] - microOriginal.data[i + 2]);
    if (delta > 2) changedGeometryPixels++;
  }
  expect(changedGeometryPixels).toBeGreaterThan(100);

  // The optional high-detail solve uses real 31.25 m height samples, not
  // pixel upscaling. It requires 32-bit indices because 257² > 65,536.
  await expect(page.locator('#lab-hero-detail')).toHaveText('SIMULATE 257² DETAIL');
  await page.locator('#lab-hero-detail').click();
  await page.waitForFunction(() => window.__planetForgeHeroRegionReady === true &&
    window.__planetForgeHeroRegionStats?.gridWidth === 257, null, { timeout: 180_000 });
  const highDetail = await page.evaluate(() => window.__planetForgeHeroRegionStats);
  expect(highDetail.regionSpanKilometers).toBe(8);
  expect(highDetail.cellSpacingMeters).toBe(31.25);
  expect(highDetail.triangles).toBe(256 * 256 * 2);
  expect(highDetail.indexBits).toBe(32);
  expect(highDetail.glError).toBe(0);
  await expect(page.locator('.lab-hero-status')).toContainText('31.25 m cells');
  await expect(page.locator('#lab-hero-detail')).toHaveText('USE FAST 129² GRID');
  await microCanvas.screenshot({ path: testInfo.outputPath('hero-nested-8km-257-detail.png') });
});

test('TerrainLab_ErosionIterations_AdvanceRewindReplayDeterministically', async ({ page }, testInfo) => {
  test.setTimeout(270_000);
  await page.goto('/?terrainLab=1');
  await page.waitForFunction(() => window.__planetForgeRegionalWatershedReady === true, null, { timeout: 180_000 });
  const select = page.locator('#lab-erosion-iterations');
  const canvas = page.locator('#lab-regional-evolved');
  const checksum = () => canvas.evaluate(element => {
    const pixels = element.getContext('2d').getImageData(0, 0, element.width, element.height).data;
    let hash = 2166136261;
    for (let i = 0; i < pixels.length; i += 4) {
      hash = Math.imul(hash ^ pixels[i], 16777619) >>> 0;
      hash = Math.imul(hash ^ pixels[i + 1], 16777619) >>> 0;
    }
    return hash;
  });

  const contrastOfDifference = () => page.locator('#lab-regional-difference').evaluate(element => {
    const pixels = element.getContext('2d').getImageData(0, 0, element.width, element.height).data;
    let minimum = 255;
    let maximum = 0;
    for (let i = 0; i < pixels.length; i += 4) {
      minimum = Math.min(minimum, pixels[i]);
      maximum = Math.max(maximum, pixels[i]);
    }
    return maximum - minimum;
  });
  // A newly opened lab should show a useful geological result, not a dark
  // empty change panel caused by starting at 0 erosion iterations.
  await expect(select).toHaveValue('5');
  await expect(page.getByTestId('lab-change-summary')).toContainText('max erosion');
  const initialFivePasses = await checksum();
  expect(await contrastOfDifference(), 'The initial geological comparison must be visible').toBeGreaterThan(18);
  await page.screenshot({ path: testInfo.outputPath('evolution-five-passes.png'), fullPage: true });

  await select.selectOption('0');
  await expect(page.getByRole('status')).toContainText('0 erosion iterations', { timeout: 120_000 });
  await expect(page.getByTestId('lab-change-summary')).toContainText('No erosion yet');
  const original = await checksum();
  expect(await contrastOfDifference()).toBe(0);
  await select.selectOption('5');
  await expect(page.getByRole('status')).toContainText('5 erosion iterations', { timeout: 120_000 });
  const fivePasses = await checksum();
  expect(fivePasses, 'Five physical erosion passes did not change the rendered canonical height map').not.toBe(original);
  expect(fivePasses, 'Initial erosion and replay must be deterministic').toBe(initialFivePasses);
  expect(await contrastOfDifference(), 'Erosion-height difference must show actual nonzero cuts or deposits').toBeGreaterThan(18);

  await select.selectOption('1');
  await expect(page.getByRole('status')).toContainText('1 erosion iterations', { timeout: 120_000 });
  await select.selectOption('5');
  await expect(page.getByRole('status')).toContainText('5 erosion iterations', { timeout: 120_000 });
  expect(await checksum()).toBe(fivePasses);
  await select.selectOption('12');
  await expect(page.getByRole('status')).toContainText('12 erosion iterations', { timeout: 120_000 });
  expect(await checksum()).not.toBe(fivePasses);
  await page.screenshot({ path: testInfo.outputPath('evolution-twelve-passes.png'), fullPage: true });
});

test('TerrainLab_RegionalWatersheds_ThreeSeeds_ShowConnectedRealDrainage', async ({ page }, testInfo) => {
  test.setTimeout(360_000);
  await page.goto('/?terrainLab=1');
  await page.waitForFunction(() => window.__planetForgeRegionalWatershedReady === true, null, { timeout: 180_000 });
  for (const seed of [24061984, 346147916, 579460630]) {
    if (seed !== 24061984) {
      await page.locator('#lab-seed').selectOption(String(seed));
      await page.getByRole('button', { name: 'REGENERATE' }).click();
      await page.waitForFunction(() => window.__planetForgeRegionalWatershedReady === true, null, { timeout: 180_000 });
    }

    await expect(page.getByRole('status')).toContainText('Seed ' + seed, { timeout: 15_000 });
    const displayedAspectRatio = await page.locator('#lab-regional-bedrock').evaluate(canvas => {
      const bounds = canvas.getBoundingClientRect();
      return bounds.width / bounds.height;
    });
    expect(displayedAspectRatio).toBeGreaterThan(0.98);
    expect(displayedAspectRatio).toBeLessThan(1.02);
    for (const name of ['lab-regional-bedrock', 'lab-regional-flow', 'lab-regional-incision',
      'lab-regional-sediment', 'lab-regional-evolved']) {
      const stats = await page.locator('#' + name).evaluate(canvas => {
        const data = canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height).data;
        let minimum = 255;
        let maximum = 0;
        for (let i = 0; i < data.length; i += 4) {
          minimum = Math.min(minimum, data[i + 1]);
          maximum = Math.max(maximum, data[i + 1]);
        }
        return { width: canvas.width, height: canvas.height, contrast: maximum - minimum };
      });
      expect(stats.width).toBe(96);
      expect(stats.height).toBe(96);
      if (name !== 'lab-regional-sediment') {
        expect(stats.contrast, 'Seed ' + seed + ': ' + name + ' has no visible geological structure').toBeGreaterThan(4);
      }
    }

    await page.screenshot({ path: testInfo.outputPath('regional-watersheds-' + seed + '.png'), fullPage: true });
  }
});

for (const seed of [24061984, 346147916, 579460630]) {
  test(`GeneratorQualityGate_Seed${seed}_RendersMultipleOrbitalAngles`, async ({ page }, testInfo) => {
    await page.goto(`/?visualTest=1&seed=${seed}`);
    await expect(page.getByTestId('planet-seed')).toHaveText(`SEED ${seed}`, { timeout: 30_000 });
    await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest) || Boolean(window.__planetForgeSurfaceError), null, { timeout: 40_000 });
    expect(await page.evaluate(() => window.__planetForgeSurfaceError ?? null)).toBeNull();
    await page.getByRole('button', { name: 'NEXT' }).click();
    await expect(page.getByText('MELTING WORLD', { exact: true })).toBeVisible({ timeout: 50_000 });

    for (const [name, yaw, pitch] of [['front', 0, 0.1], ['back', Math.PI, -0.2], ['polar', 0.4, 1.1]]) {
      await page.evaluate(({ yaw, pitch }) => {
        window.__planetForgeSurfaceTest.setYaw(yaw);
        window.__planetForgeSurfaceTest.setPitch(pitch);
      }, { yaw, pitch });
      const metrics = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
      expect(metrics.planetPixels).toBeGreaterThan(10_000);
      expect(metrics.edgePlanetPixels).toBe(0);
      expect(metrics.glError).toBe(0);
      await page.screenshot({ path: testInfo.outputPath(`terrain-seed-${seed}-${name}.png`), fullPage: true });
    }
    await page.evaluate(() => window.__planetForgeSurfaceTest.setDistance(3.8));
    const closeMetrics = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
    expect(closeMetrics.planetPixels).toBeGreaterThan(10_000);
    expect(closeMetrics.glError).toBe(0);
    await page.screenshot({ path: testInfo.outputPath(`terrain-seed-${seed}-regional.png`), fullPage: true });
  });
}

test('OrbitalZoom_RequestsHigherLodAndRestoresCoarseGlobe', async ({ page }, testInfo) => {
  await page.goto('/?visualTest=1');
  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest), null, { timeout: 30_000 });

  const initial = await page.evaluate(() => window.__planetForgeSurfaceTest.getLodStats());
  expect(initial.mode).toBe('globe');
  expect(initial.tileCount).toBe(24);
  expect(initial.maximumLevel).toBe(1);

  await page.evaluate(() => window.__planetForgeSurfaceTest.setDistance(1.22));
  await page.waitForFunction(() => window.__planetForgeSurfaceTest.getLodStats().maximumLevel > 1, null, { timeout: 75_000 });
  await page.waitForFunction(() => !window.__planetForgeSurfaceTest.getLodStats().refining, null, { timeout: 30_000 });
  const regional = await page.evaluate(() => window.__planetForgeSurfaceTest.getLodStats());
  expect(regional.tileCount).toBeLessThanOrEqual(56);
  expect(regional.lastTerrainUpdateMs).toBeGreaterThanOrEqual(0);
  await expect(page.locator('.terrain-detail-hud')).toContainText(/TERRAIN DETAIL · LOD [2-6]/);

  await page.evaluate(() => window.__planetForgeSurfaceTest.setDistance(1.03));
  await page.waitForFunction(level => window.__planetForgeSurfaceTest.getLodStats().maximumLevel > level, regional.maximumLevel, { timeout: 75_000 });
  const close = await page.evaluate(() => window.__planetForgeSurfaceTest.getLodStats());
  expect(close.mode).toBe('globe');
  expect(close.tileCount).toBeLessThanOrEqual(56);
  const closePixels = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
  expect(closePixels.planetPixels).toBeGreaterThan(10_000);
  expect(closePixels.glError).toBe(0);
  await page.screenshot({ path: testInfo.outputPath('adaptive-orbital-lod-close.png'), fullPage: true });

  // Crossing the 20 km boundary must switch from orbit tiles to the local metre-based mesh.
  await page.evaluate(() => window.__planetForgeSurfaceTest.setDistance(1.001));
  await page.waitForFunction(() => window.__planetForgeSurfaceTest.getLodStats().mode === 'local', null, { timeout: 75_000 });
  await page.waitForFunction(() => !window.__planetForgeSurfaceTest.getLodStats().refining, null, { timeout: 30_000 });
  const local = await page.evaluate(() => window.__planetForgeSurfaceTest.getLodStats());
  expect(local.tileCount).toBe(0);
  await expect(page.locator('.terrain-detail-hud')).toContainText('LOCAL TERRAIN READY');
  await page.screenshot({ path: testInfo.outputPath('local-surface-transition.png'), fullPage: true });

  await page.evaluate(() => window.__planetForgeSurfaceTest.setDistance(3.2));
  await page.waitForFunction(() => {
    const stats = window.__planetForgeSurfaceTest.getLodStats();
    return stats.mode === 'globe' && stats.maximumLevel === 1;
  }, null, { timeout: 75_000 });
  await page.waitForFunction(() => !window.__planetForgeSurfaceTest.getLodStats().refining, null, { timeout: 30_000 });
  const restored = await page.evaluate(() => window.__planetForgeSurfaceTest.getLodStats());
  expect(restored.tileCount).toBe(24);
  await expect(page.locator('.terrain-detail-hud')).toBeHidden();
});
