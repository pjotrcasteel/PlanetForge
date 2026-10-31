import { expect, test } from '@playwright/test';

test.setTimeout(300_000);
const url = '/?visualTest=1&seed=24061984';

async function evolutionStatus(page) {
  return page.evaluate(async () => {
    const m = await import('/js/planetRenderer.js');
    return m.getEvolutionStatus();
  });
}

async function canvasStatistics(page) {
  return page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => {
    // Without preserveDrawingBuffer, WebGL may clear the previous draw before
    // a separate readPixels call. Render and sample in the SAME callback.
    window.__planetForgeSurfaceTest.measure();
    const canvas = document.querySelector('#planet-canvas');
    const gl = canvas.getContext('webgl2');
    const pixels = new Uint8Array(canvas.width * canvas.height * 4);
    gl.readPixels(0, 0, canvas.width, canvas.height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
    let sum = 0, count = 0, min = 1, max = 0, red = 0, blue = 0;
    for (let y = Math.floor(canvas.height * .3); y < canvas.height * .7; y += 3) {
      for (let x = Math.floor(canvas.width * .4); x < canvas.width * .6; x += 3) {
        const i = (y * canvas.width + x) * 4;
        const r = pixels[i] / 255, g = pixels[i + 1] / 255, b = pixels[i + 2] / 255;
        const value = .2126 * r + .7152 * g + .0722 * b;
        sum += value; count++; min = Math.min(min, value); max = Math.max(max, value);
        red += r; blue += b;
      }
    }
    resolve({ mean: sum / count, range: max - min, redMean: red / count, blueMean: blue / count, glError: gl.getError() });
  })));
}

async function waitForReady(page) {
  const play = page.getByTestId('formation-play');
  await expect(play).toBeEnabled({ timeout: 120_000 });
  // Startup can advance geological time while software WebGL is compiling.
  // Freeze and restart via the real UI so each visual assertion begins at a
  // deterministic magma-ocean frame without changing production autoplay.
  await expect(play).toHaveAttribute('aria-pressed', 'true');
  await play.click();
  await expect(play).toHaveAttribute('aria-pressed', 'false');
  await page.getByTestId('formation-restart').click();
  await expect.poll(async () => (await evolutionStatus(page))?.regime, { timeout: 60_000 }).toBe('magma-ocean');
}

test('Evolution_StartsAsGlowingMagmaOcean_WithMinimalHudAndLiberties', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await page.goto(url);
  await waitForReady(page);
  await expect(page.getByTestId('formation-phase')).toContainText('Magma ocean');
  await expect(page.getByTestId('airless-enter')).toHaveCount(0);
  await expect(page.getByTestId('regional-switch')).toHaveCount(0);
  await page.waitForTimeout(2_000);
  const stats = await canvasStatistics(page);
  expect(stats.glError).toBe(0);
  expect(stats.mean).toBeGreaterThan(.05);
  expect(stats.redMean).toBeGreaterThan(stats.blueMean);
  await page.locator('.formation-details summary').click();
  await expect(page.locator('.formation-details')).toContainText('Display liberties');
  await expect(page.getByTestId('formation-temperature')).toContainText('MANTLE Tp');
  await page.screenshot({ path: testInfo.outputPath('evolution-magma-ocean.png') });
  expect(errors).toEqual([]);
});

test('Evolution_ColdStartKeepsControlsDisabledUntilRendererIsReady', async ({ page }) => {
  await page.route('**/js/planetRenderer.js', async route => {
    const response = await route.fetch();
    await new Promise(resolve => setTimeout(resolve, 2_000));
    await route.fulfill({ response });
  });
  await page.goto(url);
  await expect(page.getByTestId('formation-renderer-status')).toBeVisible({ timeout: 60_000 });
  for (const id of ['formation-play', 'formation-speed', 'formation-restart']) await expect(page.getByTestId(id)).toBeDisabled();
  await expect(page.getByTestId('formation-renderer-status')).toHaveCount(0, { timeout: 120_000 });
  for (const id of ['formation-play', 'formation-speed', 'formation-restart']) await expect(page.getByTestId(id)).toBeEnabled();
});

