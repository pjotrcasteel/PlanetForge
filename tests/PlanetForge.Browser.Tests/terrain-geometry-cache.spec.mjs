import { expect, test } from '@playwright/test';

test('TerrainGeometryStore_LodDeltasKeepOverlaysComplete_AndRevisionsInvalidate', async ({ page }) => {
  await page.goto('/?terrainLab=1');
  const result = await page.evaluate(async () => {
    const store = await import('./js/surfaceGeometryStore.js');
    store.clearRetainedSurfaceGeometry();
    const tile = key => ({
      key,
      surfaceVertexCount: 1,
      positions: [1, 2, 3],
      normals: [0, 1, 0]
    });
    const reference = key => ({ key, surfaceVertexCount: 1, positions: [], normals: [] });
    const snapshot = (tiles, revision = 0) => ({
      seed: 42,
      terrainRevision: revision,
      physicalParameters: { radiusMeters: 6_371_000 },
      surfaceTiles: tiles
    });

    store.retainSurfaceGeometry(snapshot([tile('A'), tile('B')]));
    const first = store.getRetainedSurfaceGeometry().surfaceTiles.map(t => t.key);

    store.retainSurfaceGeometry(snapshot([reference('B'), tile('C')]));
    const second = store.getRetainedSurfaceGeometry().surfaceTiles;
    const overlayComplete = second.length === 2 && second.every(t => t.positions.length === 3);

    store.retainSurfaceGeometry(snapshot([reference('B'), reference('C')], 1));
    const afterRevision = store.getRetainedSurfaceGeometry().surfaceTiles.length;

    store.retainSurfaceGeometry(snapshot([tile('C')], 1));
    const refreshed = store.getRetainedSurfaceGeometry().surfaceTiles.map(t => t.key);
    store.clearRetainedSurfaceGeometry();
    return { first, second: second.map(t => t.key), overlayComplete, afterRevision, refreshed };
  });

  expect(result.first).toEqual(['A', 'B']);
  expect(result.second).toEqual(['B', 'C']);
  expect(result.overlayComplete).toBe(true);
  expect(result.afterRevision).toBe(0);
  expect(result.refreshed).toEqual(['C']);
});
