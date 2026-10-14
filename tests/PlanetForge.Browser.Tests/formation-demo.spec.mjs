import { test, expect } from '@playwright/test';
test('Main globe prototype evolves, orbits, pauses and resets on a mobile viewport', async ({ page }) => {
    const errors = []; page.on('pageerror', e => errors.push(e.message));
    await page.goto('/?demo=1&seed=24061984');
    const step = page.getByTestId('formation-step');
    await expect(step).toBeEnabled();
    await expect(page.getByTestId('formation-phase')).toHaveText('Magma ocean');
    const initial = await page.locator('#planet-canvas').screenshot();
    await page.getByTestId('formation-speed').selectOption('30');
    await step.click();
    await expect(page.getByTestId('formation-time')).toContainText('00:00:30');
    const evolved = await page.locator('#planet-canvas').screenshot();
    expect(evolved.equals(initial)).toBe(false);
    // Same view, later simulation time: check material evolution beyond first frame.
    await step.click();
    await expect(page.getByTestId('formation-time')).toContainText('00:01:00');
    const cooling = await page.locator('#planet-canvas').screenshot();
    expect(cooling.equals(evolved)).toBe(false);
    await page.mouse.move(190, 420); await page.mouse.down(); await page.mouse.move(270, 440, { steps: 8 }); await page.mouse.up();
    const rotated = await page.locator('#planet-canvas').screenshot(); expect(rotated.equals(evolved)).toBe(false);
    await page.getByTestId('formation-play').click();
    await expect(page.getByTestId('formation-time')).not.toContainText('00:00:30');
    await page.getByTestId('formation-play').click();
    await expect(step).toBeEnabled();
    await page.getByTestId('formation-restart').click();
    await expect(page.getByTestId('formation-time')).toContainText('00:00:00');
    await expect(page.getByTestId('formation-phase')).toHaveText('Magma ocean');
    expect(errors).toEqual([]);
});
test('Offline package initializes and exposes the same crust progression', async ({ page }) => {
    const errors=[];page.on('pageerror',e=>errors.push(e.message));
    await page.goto('/planetforge-demo.html');
    await expect(page.locator('#readout')).toContainText('Demo T+0 s');
    await page.locator('#speed').selectOption('30'); await page.locator('#step').click();
    await expect(page.locator('#readout')).toContainText('Demo T+30 s');
    await expect(page.locator('#phase')).toHaveText('Crust islands · active lava');
    await page.locator('#reset').click();await expect(page.locator('#readout')).toContainText('Demo T+0 s');
    expect(errors).toEqual([]);
});
