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
  // Rendering may advance through a key moment while SwiftShader compiles.
  // Wait for the actual renderer, then stop its real model clock directly;
  // the Play button may be temporarily disabled during automatic inspection.
  await expect(play).toBeVisible({ timeout: 120_000 });
  await expect(page.getByTestId('formation-renderer-status'))
    .toHaveCount(0, { timeout: 120_000 });
  await page.evaluate(async () => {
    const renderer = await import('/js/planetRenderer.js');
    if (renderer.getSpecialEventStatus().focus) renderer.exitSpecialEvent();
    renderer.setEvolutionPlaying(false);
  });
  await expect(play).toBeEnabled({ timeout: 30_000 });
  await expect(play).toHaveAttribute('aria-pressed', 'false', { timeout: 30_000 });
  await page.getByTestId('formation-restart').click();
  await expect(page.getByTestId('formation-restart-overlay')).toBeVisible();
  await page.getByTestId('formation-restart-confirm').click();
  await expect(page.getByTestId('formation-restart-overlay')).toHaveCount(0);
  // These legacy tests intentionally exercise S0 thermal physics. Advance
  // the separate F0 reconstruction deterministically without waiting through
  // its collision cinema; a dedicated spec captures the actual F0 visuals.
  await page.evaluate(async () => {
    const renderer = await import('/js/planetRenderer.js');
    const engine = await import('/js/evolution/planetEvolution.js');
    const model = renderer.getEvolutionDebug();
    const originalRate = model.maximumYearsPerSecond;
    model.maximumYearsPerSecond = 50e6;
    model.playing = true;
    let steps = 0;
    while (!model.formation.complete && steps++ < 40) engine.stepEvolution(model, 1);
    model.playing = false;
    model.maximumYearsPerSecond = originalRate;
    renderer.setEvolutionClouds(model.showClouds); // schedules the real S0 draw
    if (!model.formation.complete) throw new Error('F0 did not reach thermal handoff.');
  });
  await expect.poll(async () => (await evolutionStatus(page))?.regime, { timeout: 60_000 }).toBe('magma-ocean');
}