test('Evolution_CoolsThroughLidIntoPlateTectonics_PausesAndRestarts', async ({ page }, testInfo) => {
  // SwiftShader's 1440x900 planetary fragment pass can starve browser input
  // for minutes. This test covers stage transitions at a normal 960x600
  // viewport; dedicated visual tests still exercise desktop/portrait sizes.
  await page.setViewportSize({ width: 960, height: 600 });
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await page.goto(url);
  await waitForReady(page);
  await page.getByTestId('formation-speed').selectOption('50');
  await page.getByTestId('formation-play').click();
  await expect(page.getByTestId('formation-play')).toHaveAttribute('aria-pressed', 'true');
  await expect.poll(async () => (await evolutionStatus(page)).regime, { timeout: 120_000, intervals: [1_000] }).toBe('stagnant-lid');
  await expect(page.getByTestId('formation-phase')).toContainText(/Stagnant lid|Steam atmosphere|Water world/);
  await page.screenshot({ path: testInfo.outputPath('evolution-stagnant-lid.png') });
  // This test measures the thermal regimes, not the cinematic hold duration.
  // Return from a real key moment so its 14-second pause cannot consume the
  // already GPU-heavy integration-test timeout.
  await page.evaluate(async () => {
    const renderer = await import('/js/planetRenderer.js');
    if (renderer.getSpecialEventStatus().focus) renderer.exitSpecialEvent();
  });
  // The reconstructed Earth renderer is GPU-heavy in software Chromium.
  // As soon as the real physical model reaches plates, pause through the
  // exported production API in the same browser evaluation. This prevents
  // continued GPU work from starving the subsequent UI interaction test.
  await expect.poll(async () => page.evaluate(async () => {
    const renderer = await import('/js/planetRenderer.js');
    // Any ocean/plate key moment can enter while the accelerated geological
    // model runs. Return via production API before reading the next regime.
    if (renderer.getSpecialEventStatus().focus) renderer.exitSpecialEvent();
    const status = renderer.getEvolutionStatus();
    if (status?.regime === 'plates') renderer.setEvolutionPlaying(false);
    return status?.regime;
  }), { timeout: 180_000, intervals: [1_000] }).toBe('plates');
  // From plate onset the Earth regime takes over and names the era.
  await expect(page.getByTestId('formation-phase')).toContainText(/Plate tectonics|Ma ·/);
  await page.waitForTimeout(5_000);
  const status = await evolutionStatus(page);
  expect(status.plates).toBeGreaterThanOrEqual(3);
  expect(status.framesPerSecond).toBeGreaterThan(20);
  const stats = await canvasStatistics(page);
  expect(stats.glError).toBe(0);
  expect(stats.range).toBeGreaterThan(.02);
  await page.screenshot({ path: testInfo.outputPath('evolution-plates.png') });

  // The test stopped at plates deliberately; Blazor should reflect this
  // pause after its next 250-ms HUD poll, with no inspection locking controls.
  await expect(page.getByTestId('special-event-focus')).toHaveCount(0, { timeout: 20_000 });
  await expect(page.getByTestId('formation-play')).toBeEnabled({ timeout: 20_000 });
  await expect(page.getByTestId('formation-play')).toHaveAttribute('aria-pressed', 'false');
  const paused = (await evolutionStatus(page)).timeSeconds;
  await page.waitForTimeout(1_000);
  expect((await evolutionStatus(page)).timeSeconds).toBe(paused);
  await page.getByTestId('formation-play').click();
  await expect(page.getByTestId('formation-play')).toHaveAttribute('aria-pressed', 'true');
  await expect.poll(async () => (await evolutionStatus(page)).timeSeconds, { timeout: 15_000 })
    .toBeGreaterThan(paused);
  await page.getByTestId('formation-play').click();
  await expect(page.getByTestId('formation-play')).toHaveAttribute('aria-pressed', 'false');

  const clouds = page.getByTestId('formation-clouds');
  await expect(clouds).toHaveText('Clouds on');
  await clouds.click();
  await expect(clouds).toHaveText('Clouds off');
  await clouds.click();
  await expect(clouds).toHaveText('Clouds on');

  await page.getByTestId('formation-restart').click();
  await expect.poll(async () => (await evolutionStatus(page)).regime, { timeout: 60_000 }).toBe('magma-ocean');
  await expect(page.getByTestId('formation-phase')).toContainText('Magma ocean');
  expect(errors).toEqual([]);
});

