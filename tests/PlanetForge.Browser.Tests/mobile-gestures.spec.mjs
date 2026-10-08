import { expect, test } from '@playwright/test';

test.use({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 1, hasTouch: true, isMobile: true });
test.setTimeout(120_000);

test('Mobile_PinchZoom_ZoomsInAndOut_AndPreservesSingleFingerRotation', async ({ page }, testInfo) => {
  await page.goto('/?visualTest=1');
  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest), null, { timeout: 45_000 });

  const canvas = page.locator('#planet-canvas');
  await expect(canvas).toBeVisible();
  expect(await canvas.evaluate(element => getComputedStyle(element).touchAction)).toBe('none');

  const bounds = await canvas.boundingBox();
  expect(bounds).not.toBeNull();
  const centerX = Math.round(bounds.x + bounds.width / 2);
  const centerY = Math.round(bounds.y + bounds.height / 2);
  const session = await page.context().newCDPSession(page);
  const distance = () => page.evaluate(() => window.__planetForgeSurfaceTest.getDistance());
  const yaw = () => page.evaluate(() => window.__planetForgeSurfaceTest.getYaw());
  const startDistance = await distance();

  const touches = (offset) => [
    { x: centerX - offset, y: centerY },
    { x: centerX + offset, y: centerY }
  ];

  await session.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: touches(35) });
  await session.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: touches(60) });
  await session.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: touches(85) });
  const zoomedIn = await distance();
  expect(zoomedIn).toBeLessThan(startDistance * 0.85);

  await session.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: touches(50) });
  await session.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: touches(30) });
  const zoomedOut = await distance();
  expect(zoomedOut).toBeGreaterThan(zoomedIn * 1.4);
  await session.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });

  const startYaw = await yaw();
  await session.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: centerX, y: centerY }] });
  await session.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: centerX + 45, y: centerY }] });
  await session.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
  expect(Math.abs(await yaw() - startYaw)).toBeGreaterThan(0.1);

  const metrics = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
  expect(metrics.planetPixels).toBeGreaterThan(10_000);
  expect(metrics.glError).toBe(0);
  await page.screenshot({ path: testInfo.outputPath('mobile-pinch-zoom.png'), fullPage: true });
});


test('Mobile_PinchPause_DefersMeshRequestsUntilAllFingersReleased', async ({ page }) => {
  await page.goto('/?visualTest=1');
  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest), null, { timeout: 45_000 });

  // Begin in adaptive orbit. A pause partway through a pinch must not run WebAssembly mesh
  // generation while the user is still manipulating the camera.
  await page.evaluate(() => window.__planetForgeSurfaceTest.setDistance(1.2));
  await page.waitForFunction(() => !window.__planetForgeSurfaceTest.getLodStats().refining, null, { timeout: 75_000 });
  const before = await page.evaluate(() => window.__planetForgeSurfaceTest.getLodStats().surfaceRequestsStarted);
  const bounds = await page.locator('#planet-canvas').boundingBox();
  expect(bounds).not.toBeNull();
  const centerX = Math.round(bounds.x + bounds.width / 2);
  const centerY = Math.round(bounds.y + bounds.height / 2);
  const touches = offset => [
    { x: centerX - offset, y: centerY },
    { x: centerX + offset, y: centerY }
  ];
  const session = await page.context().newCDPSession(page);

  await session.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: touches(36) });
  await session.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: touches(49) });
  await page.waitForTimeout(350);

  const during = await page.evaluate(() => window.__planetForgeSurfaceTest.getLodStats());
  expect(during.surfaceRequestsStarted).toBe(before);
  expect(during.refining).toBe(true);

  await session.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
  await page.waitForFunction(previous => window.__planetForgeSurfaceTest.getLodStats().surfaceRequestsStarted > previous,
    before, { timeout: 45_000 });
  await page.waitForFunction(() => !window.__planetForgeSurfaceTest.getLodStats().refining, null, { timeout: 75_000 });
  await page.waitForFunction(() => window.__planetForgeSurfaceTest.getLodStats().lastTerrainDrawSubmittedMs !== null,
    null, { timeout: 15_000 });

  const after = await page.evaluate(() => window.__planetForgeSurfaceTest.getLodStats());
  expect(after.surfaceRequestsStarted).toBe(before + 1);
  expect(after.lastGeometryCommitMs).toBeGreaterThanOrEqual(0);
  expect(after.lastTerrainDrawSubmittedMs).toBeGreaterThanOrEqual(after.lastGeometryCommitMs);
  expect(after.lastGestureToTerrainDrawMs).toBeGreaterThanOrEqual(0);
  expect(after.surfaceResponsesSuperseded).toBeGreaterThanOrEqual(0);
});
