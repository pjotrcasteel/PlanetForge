import { expect, test } from '@playwright/test';

// Genuine WebGL frames from the running game, uploaded as CI visual artifacts.
// The visual QA inspection samples named deterministic F0 states; it does not
// pretend that impact debris is solved by an SPH collision engine.
test.setTimeout(180_000);

test('F0 mobile Earth-Moon formation renders proto-Earth, Theia collision, ejecta and Moon', async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 390, height: 760 });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto('/?visualTest=1&seed=24061984');
    await expect(page.getByTestId('formation-play')).toBeEnabled({ timeout: 120_000 });
    await expect(page.getByTestId('formation-phase')).toContainText('Proto-Earth', { timeout: 45_000 });
    await page.evaluate(async () => {
        (await import('/js/planetRenderer.js')).setEvolutionPlaying(false);
    });
    const cases = [
        { name: 'proto-earth-accretion', stage: 'accretion', years: 45e6, seconds: 7, progress: 0 },
        { name: 'theia-giant-impact', stage: 'impact', years: 60e6, seconds: 3.2, progress: 3.2 },
        { name: 'orbiting-debris-moon', stage: 'moon-assembly', years: 60e6 + 0.8, seconds: 17, progress: 9 },
        { name: 'postimpact-hot-earth', stage: 'aftermath', years: 60e6 + 1, seconds: 28, progress: 1 },
    ];
    for (const scene of cases) {
        const state = await page.evaluate(async frame => {
            const renderer = await import('/js/planetRenderer.js');
            const model = renderer.getEvolutionDebug();
            model.formation.stage = frame.stage;
            model.formation.ageYears = frame.years;
            model.formation.visualSeconds = frame.seconds;
            model.formation.stageSeconds = frame.progress;
            renderer.setEvolutionClouds(model.showClouds); // draws real WebGL renderer
            return renderer.getEvolutionStatus();
        }, scene);
        expect(state.regime).toBe('formation');
        expect(state.timeSeconds).toBeGreaterThanOrEqual(0);
        await page.waitForTimeout(1_200); // let the actual fragment shader present
        const graphics = await page.evaluate(() => {
            const canvas = document.getElementById('planet-canvas');
            const gl = canvas.getContext('webgl2');
            return { width: canvas.width, height: canvas.height, glError: gl.getError() };
        });
        expect(graphics.width).toBeGreaterThan(0);
        expect(graphics.height).toBeGreaterThan(0);
        expect(graphics.glError).toBe(0);
        await page.screenshot({ path: testInfo.outputPath('f0-' + scene.name + '.png') });
    }
    await expect(page.getByTestId('formation-hud')).toBeVisible();
    await expect(page.getByTestId('special-events-timeline')).toBeVisible();
    const hud = await page.getByTestId('formation-hud').boundingBox();
    const dock = await page.locator('.formation-controls').boundingBox();
    expect(Math.abs(hud.y - dock.y)).toBeLessThan(3);
    expect(errors).toEqual([]);
});