test('Evolution_Portrait_ControlsFit', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(url);
  await waitForReady(page);
  for (const id of ['formation-play', 'formation-speed', 'formation-clouds', 'formation-restart']) await expect(page.getByTestId(id)).toBeInViewport();
  expect((await canvasStatistics(page)).glError).toBe(0);
});


test('PerformanceDiagnostics_OptIn_RecordsFramesAndDoesNotAlterEvolution', async ({ page }) => {
  await page.goto(url + '&pfPerf=1');
  await expect(page.getByTestId('formation-play')).toBeEnabled({ timeout: 120_000 });
  await expect.poll(async () => page.evaluate(async () => {
    const m = await import('/js/planetRenderer.js');
    return m.getPerformanceReport()?.durations?.visibleFrameIntervalMs?.count ?? 0;
  }), { timeout: 45_000 }).toBeGreaterThan(0);
  const report = await page.evaluate(async () => (await import('/js/planetRenderer.js')).getPerformanceReport());
  expect(report.enabled).toBe(true);
  expect(report.canvas.width).toBeGreaterThan(0);
  expect(report.durations.rendererInitializeCpuMs.count).toBe(1);
  expect(report.durations.visibleFrameIntervalMs.p95Ms).toBeGreaterThanOrEqual(0);
  expect(report.geologyRegime).toMatch(/magma-ocean|stagnant-lid|plates/);
  expect(report.measurementType).toBe('cpu-wall-and-gpu-readback-sync');
  // The same session remains physically running, with no extra pixel readback.
  await expect(page.getByTestId('formation-play')).toHaveAttribute('aria-pressed', 'true');
});


