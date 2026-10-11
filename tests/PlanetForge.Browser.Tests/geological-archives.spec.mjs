import { expect, test } from '@playwright/test';

const regionAddress = 'G18:S24061984:F4:L0:X0:Y0';
const archiveKey = `planetforge/geology/v1/${regionAddress}/YBP:50000000`;

function archive(cut = 20, epoch = 50_000_000) {
    const width = 8;
    const cells = width * width;
    const snapshot = (iteration, height, exported) => ({
        SchemaVersion: 5,
        Seed: 24061984,
        RegionKey: regionAddress,
        Width: width,
        Height: width,
        CellSpacingMeters: 1_000,
        Iteration: iteration,
        ElevationMeters: Array(cells).fill(height),
        CumulativeErodedVolumeCubicMeters: exported,
        CumulativeDepositedVolumeCubicMeters: 0,
        CumulativeExportedVolumeCubicMeters: exported
    });
    const exported = cut * cells * 1_000_000;
    return JSON.stringify({
        FormatVersion: 1, Seed: 24061984, GenerationVersion: 18,
        Face: 4, Level: 0, X: 0, Y: 0,
        YearsBeforePresent: epoch, PlanetRadiusMeters: 6_371_000,
        Original: snapshot(0, 1_000, 0),
        Evolved: snapshot(1, 1_000 - cut, exported)
    });
}

async function importArchive(page, json) {
    await page.locator('#archive-file').setInputFiles({
        name: 'planetforge-region.json',
        mimeType: 'application/json',
        buffer: Buffer.from(json)
    });
    await expect(page.getByTestId('archive-preview')).toBeVisible();
}

test('GeologicalArchives_ImportValidateReloadExportAndExplicitReplace', async ({ page }) => {
    await page.goto('/?geologyArchive=1');
    await expect(page.getByRole('heading', { name: 'Geological archives' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Saved regions' })).toBeVisible();

    await importArchive(page, archive(20));
    await expect(page.getByTestId('archive-preview')).toContainText('50000000 years before present');
    await page.locator('#archive-save').click();
    await expect(page.getByTestId('archive-status')).toContainText('saved');
    await expect(page.locator('.archive-key')).toHaveCount(1);

    await page.reload();
    await page.locator('.archive-key').first().click();
    await expect(page.getByTestId('archive-preview')).toContainText('1280000000');
    const downloadPromise = page.waitForEvent('download');
    await page.locator('#archive-export').click();
    const downloaded = await downloadPromise;
    expect(downloaded.suggestedFilename()).toMatch(/PlanetForge-G18-S24061984.*\.json$/);

    await importArchive(page, archive(30));
    await expect(page.getByText('A local archive already exists', { exact: false })).toBeVisible();
    await expect(page.locator('#archive-save')).toBeDisabled();
    await page.locator('#archive-replace').check();
    await page.locator('#archive-save').click();
    await expect(page.getByTestId('archive-status')).toContainText('saved');
    await page.reload();
    await page.locator('.archive-key').first().click();
    await expect(page.getByTestId('archive-preview')).toContainText('1920000000');
});

test('GeologicalArchives_RejectsMalformedFileAndConcurrentOverwrite', async ({ page }) => {
    await page.goto('/?geologyArchive=1');
    await page.locator('#archive-file').setInputFiles({
        name: 'broken.json', mimeType: 'application/json', buffer: Buffer.from('not json')
    });
    await expect(page.getByTestId('archive-status')).toContainText('Invalid backup');
    await expect(page.getByTestId('archive-preview')).toHaveCount(0);
    await importArchive(page, archive(20));
    await page.locator('#archive-save').click();
    await importArchive(page, archive(30));
    await page.locator('#archive-replace').check();

    await page.evaluate(async ({ key, json }) => {
        const store = await import('./js/geologicalRegionStore.js');
        await store.saveArchive(key, json);
    }, { key: archiveKey, json: archive(55) });
    await page.locator('#archive-save').click();
    await expect(page.getByTestId('archive-status')).toContainText('Archive changed in another tab');

    const unchanged = await page.evaluate(async key => {
        const store = await import('./js/geologicalRegionStore.js');
        return await store.loadArchive(key);
    }, archiveKey);
    expect(unchanged).toBe(archive(55));
});

test('GeologicalArchives_PaginationAndMobileLayout', async ({ page }) => {
    await page.goto('/?geologyArchive=1');
    await page.evaluate(async () => {
        const store = await import('./js/geologicalRegionStore.js');
        for (let i = 0; i < 23; i++) {
            await store.saveArchive(`planetforge/geology/v1/G18:S123:F4:L5:X${i}:Y0/YBP:0`, '{}');
        }
    });
    await page.locator('#archive-refresh').click();
    await expect(page.locator('.archive-key')).toHaveCount(10);
    await expect(page.getByText('Page 1 of 3')).toBeVisible();
    await page.locator('#archive-next').click();
    await expect(page.getByText('Page 2 of 3')).toBeVisible();
    await page.locator('#archive-next').click();
    await expect(page.locator('.archive-key')).toHaveCount(3);

    const overflow = await page.evaluate(() => ({
        pixels: document.documentElement.scrollWidth - innerWidth,
        offenders: [...document.querySelectorAll('body *')]
            .filter(element => element.getBoundingClientRect().right > innerWidth + 2)
            .slice(0, 8)
            .map(element => ({ tag: element.tagName, className: String(element.className).slice(0, 80),
                right: Math.round(element.getBoundingClientRect().right) }))
    }));
    expect(overflow.pixels, `The archive inspector must fit on iPhone; overflowing elements: ${JSON.stringify(overflow.offenders)}`)
        .toBeLessThanOrEqual(2);
});

test('GeologicalArchives_DeletionIsScopedToOneEpoch', async ({ page }) => {
    await page.goto('/?geologyArchive=1');
    await page.evaluate(async ({ a, b, json }) => {
        const store = await import('./js/geologicalRegionStore.js');
        await store.saveArchive(a, json);
        await store.saveArchive(b, json.replace('"YearsBeforePresent":50000000', '"YearsBeforePresent":0'));
    }, { a: archiveKey, b: archiveKey.replace('50000000', '0'), json: archive(20) });
    await page.locator('#archive-refresh').click();
    await page.locator('.archive-key').filter({ hasText: 'YBP:50000000' }).click();
    await page.locator('#archive-delete-start').click();
    await page.locator('#archive-delete-confirm').click();
    await expect(page.locator('.archive-key')).toHaveCount(1);
    await expect(page.locator('.archive-key').first()).toContainText('YBP:0');
});
