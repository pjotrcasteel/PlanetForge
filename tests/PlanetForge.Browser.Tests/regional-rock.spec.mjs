import { expect, test } from '@playwright/test';

test.setTimeout(180_000);

test('RegionalRock_PhaseDrivenGlobe_AdvancesConservedMaterialWithoutFictionalCrust', async ({ page }, testInfo) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/?visualTest=1&regionalRock=1');
  await expect(page.getByTestId('planet-scenario-label')).toHaveText('REGIONAL ROCK · CONSERVED PHASE REFERENCE', { timeout: 45_000 });
  await expect(page.getByTestId('regional-temperature-range')).toBeVisible();
  await expect(page.getByTestId('regional-melt-range')).toBeVisible();
  await expect(page.getByText('DECLARED THERMAL CONTRAST · NOT CRUST')).toBeVisible();
  await expect(page.getByTestId('airless-elapsed')).toContainText('T+0h');
  await page.waitForFunction(() => Boolean(window.__planetForgeSurfaceTest) || Boolean(window.__planetForgeSurfaceError), null, { timeout: 45_000 });
  expect(await page.evaluate(() => window.__planetForgeSurfaceError ?? null)).toBeNull();

  const before = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
  expect(before.planetPixels).toBeGreaterThan(10_000);
  expect(before.glError).toBe(0);
  const initialReadout = await page.getByTestId('regional-temperature-range').textContent();
  await page.screenshot({ path: testInfo.outputPath('regional-rock-melting.png') });

  await page.getByTestId('airless-advance').click();
  await expect(page.getByTestId('airless-elapsed')).toContainText('T+1h', { timeout: 90_000 });
  await expect(page.getByTestId('airless-status')).toContainText('ACCEPTED');
  await expect(page.getByTestId('regional-temperature-range')).not.toHaveText(initialReadout);
  const after = await page.evaluate(() => window.__planetForgeSurfaceTest.measure());
  expect(after.planetPixels).toBeGreaterThan(10_000);
  expect(after.glError).toBe(0);
  await page.screenshot({ path: testInfo.outputPath('regional-rock-cooling.png') });

  await page.getByTestId('airless-reset').click();
  await expect(page.getByTestId('airless-elapsed')).toContainText('T+0h', { timeout: 45_000 });
  await expect(page.getByTestId('regional-temperature-range')).toHaveText(initialReadout);
  expect(errors).toEqual([]);
});

test('RegionalRock_ExistingOneZoneSimulation_CanSwitchWithoutChangingMainPlanet', async ({ page }) => {
  await page.goto('/?visualTest=1&legacy=1');
  await expect(page.getByTestId('airless-enter')).toBeVisible({ timeout: 45_000 });
  await page.getByTestId('airless-enter').click();
  await expect(page.getByTestId('airless-surface-temperature')).toHaveText('SURFACE 1600 K');
  await page.getByTestId('regional-switch').click();
  await expect(page.getByTestId('regional-temperature-range')).toBeVisible({ timeout: 45_000 });
  await expect(page.getByTestId('planet-scenario-label')).toHaveText('REGIONAL ROCK · CONSERVED PHASE REFERENCE');

  await page.getByTestId('regional-switch').click();
  await expect(page.getByTestId('airless-surface-temperature')).toHaveText('SURFACE 1600 K');
  await page.getByTestId('airless-exit').click();
  await expect(page.getByText('SNOWBALL EXPERIMENT', { exact: true })).toBeVisible();
});


