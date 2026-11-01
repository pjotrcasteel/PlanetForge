import { expect, test } from '@playwright/test';
import { PNG } from 'pngjs';

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
    // A 3.4-second slow frame must advance the cinematic by 3.4 seconds,
    // including any leftover time when it crosses a scene boundary.
    const clock = await page.evaluate(async () => {
        const formation = await import('/js/evolution/earthMoonFormation.mjs');
        const state = formation.createEarthMoonFormation(7);
        state.stage = 'impact';
        state.ageYears = formation.IMPACT_AGE_YEARS;
        formation.stepEarthMoonFormation(state, 3.4, 20e6);
        const afterSlowFrame = state.stageSeconds;
        const afterSlowFrameLabel = formation.earthMoonFormationStatus(state).rateLabel;
        formation.stepEarthMoonFormation(state, 9, 20e6);
        const afterBoundary = { stage: state.stage, seconds: state.stageSeconds };
        formation.stepEarthMoonFormation(state, 40, 20e6);
        const finished = state.complete && state.ageYears === formation.HANDOFF_AGE_YEARS;
        const crossing = formation.createEarthMoonFormation(8);
        crossing.ageYears = 59e6;
        formation.stepEarthMoonFormation(crossing, 12, 20e6);
        return { afterSlowFrame, afterSlowFrameLabel, afterBoundary, finished,
            accretionBoundary: { stage: crossing.stage, seconds: crossing.stageSeconds } };
    });
    expect(clock.afterSlowFrame).toBeCloseTo(3.4, 5);
    expect(clock.afterSlowFrameLabel).toContain('3.4 / 9 s');
    expect(clock.afterBoundary.stage).toBe('moon-assembly');
    expect(clock.afterBoundary.seconds).toBeCloseTo(3.4, 5);
    expect(clock.finished).toBe(true);
    expect(clock.accretionBoundary.stage).toBe('moon-assembly');
    expect(clock.accretionBoundary.seconds).toBeCloseTo(2.95, 5);

    const cases = [
        { name: 'proto-earth-accretion', stage: 'accretion', years: 45e6, seconds: 7, progress: 0 },
        { name: 'theia-approach', stage: 'impact', years: 60e6, seconds: 0.8, progress: 0.8 },
        { name: 'theia-giant-impact', stage: 'impact', years: 60e6, seconds: 3.2, progress: 3.2 },
        { name: 'theia-ejecta-curtain', stage: 'impact', years: 60e6, seconds: 5.5, progress: 5.5 },
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
        const screenshot = await page.screenshot({ path: testInfo.outputPath('f0-' + scene.name + '.png') });
        if (scene.name === 'proto-earth-accretion') {
            // Single-surface quality gate: central planet pixels must contain
            // natural material contrast, not a flat orange fill. Keep this
            // tolerant of SwiftShader/mobile differences; human art review
            // remains an explicit separate approval.
            const png = PNG.sync.read(screenshot);
            const reds = [];
            for (let y = Math.floor(png.height / 2) - 54; y <= Math.floor(png.height / 2) + 54; y += 5) {
                for (let x = Math.floor(png.width / 2) - 54; x <= Math.floor(png.width / 2) + 54; x += 5) {
                    reds.push(png.data[(y * png.width + x) * 4]);
                }
            }
            reds.sort((a, b) => a - b);
            const p10 = reds[Math.floor(reds.length * 0.10)];
            const p90 = reds[Math.floor(reds.length * 0.90)];
            expect(p90 - p10, 'proto-Earth surface must retain rock/melt contrast')
                .toBeGreaterThan(18);
        }
        if (scene.stage === 'impact' || scene.stage === 'moon-assembly') {
            // Frame-composition assertion, not merely a WebGL compile smoke test:
            // the proto-Earth material must still occupy the centre of the viewport.
            // Previously the full-screen triangle halved vUv, moving Earth
            // entirely into the upper-right corner while screenshots still passed.
            const { width, height, data } = PNG.sync.read(screenshot);
            const reds = [];
            for (const dx of [-5, 0, 5]) {
                for (const dy of [-5, 0, 5]) {
                    const x = Math.floor(width / 2) + dx;
                    const y = Math.floor(height / 2) + dy;
                    reds.push(data[(y * width + x) * 4]);
                }
            }
            const meanRed = reds.reduce((sum, value) => sum + value, 0) / reds.length;
            expect(meanRed, scene.name + ' must show visible proto-Earth material in frame centre')
                .toBeGreaterThan(18);
        }
    }
    // Inspect the true first post-impact thermal renderer, too. The formation
    // cinematic has its own shader, so without this check a handoff shader
    // regression could hide until the scene finishes on a player's phone.
    const handoff = await page.evaluate(async () => {
        const renderer = await import('/js/planetRenderer.js');
        const model = renderer.getEvolutionDebug();
        model.formation.complete = true;
        model.thermal.timeSeconds = (60e6 + 1) * 365.25 * 24 * 3600;
        model.postImpactStartSeconds = model.thermal.timeSeconds;
        model.postImpactWallTime = model.wallTime;
        renderer.setEvolutionClouds(model.showClouds);
        return { freshness: model.postImpactWallTime === model.wallTime, completed: model.formation.complete };
    });
    expect(handoff).toEqual({ freshness: true, completed: true });
    await page.waitForTimeout(1_200);
    const handoffGraphics = await page.evaluate(() => {
        const canvas = document.getElementById('planet-canvas');
        return { glError: canvas.getContext('webgl2').getError(), width: canvas.width };
    });
    expect(handoffGraphics.glError).toBe(0);
    expect(handoffGraphics.width).toBeGreaterThan(0);
    await page.screenshot({ path: testInfo.outputPath('f0-newborn-molten-handoff.png') });

    await expect(page.getByTestId('formation-hud')).toBeVisible();
    await expect(page.getByTestId('special-events-timeline')).toBeVisible();
    const hud = await page.getByTestId('formation-hud').boundingBox();
    const dock = await page.locator('.formation-controls').boundingBox();
    expect(Math.abs(hud.y - dock.y)).toBeLessThan(3);
    expect(errors).toEqual([]);
});
