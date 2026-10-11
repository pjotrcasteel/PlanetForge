import { expect, test } from '@playwright/test';

test('GeologyPreview_AuditedTimelineDrawsMeasuredGlobalMapsWithoutTouchingMainPlanet', async ({ page }) => {
    test.setTimeout(240_000);
    await page.goto('/?geologyPreview=1');
    await expect(page.getByRole('heading', { name: 'Geological deformation preview' })).toBeVisible();
    await expect(page.getByTestId('geology-preview-status')).toContainText('Ready', { timeout: 180_000 });
    await page.waitForFunction(() => window.__planetForgeGeologyPreviewReady === true, { timeout: 90_000 });

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

    // Oldest epoch is exactly the undeformed reference state.
    await page.locator('#geology-epoch').selectOption('0');
    await expect(page.getByTestId('geology-preview-metrics')).toContainText('0 m');
    await page.locator('#geology-epoch').selectOption('2');
    await expect(page.getByTestId('geology-preview-status')).toContainText('Ready');
    await expect(page.getByTestId('geology-preview-scale')).toContainText('Measured map range');

    await page.goto('/');
    await expect(page.locator('#planet-canvas')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Open geological deformation preview' })).toBeVisible();
});

test('GeologyPreview_MobileLayout_FitsIPhoneWithoutHorizontalOverflow', async ({ page }) => {
    test.setTimeout(240_000);
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/?geologyPreview=1');
    await expect(page.getByTestId('geology-preview-status')).toContainText('Ready', { timeout: 180_000 });
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
    await page.getByRole('link', { name: '← Planet' }).click();
    await expect(page.locator('#planet-canvas')).toBeVisible();
});
