import { test } from 'node:test';
import assert from 'node:assert/strict';
import { FormationDemoWorld } from '../../../src/PlanetForge.Presentation.Web/wwwroot/js/formationDemoModel.mjs';
test('Seeded replay conserves lava, crust and finite reservoir through cooling and flow', () => {
    const a = new FormationDemoWorld(24061984, 64, 32), b = new FormationDemoWorld(24061984, 64, 32);
    for (const seconds of [5, 10, 15, 30, 60, 60]) {
        a.advance(seconds); b.advance(seconds);
        assert.deepEqual(a.lava, b.lava); assert.deepEqual(a.crust, b.crust);
        assert.ok(Math.abs(a.inventory() - a.initialInventory) < a.initialInventory * 1e-12);
        for (const name of ['lava', 'crust', 'reservoir', 'heat', 'uplift']) assert.ok(a[name].every(x => Number.isFinite(x) && x >= 0));
    }
    assert.ok(a.crust.some(x => x > .18)); assert.ok(a.lava.some(x => x > .1));
    assert.ok(a.uplift.some(x => x > 0)); assert.ok(a.reservoir.some(x => x < 2));
});
test('Playback batches and one equal-duration request evolve the same world', () => {
    const a = new FormationDemoWorld(42, 32, 16), b = new FormationDemoWorld(42, 32, 16);
    a.advance(30); for (let i = 0; i < 6; i++) b.advance(5);
    assert.deepEqual(a.crust, b.crust); assert.deepEqual(a.lava, b.lava);
    assert.equal(a.time, 30); assert.equal(b.time, 30);
});
test('Seed changes initial geometry and invalid steps fail before mutation', () => {
    const a = new FormationDemoWorld(1, 32, 16), b = new FormationDemoWorld(2, 32, 16);
    assert.notDeepEqual(a.bed, b.bed);
    for (const dt of [-1, NaN, Infinity, 301]) assert.throws(() => a.advance(dt), RangeError);
    assert.equal(a.time, 0); assert.equal(a.inventory(), a.initialInventory);
});
