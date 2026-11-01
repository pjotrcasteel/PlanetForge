import { expect, test } from '@playwright/test';

async function openStableBlazorPage(page, path, readySelector) {
    const failures = [];
    page.on('pageerror', error => failures.push(error.message));
    page.on('console', message => {
        if (message.type() === 'error') failures.push(message.text());
    });

    // A temporary python HTTP server can briefly fail to serve a WebAssembly
    // asset after another heavy WebGL case releases the browser context.
    // Retry the entire bootstrap ONCE. A repeat failure is still fatal and
    // includes the captured asset/JS errors for diagnosis.
    for (let attempt = 0; attempt < 2; attempt++) {
        await page.goto(path, { waitUntil: 'domcontentloaded' });
        try {
            await expect(page.locator(readySelector)).toBeVisible({ timeout: 25_000 });
            return;
        } catch (error) {
            if (attempt === 1) {
                throw new Error(`Blazor failed both boot attempts at ${path}. Browser errors: ${JSON.stringify(failures)}. ${error.message}`);
            }
        }
    }
}

test('GeologyPreview_AuditedTimelineDrawsMeasuredGlobalMapsWithoutTouchingMainPlanet', async ({ page }, testInfo) => {
    test.setTimeout(240_000);
    await openStableBlazorPage(page, '/?geologyPreview=1', '.geology-preview-header h1');
    await expect(page.getByRole('heading', { name: 'Geological deformation preview' })).toBeVisible();
    await expect(page.getByTestId('geology-preview-status')).toContainText('Ready', { timeout: 90_000 });
    await expect(page.getByTestId('geology-preview-scale')).toContainText('Measured map range', { timeout: 60_000 });
    await page.waitForFunction(() => window.__planetForgeGeologyPreviewReady === true, null, { timeout: 60_000 });

    const metrics = page.getByTestId('geology-preview-metrics');
    await expect(metrics).toContainText('100%');
    await expect(metrics).toContainText('Maximum column displacement');
    await expect(page.getByTestId('geology-preview-scale')).toContainText('Measured map range');
    const summary = await page.evaluate(() => {
        const result = window.__planetForgeGeologyPreviewStats;
        const pixels = [...document.querySelectorAll('.geology-preview-maps canvas')].map(canvas => {
            const ctx = canvas.getContext('2d', { willReadFrequently: true });
            const image = ctx.getImageData(0, 0, canvas.width, canvas.height);
            const first = image.data.slice(0, 4).join(',');
            let differences = 0;
            for (let i = 0; i < image.data.length; i += 256) {
                if (image.data.slice(i, i + 4).join(',') !== first) differences++;
            }
            return { width: canvas.width, height: canvas.height, differences };
        });
        return { result, pixels };
    });
    expect(Number.isFinite(summary.result.maxAbsUpliftMeters)).toBe(true);
    expect(summary.result.maxAbsUpliftMeters).toBeGreaterThan(0);
    expect(summary.result.maxAbsThicknessMeters).toBeGreaterThan(0);
    expect(summary.pixels).toHaveLength(2);
    for (const canvas of summary.pixels) {
        expect(canvas.width).toBe(256);
        expect(canvas.height).toBe(128);
        expect(canvas.differences).toBeGreaterThan(10);
    }

    await page.screenshot({ path: testInfo.outputPath('geology-deformation-present.png'), fullPage: true });

    // Oldest epoch is exactly the undeformed reference state.
    await page.locator('#geology-epoch').selectOption('0');
    await expect(page.getByTestId('geology-preview-metrics')).toContainText('0 m');
    await page.screenshot({ path: testInfo.outputPath('geology-deformation-reference.png'), fullPage: true });
    await page.locator('#geology-epoch').selectOption('2');
    await expect(page.getByTestId('geology-preview-status')).toContainText('Ready');
    await expect(page.getByTestId('geology-preview-scale')).toContainText('Measured map range');

    await openStableBlazorPage(page, '/', '#planet-canvas');
    await expect(page.getByTestId('formation-phase')).toHaveText('Proto-Earth · gravitational accretion', { timeout: 60_000 });
    // The new formation prologue advances continuously; the HUD reports physical
    // duration and playback rate, not the old stopped T+00:00:00 clock.
    await expect(page.getByTestId('formation-time')).toHaveText(/^\d+(?:\.\d+)? (?:s|min|h|days|yr|kyr|Myr|Gyr) · .+/);
});

test('GeologyPreview_MobileLayout_FitsIPhoneWithoutHorizontalOverflow', async ({ page }) => {
    test.setTimeout(240_000);
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/?geologyPreview=1');
    await expect(page.getByTestId('geology-preview-status')).toContainText('Ready', { timeout: 90_000 });
    await expect(page.locator('#geology-uplift')).toBeVisible();
    const overflow = await page.evaluate(() => ({
        pixels: document.documentElement.scrollWidth - innerWidth,
        offenders: [...document.querySelectorAll('body *')]
            .filter(element => element.getBoundingClientRect().right > innerWidth + 2)
            .slice(0, 8)
            .map(element => ({ tag: element.tagName, className: String(element.className).slice(0, 80),
                right: Math.round(element.getBoundingClientRect().right) }))
    }));
    expect(overflow.pixels, JSON.stringify(overflow.offenders)).toBeLessThanOrEqual(2);
    await openStableBlazorPage(page, '/', '#planet-canvas');
});