test('Evolution_StartsAsGlowingMagmaOcean_WithMinimalHudAndLiberties', async ({ page }, testInfo) => {
  // Magma color/readback is resolution-independent. Keep the GPU pass small
  // enough for SwiftShader; the viewport suites cover desktop/portrait layout.
  await page.setViewportSize({ width: 960, height: 600 });
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
  // A full S0 lid→plates integration is variable under CI software WebGL.
  // Keep a per-test safety margin without relaxing assertion-level deadlines.
  test.setTimeout(420_000);
  // SwiftShader's 1440x900 planetary fragment pass can starve browser input
  // for minutes. This test covers stage transitions at a normal 960x600
  // viewport; dedicated visual tests still exercise desktop/portrait sizes.
  await page.setViewportSize({ width: 960, height: 600 });
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await page.goto(url);
  await waitForReady(page);
  const speed = page.getByTestId('formation-speed');
  for (let i = 0; i < 5 && (await speed.innerText()).trim() !== '50×'; i++) await speed.click();
  await expect(speed).toHaveText('50×');
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
  // The onset inspection must land on the simulation's first plate boundary.
  // Verify against the *real* GPU ownership field, not a hard-coded position.
  const plateFeature = await page.evaluate(async () => {
    const renderer = await import('/js/planetRenderer.js');
    const evolution = await import('/js/evolution/planetEvolution.js');
    const model = renderer.getEvolutionDebug();
    const feature = model.firstPlateFocus;
    const event = renderer.getSpecialEventStatus().events.find(e => e.id.endsWith('-plate-onset'));
    if (!feature || !event) return null;
    const field = event.inspectable ? evolution.readEvolutionField(model, 'state0', true) : null;
    const at = (x, y) => Math.round(field.data[(y * field.width + (x + field.width) % field.width) * 4]);
    const x = feature.gridX, y = feature.gridY;
    const owner = field ? at(x, y) : null;
    const near = field ? [at(x + 4, y), at(x - 4, y),
      at(x, Math.min(field.height - 1, y + 4)), at(x, Math.max(0, y - 4))] : [];
    // Geological playback may have advanced before the slow software-GPU
    // test can poll it. Never request a rewind to an archived event.
    if (event.inspectable) renderer.enterSpecialEvent(event.id);
    const focus = renderer.getSpecialEventStatus().focus;
    return { feature, owner, near, focus, inspectable: event.inspectable };
  });
  expect(plateFeature).not.toBeNull();
  expect(plateFeature.feature.plateId).toBe(0);
  expect(plateFeature.feature.adjacentPlateId).toBeGreaterThan(0);
  if (plateFeature.inspectable) {
    // Only compare a live GPU texture to the captured point while the
    // geological epoch has *not* advanced away from onset.
    expect(plateFeature.owner).toBe(0);
    expect(plateFeature.near).toContain(plateFeature.feature.adjacentPlateId);
    expect(plateFeature.focus.scale).toContain('S0 first modeled plate boundary');
    await page.waitForTimeout(1_600);
    await page.screenshot({ path: testInfo.outputPath('evolution-first-tectonic-plate-focus.png') });
    await page.evaluate(async () => (await import('/js/planetRenderer.js')).exitSpecialEvent());
  }
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
  // Exercise controls at a slow geological rate. The accelerated 50-Myr/s
  // regime test above has already verified the physical plate transition.
  await page.evaluate(async () =>
    (await import('/js/planetRenderer.js')).setEvolutionSpeed(100_000));
  await page.getByTestId('formation-play').click();
  await expect(page.getByTestId('formation-play')).toHaveAttribute('aria-pressed', 'true');
  await expect.poll(async () => page.evaluate(async (pausedSeconds) => {
    const renderer = await import('/js/planetRenderer.js');
    // Fast-forwarding may encounter another major key moment between the
    // Play click and this poll. Complete that inspection before advancing.
    if (renderer.getSpecialEventStatus().focus) renderer.exitSpecialEvent();
    const status = renderer.getEvolutionStatus();
    // Stop at the first actual post-resume step before another key moment
    // can disable the UI button on this accelerated thermal run.
    if (status.timeSeconds > pausedSeconds) renderer.setEvolutionPlaying(false);
    return status.timeSeconds;
  }, paused), { timeout: 45_000, intervals: [500] }).toBeGreaterThan(paused);
  await expect.poll(async () => (await evolutionStatus(page)).playing, { timeout: 15_000 }).toBe(false);
  await expect(page.getByTestId('special-event-focus')).toHaveCount(0);
  await expect(page.getByTestId('formation-play')).toHaveAttribute('aria-pressed', 'false', { timeout: 15_000 });

  const clouds = page.getByTestId('formation-clouds');
  await expect(clouds).toHaveAttribute('aria-pressed', 'true');
  await clouds.click();
  await expect(clouds).toHaveAttribute('aria-pressed', 'false');
  await clouds.click();
  await expect(clouds).toHaveAttribute('aria-pressed', 'true');

  // Explicitly leave any auto-inspection before invoking the real Restart
  // button; accelerated geology tests must not race its disabled state.
  await page.evaluate(async () => {
    const renderer = await import('/js/planetRenderer.js');
    if (renderer.getSpecialEventStatus().focus) renderer.exitSpecialEvent();
    renderer.setEvolutionPlaying(false);
  });
  await expect(page.getByTestId('formation-restart')).toBeEnabled({ timeout: 30_000 });
  await page.getByTestId('formation-restart').click();
  await expect(page.getByTestId('formation-restart-overlay')).toBeVisible();
  await page.getByTestId('formation-restart-confirm').click();
  await expect.poll(async () => (await evolutionStatus(page)).regime, { timeout: 60_000 }).toBe('formation');
  await expect(page.getByTestId('formation-phase')).toContainText('Proto-Earth');
  expect(errors).toEqual([]);
});

