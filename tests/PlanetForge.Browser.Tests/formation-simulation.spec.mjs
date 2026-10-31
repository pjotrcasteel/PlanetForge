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
  // The reconstructed Earth renderer is GPU-heavy in software Chromium.
  // As soon as the real physical model reaches plates, pause through the
  // exported production API in the same browser evaluation. This prevents
  // continued GPU work from starving the subsequent UI interaction test.
  await expect.poll(async () => page.evaluate(async () => {
    const renderer = await import('/js/planetRenderer.js');
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

  await page.getByTestId('formation-play').click();
  await expect(page.getByTestId('formation-play')).toHaveAttribute('aria-pressed', 'false');
  const paused = (await evolutionStatus(page)).timeSeconds;
  await page.waitForTimeout(1_000);
  expect((await evolutionStatus(page)).timeSeconds).toBe(paused);

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


test('Evolution_WitnessedImpact_AutoZoomsFromHorizontalTimeline_ThenResumesGeology', async ({ page }, testInfo) => {
  // Use the existing seeded model, accelerating only the next impact arrival.
  await page.setViewportSize({ width: 720, height: 460 });
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await page.goto(url);
  await waitForReady(page);

  const timeline = page.getByTestId('special-events-timeline');
  const rail = page.getByTestId('special-event-rail');
  await expect(timeline).toBeVisible();
  await expect(timeline).toContainText('EARTH HISTORY');
  await expect(timeline).toContainText('LOG TIME');
  const railBox = await rail.boundingBox();
  expect(railBox.width).toBeGreaterThan(railBox.height * 8);
  const before = await page.evaluate(async () =>
    (await import('/js/planetRenderer.js')).getPlanetCameraSnapshot());

  // Collect local-clock/camera samples *inside* Chromium while the event is
  // active. SwiftShader UI queries can stall for >14 seconds, after the
  // automatic close-up has already correctly returned to geological time.
  await page.evaluate(async () => {
    const renderer = await import('/js/planetRenderer.js');
    const probe = {
      samples: [], sawFocus: false, completed: false
    };
    window.__pfAutoFocusProbe = probe;
    const timer = setInterval(() => {
      const focus = renderer.getSpecialEventStatus().focus;
      if (focus) {
        probe.sawFocus = true;
        probe.samples.push({
          elapsed: focus.elapsedSeconds,
          epoch: renderer.getEvolutionStatus().timeSeconds,
          distance: renderer.getPlanetCameraSnapshot().distance
        });
      } else if (probe.sawFocus) {
        clearInterval(timer);
        probe.completed = true;
      }
    }, 75);
    const model = renderer.getEvolutionDebug();
    model.nextMagmaImpact = 0;
    renderer.setEvolutionPlaying(true);
  });

  // The camera enters automatically; clicking a marker is not required.
  const focusPanel = page.getByTestId('special-event-focus');
  await expect(focusPanel).toBeVisible({ timeout: 45_000 });
  await expect(focusPanel).toContainText('AUTO ZOOM');
  await expect(focusPanel).toContainText('1× REAL TIME');
  await expect(page.getByTestId('formation-play')).toBeDisabled();
  const marker = page.getByTestId('special-event-enter').last();
  await expect(marker).toBeVisible();
  const dot = await marker.boundingBox();
  expect(Math.abs(dot.width - dot.height)).toBeLessThan(2);
  expect(dot.width).toBeLessThan(20);
  await page.screenshot({ path: testInfo.outputPath('special-impact-horizontal-auto-zoom.png') });

  // Browser-side samples survive after the short focus ends. Verify the
  // close-up really reached the event and never advanced geological time.
  await expect.poll(async () => page.evaluate(() => window.__pfAutoFocusProbe.completed),
    { timeout: 30_000 }).toBe(true);
  const samples = await page.evaluate(() => window.__pfAutoFocusProbe.samples);
  expect(samples.length).toBeGreaterThan(1);
  expect(Math.max(...samples.map(s => s.elapsed))).toBeGreaterThan(1.3);
  expect(Math.min(...samples.map(s => s.distance))).toBeLessThan(before.distance);
  expect(new Set(samples.map(s => s.epoch)).size).toBe(1);

  await expect(focusPanel).toHaveCount(0);
  await expect(page.getByTestId('formation-play')).toBeEnabled();
  const restored = await page.evaluate(async () =>
    (await import('/js/planetRenderer.js')).getPlanetCameraSnapshot());
  expect(restored.distance).toBeCloseTo(before.distance, 6);
  expect((await evolutionStatus(page)).playing).toBe(true);
  // Local inspection does not spend the countdown to the next real impact.
  await page.waitForTimeout(1_200);
  expect((await evolutionStatus(page)).timeSeconds).toBeGreaterThan(samples[0].epoch);
  expect(await page.evaluate(async () =>
    (await import('/js/planetRenderer.js')).getSpecialEventStatus().focus)).toBeNull();
  expect(errors).toEqual([]);
});