test('Evolution_KeyMilestone_AutoZooms_WhileOrdinaryImpactsStayAmbient', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 720, height: 460 });
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  await page.goto(url);
  await waitForReady(page);

  const timeline = page.getByTestId('special-events-timeline');
  const rail = page.getByTestId('special-event-rail');
  await expect(timeline).toBeVisible();
  await expect(timeline).toContainText('EARTH HISTORY');
  await expect(timeline).toContainText('Major planetary changes');
  const railBox = await rail.boundingBox();
  expect(railBox.width).toBeGreaterThan(railBox.height * 8);
  const before = await page.evaluate(async () =>
    (await import('/js/planetRenderer.js')).getPlanetCameraSnapshot());

  // Monitor the real thermal transition inside Chromium: SwiftShader-backed
  // Playwright UI calls can outlast a 14-second close-up.
  await page.evaluate(async () => {
    const renderer = await import('/js/planetRenderer.js');
    const model = renderer.getEvolutionDebug();
    const probe = { samples: [], sawFocus: false, sawImpact: false,
      sawFocusHud: false, sawDisabledControls: false, dotSize: null,
      completed: false, timelineEvents: [] };
    window.__pfKeyMomentProbe = probe;
    const timer = setInterval(() => {
      const status = renderer.getSpecialEventStatus();
      probe.sawImpact ||= model.magmaImpacts.length > 0;
      probe.timelineEvents = status.events;
      const focusHud = document.querySelector('[data-testid="special-event-focus"]');
      probe.sawFocusHud ||= Boolean(focusHud?.textContent?.includes('Model-state threshold'));
      probe.sawDisabledControls ||= Boolean(document.querySelector('[data-testid="formation-play"]')?.disabled);
      const dot = document.querySelector('[data-testid="special-event-enter"]');
      if (dot && !probe.dotSize) {
        const bounds = dot.getBoundingClientRect();
        probe.dotSize = { width: bounds.width, height: bounds.height };
      }
      if (status.focus) {
        probe.sawFocus = true;
        probe.samples.push({
          title: status.focus.title,
          source: status.focus.source,
          elapsed: status.focus.elapsedSeconds,
          epoch: renderer.getEvolutionStatus().timeSeconds,
          distance: renderer.getPlanetCameraSnapshot().distance
        });
      } else if (probe.sawFocus) {
        // Freeze the resumed parent before the next major milestone can
        // immediately start its own 14-second inspection.
        renderer.setEvolutionPlaying(false);
        clearInterval(timer);
        probe.completed = true;
      }
    }, 75);
    // The real seeded impact emitter still runs, but only a physically
    // observed solid-lid transition should claim a key-moment zoom.
    model.nextMagmaImpact = 0;
    renderer.setEvolutionSpeed(50e6);
    renderer.setEvolutionPlaying(true);
  });

  await expect.poll(async () => page.evaluate(() => window.__pfKeyMomentProbe.sawFocus),
    { timeout: 120_000, intervals: [250] }).toBe(true);
  // Capture a real WebGL frame while the browser-side probe records the
  // HUD and camera; do not race the short auto-return with slow UI calls.
  await page.screenshot({ path: testInfo.outputPath('first-rocky-lid-key-moment.png') });
  await expect.poll(async () => page.evaluate(() => window.__pfKeyMomentProbe.completed),
    { timeout: 30_000 }).toBe(true);
  const probe = await page.evaluate(() => window.__pfKeyMomentProbe);
  expect(probe.sawImpact).toBe(true);
  expect(probe.sawFocusHud).toBe(true);
  expect(probe.sawDisabledControls).toBe(true);
  expect(Math.abs(probe.dotSize.width - probe.dotSize.height)).toBeLessThan(2);
  expect(probe.dotSize.width).toBeLessThan(20);
  const marker = page.getByTestId('special-event-enter').first();
  await expect(marker).toHaveAttribute('aria-label', /Persistent rocky lid forms/);
  expect(probe.samples.length).toBeGreaterThan(1);
  expect(probe.samples[0].title).toBe('Persistent rocky lid forms');
  expect(probe.samples[0].source).toBe('Model-state threshold');
  expect(Math.max(...probe.samples.map(sample => sample.elapsed))).toBeGreaterThan(1.3);
  expect(Math.min(...probe.samples.map(sample => sample.distance))).toBeLessThan(before.distance);
  expect(new Set(probe.samples.map(sample => sample.epoch)).size).toBe(1);
  expect(probe.timelineEvents.every(event => !/impact/i.test(event.title))).toBe(true);

  await expect(page.getByTestId('special-event-focus')).toHaveCount(0);
  await expect(page.getByTestId('formation-play')).toBeEnabled();
  const restored = await page.evaluate(async () =>
    (await import('/js/planetRenderer.js')).getPlanetCameraSnapshot());
  expect(restored.distance).toBeCloseTo(before.distance, 6);
  expect((await evolutionStatus(page)).playing).toBe(false);
  // A slow resume proves we returned to the original planet clock without
  // immediately triggering another unrelated geological milestone.
  await page.evaluate(async () => {
    const renderer = await import('/js/planetRenderer.js');
    renderer.setEvolutionSpeed(1e6);
    renderer.setEvolutionPlaying(true);
  });
  await expect.poll(async () => (await evolutionStatus(page)).timeSeconds, { timeout: 15_000 })
    .toBeGreaterThan(probe.samples[0].epoch);
  expect(errors).toEqual([]);
});