test('Evolution_Portrait_ControlsFit', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(url);
  await waitForReady(page);
  for (const id of ['formation-play', 'formation-speed', 'formation-clouds', 'formation-restart']) {
    await expect(page.getByTestId(id)).toBeInViewport();
    const dimensions = await page.getByTestId(id).boundingBox();
    expect(dimensions.width).toBeGreaterThanOrEqual(42);
    expect(dimensions.height).toBeGreaterThanOrEqual(42);
  }
  const hudBox = await page.getByTestId('formation-hud').boundingBox();
  const railBox = await page.getByTestId('special-events-timeline').boundingBox();
  expect(hudBox.height).toBeLessThan(85);
  expect(railBox.height).toBeLessThan(100);
  await expect(page.getByTestId('timeline-planned-node')).toHaveCount(8);
  await expect(page.getByTestId('timeline-overview-node')).toHaveCount(10);
  await expect(page.getByTestId('timeline-origin-node')).toHaveCount(1);
  await expect(page.getByTestId('timeline-present-node')).toHaveCount(1);
  const dockBox = await page.locator('.formation-controls').boundingBox();
  expect(Math.abs(dockBox.y - hudBox.y)).toBeLessThan(2);
  expect(dockBox.y + dockBox.height).toBeLessThan(railBox.y);
  const controls = await page.locator('.formation-controls button').evaluateAll(buttons =>
    buttons.map(button => {
      const bounds = button.getBoundingClientRect();
      return { left: bounds.left, top: bounds.top, bottom: bounds.bottom };
    }));
  expect(controls).toHaveLength(4);
  for (const control of controls) expect(control.left).toBeGreaterThan(310);
  for (let i = 1; i < controls.length; i++) expect(controls[i].top).toBeGreaterThan(controls[i - 1].bottom);
  expect(controls[3].bottom).toBeLessThan(railBox.y);

  // The entire 4.567-Gyr overview must be linearly spaced, including late-life anchors.
  const anchors = await page.getByTestId('timeline-overview-node').evaluateAll(nodes => nodes.map(node => ({
    name: node.dataset.chapter,
    years: Number(node.dataset.years),
    percent: Number.parseFloat(node.style.left)
  })));
  for (const anchor of anchors)
    expect(anchor.percent).toBeCloseTo(anchor.years / 4_567_000_000 * 100, 4);
  expect(anchors[0].name).toBe('formation');
  expect(anchors.at(-1).name).toBe('present');

  const speedBefore = await page.getByTestId('formation-speed').innerText();
  await page.getByTestId('formation-speed').click();
  expect(await page.getByTestId('formation-speed').innerText()).not.toBe(speedBefore);
  const timeBefore = (await evolutionStatus(page)).timeSeconds;
  // The future chapter is currently beyond the magnified physical window.
  // Querying it via synthetic DOM click tests its non-seeking information panel.
  await page.getByTestId('timeline-planned-node').nth(3).evaluate(element => element.click());
  await expect(page.getByTestId('timeline-planned-preview')).toContainText('Earliest life evidence');
  expect((await evolutionStatus(page)).timeSeconds).toBe(timeBefore);
  await page.getByTestId('formation-restart').click();
  await expect(page.getByTestId('formation-restart-overlay')).toBeVisible();
  await page.getByTestId('formation-restart-cancel').click();
  await expect(page.getByTestId('formation-restart-overlay')).toHaveCount(0);
  expect((await evolutionStatus(page)).timeSeconds).toBe(timeBefore);
  expect((await canvasStatistics(page)).glError).toBe(0);
  await page.screenshot({ path: testInfo.outputPath('planetforge-mobile-right-dock-linear-time.png') });
  // The top-aligned dock must never cover the phase chip on a 320 px phone.
  await page.setViewportSize({ width: 320, height: 700 });
  const compactHud = await page.getByTestId('formation-hud').boundingBox();
  const compactDock = await page.locator('.formation-controls').boundingBox();
  expect(compactHud.x + compactHud.width + 8).toBeLessThanOrEqual(compactDock.x);
  await page.setViewportSize({ width: 390, height: 844 });

  // UI-only lens probe: inject a model clock sample into this disposable paused
  // browser test (not production simulation) to verify camera panning and scale.
  await page.evaluate(async () => {
    const renderer = await import('/js/planetRenderer.js');
    renderer.getEvolutionDebug().thermal.timeSeconds = 1_250_000_000 * 31_557_600;
  });
  const rail = page.getByTestId('special-event-rail');
  await expect.poll(async () => Number(await rail.getAttribute('data-window-now-years')),
    { timeout: 15_000 }).toBeGreaterThan(1_200_000_000);
  const lens = await rail.evaluate(element => ({
    start: Number(element.dataset.windowStartYears),
    span: Number(element.dataset.windowSpanYears),
    now: Number(element.dataset.windowNowYears)
  }));
  expect(lens.start).toBeGreaterThan(0);
  expect(lens.span).toBeGreaterThan(25_000);
  expect(lens.now).toBeGreaterThanOrEqual(lens.start);
  expect(lens.now).toBeLessThanOrEqual(lens.start + lens.span);
  for (const name of ['first-life', 'oxygen']) {
    const node = page.locator(`[data-testid="timeline-planned-node"][data-chapter="${name}"]`);
    const point = await node.evaluate(element => ({
      year: Number(element.dataset.years),
      percent: Number.parseFloat(element.style.left)
    }));
    expect(point.percent).toBeCloseTo((point.year - lens.start) / lens.span * 100, 3);
  }
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
  await expect(timeline).toContainText('MILESTONES');
  await expect(timeline).toContainText('LINEAR TIME');
  await expect(page.getByTestId('timeline-overview-node')).toHaveCount(10);
  await expect(page.getByTestId('timeline-present-node')).toHaveCount(1);
  const railBox = await rail.boundingBox();
  expect(railBox.width).toBeGreaterThan(railBox.height * 5);
  const before = await page.evaluate(async () =>
    (await import('/js/planetRenderer.js')).getPlanetCameraSnapshot());

  // Monitor the real thermal transition inside Chromium: SwiftShader-backed
  // Playwright UI calls can outlast a 14-second close-up.
  await page.evaluate(async () => {
    const renderer = await import('/js/planetRenderer.js');
    const model = renderer.getEvolutionDebug();
    const probe = { samples: [], sawFocus: false, sawImpact: false,
      sawFocusHud: false, sawDisabledControls: false, sawPausedModel: false,
      dotSize: null, completed: false, timelineEvents: [] };
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
        const sphere = window.getComputedStyle(dot, '::before');
        probe.dotSize = { width: bounds.width, height: bounds.height,
          sphereWidth: parseFloat(sphere.width), sphereHeight: parseFloat(sphere.height) };
      }
      if (status.focus) {
        probe.sawFocus = true;
        probe.sawPausedModel ||= renderer.getEvolutionStatus().playing === false;
        probe.samples.push({
          title: status.focus.title,
          source: status.focus.source,
          scale: status.focus.scale,
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
  expect(probe.sawPausedModel).toBe(true);
  expect(Math.abs(probe.dotSize.width - probe.dotSize.height)).toBeLessThan(2);
  // The visual sphere stays small, while its transparent touch target is usable on phones.
  expect(probe.dotSize.width).toBeGreaterThanOrEqual(36);
  expect(probe.dotSize.sphereWidth).toBeLessThanOrEqual(16);
  expect(Math.abs(probe.dotSize.sphereWidth - probe.dotSize.sphereHeight)).toBeLessThan(2);
  const marker = page.getByTestId('special-event-enter').first();
  await expect(marker).toHaveAttribute('aria-label', /Persistent rocky lid forms/);
  // Software WebGL may stall 75-ms probes for the entire cinematic hold.
  // One witnessed focus, frozen parent model, and restored original orbit
  // remain meaningful regardless of how many timer callbacks run.
  expect(probe.samples.length).toBeGreaterThanOrEqual(1);
  expect(probe.samples[0].title).toBe('Persistent rocky lid forms');
  expect(probe.samples[0].source).toMatch(/^Model-state threshold/);
  expect(probe.samples[0].scale).toMatch(/Planetary orbital close-up/);
  expect(probe.timelineEvents.every(event => !/impact/i.test(event.title))).toBe(true);
  expect(probe.timelineEvents.every(event =>
    event.authority === 'model-observed' &&
    event.scope === 'global' &&
    event.interval.startSeconds <= event.planetSeconds &&
    event.interval.endSeconds === event.planetSeconds &&
    event.trigger && event.source && event.uncertainty && event.scale)).toBe(true);
  const eventLog = page.getByTestId('special-event-log');
  await expect(eventLog).toBeVisible();
  await expect(eventLog).not.toHaveAttribute('open', '');
  await eventLog.locator('summary').click();
  await expect(eventLog).toHaveAttribute('open', '');
  await expect(eventLog).toContainText('MODEL OBSERVATION');
  await expect(eventLog).toContainText('Recorded model step:');

  await expect(page.getByTestId('special-event-focus')).toHaveCount(0);
  await expect(page.getByTestId('formation-play')).toBeEnabled();
  const restored = await page.evaluate(async () =>
    (await import('/js/planetRenderer.js')).getPlanetCameraSnapshot());
  expect(restored.distance).toBeCloseTo(before.distance, 6);
  expect((await evolutionStatus(page)).playing).toBe(false);
  // Verify the production clock can resume after the orbital inspection.
  // Its long-term progression is covered by the separate thermal regime test;
  // do not race another auto-inspection in this GPU-heavy zoom regression.
  const resumed = await page.evaluate(async () => {
    const renderer = await import('/js/planetRenderer.js');
    renderer.setEvolutionSpeed(100_000);
    renderer.setEvolutionPlaying(true);
    const playing = renderer.getEvolutionStatus().playing;
    renderer.setEvolutionPlaying(false);
    return playing;
  });
  expect(resumed).toBe(true);
  expect(errors).toEqual([]);
});