test('RegionalRock_iPhoneScienceHud_LoadsScopedCSSAndKeepsAllReadoutsUnobstructed', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/?visualTest=1&regionalRock=1');
  await expect(page.getByTestId('regional-temperature-range')).toBeVisible({ timeout: 45_000 });
  await expect(page.getByTestId('airless-elapsed')).toContainText('T+0h');

  const layout = await page.evaluate(() => {
    const panel = document.querySelector('.foundation-clock-thermal');
    const temperature = document.querySelector('[data-testid="regional-temperature-range"]');
    const melt = document.querySelector('[data-testid="regional-melt-range"]');
    const elapsed = document.querySelector('[data-testid="airless-elapsed"]');
    const caveat = document.querySelector('.foundation-thermal-limit');
    const stylesheet = Array.from(document.styleSheets).some(sheet =>
      sheet.href?.includes('PlanetForge.Presentation.Web.styles.css'));
    const bounds = element => {
      const r = element.getBoundingClientRect();
      return { top: r.top, bottom: r.bottom, left: r.left, right: r.right, width: r.width, height: r.height };
    };
    return {
      stylesheet,
      panel: bounds(panel),
      temperature: bounds(temperature),
      melt: bounds(melt),
      elapsed: bounds(elapsed),
      caveat: bounds(caveat),
      viewportWidth: window.innerWidth,
      documentWidth: document.documentElement.scrollWidth,
      titleVisible: getComputedStyle(document.querySelector('.foundation-clock-thermal > strong')).display !== 'none'
    };
  });

  expect(layout.stylesheet).toBe(true);
  expect(layout.titleVisible).toBe(true);
  expect(layout.panel.width).toBeGreaterThan(260);
  expect(layout.panel.height).toBeLessThan(160);
  expect(layout.documentWidth).toBeLessThanOrEqual(layout.viewportWidth);
  expect(layout.panel.left).toBeGreaterThanOrEqual(0);
  expect(layout.panel.right).toBeLessThanOrEqual(layout.viewportWidth);
  expect(layout.temperature.left).toBeGreaterThanOrEqual(layout.panel.left);
  expect(layout.temperature.right).toBeLessThanOrEqual(layout.panel.right);
  expect(layout.temperature.bottom).toBeLessThanOrEqual(layout.melt.top + 1);
  expect(layout.melt.right).toBeLessThanOrEqual(layout.elapsed.left + 1);
  expect(layout.elapsed.bottom).toBeLessThanOrEqual(layout.caveat.top + 1);
  await page.screenshot({ path: testInfo.outputPath('regional-rock-iphone-hud.png') });
});


test('RegionalRock_iPhoneActions_StayWithinVisibleViewportAndAdvanceSimulation', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/?visualTest=1&regionalRock=1');
  await expect(page.getByTestId('regional-temperature-range')).toBeVisible({ timeout: 45_000 });
  await expect(page.getByTestId('airless-advance')).toBeVisible();
  await expect(page.getByTestId('airless-reset')).toBeVisible();
  await expect(page.getByTestId('airless-exit')).toBeVisible();
  await expect(page.getByTestId('regional-switch')).toBeVisible();

  const placement = await page.evaluate(() => {
    const stage = document.querySelector('.planet-stage.foundation-stage');
    const controls = document.querySelector('.foundation-controls.foundation-thermal-controls');
    const buttons = Array.from(controls.querySelectorAll('button'));
    const viewportHeight = document.documentElement.clientHeight;
    const viewportWidth = document.documentElement.clientWidth;
    return {
      stageMinHeight: getComputedStyle(stage).minHeight,
      stageBottom: stage.getBoundingClientRect().bottom,
      controlsBottom: controls.getBoundingClientRect().bottom,
      viewportHeight,
      viewportWidth,
      buttons: buttons.map(button => {
        const box = button.getBoundingClientRect();
        const x = (box.left + box.right) / 2;
        const y = (box.top + box.bottom) / 2;
        return {
          name: button.textContent.trim(),
          left: box.left, right: box.right,
          top: box.top, bottom: box.bottom,
          hitTested: button.contains(document.elementFromPoint(x, y))
        };
      })
    };
  });

  expect(placement.stageMinHeight).toBe('0px');
  expect(placement.stageBottom).toBeLessThanOrEqual(placement.viewportHeight + 1);
  expect(placement.controlsBottom).toBeLessThanOrEqual(placement.viewportHeight - 3);
  expect(placement.buttons).toHaveLength(4);
  for (const button of placement.buttons) {
    expect(button.left, button.name).toBeGreaterThanOrEqual(0);
    expect(button.right, button.name).toBeLessThanOrEqual(placement.viewportWidth);
    expect(button.top, button.name).toBeGreaterThanOrEqual(0);
    expect(button.bottom, button.name).toBeLessThanOrEqual(placement.viewportHeight - 3);
    expect(button.hitTested, button.name).toBe(true);
  }

  await page.screenshot({ path: testInfo.outputPath('regional-rock-iphone-actions-visible.png') });
  await page.getByTestId('airless-advance').click();
  await expect(page.getByTestId('airless-elapsed')).toContainText('T+1h', { timeout: 90_000 });
  await expect(page.getByTestId('airless-status')).toContainText('ACCEPTED');
});
