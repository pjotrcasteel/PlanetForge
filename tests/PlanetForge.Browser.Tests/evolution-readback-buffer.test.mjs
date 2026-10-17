import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readEvolutionField } from '../../src/PlanetForge.Presentation.Web/wwwroot/js/evolution/planetEvolution.js';

function fakeEvolution() {
    let serial = 0;
    const gl = {
        FRAMEBUFFER: 1, COLOR_ATTACHMENT0: 100, RGBA: 200, FLOAT: 300,
        bindFramebuffer: () => {},
        readBuffer: () => {},
        readPixels(_x, _y, width, height, _format, _type, dest) {
            assert.equal(width, 1024);
            assert.equal(height, 512);
            dest[0] = ++serial;
            dest[1] = 1.25;
        },
    };
    return { gl, current: 0, targets: { state: [{ framebuffer: 11 }], derived: { framebuffer: 12 }, compose: { framebuffer: 13 } } };
}

test('ReadEvolutionField_DefaultSnapshots_AreIndependent', () => {
    const evo = fakeEvolution();
    const first = readEvolutionField(evo, 'derived');
    const second = readEvolutionField(evo, 'derived');
    assert.notStrictEqual(first.data, second.data);
    assert.equal(first.data[0], 1);
    assert.equal(second.data[0], 2);
    assert.equal(first.data[1], second.data[1]);
    assert.equal(first.data.byteLength, 8 * 1024 * 1024);
    assert.equal(evo.fieldReadbackScratch, undefined);
});

test('ReadEvolutionField_InternalScratch_ReusesBufferPerFieldWithoutCrossFieldAlias', () => {
    const evo = fakeEvolution();
    const a = readEvolutionField(evo, 'derived', true);
    const b = readEvolutionField(evo, 'derived', true);
    const c = readEvolutionField(evo, 'state0', true);
    assert.strictEqual(a.data, b.data);
    assert.notStrictEqual(a.data, c.data);
    assert.equal(a.data[0], 2);
    assert.equal(c.data[0], 3);
    assert.equal(evo.fieldReadbackScratch.size, 2);
    const publicSnapshot = readEvolutionField(evo, 'derived');
    assert.notStrictEqual(publicSnapshot.data, a.data);
    assert.equal(a.data[0], 2);
    assert.equal(publicSnapshot.data[0], 4);
});

test('ReadEvolutionField_RepeatedScratch_AlwaysOverwritesFreshPhysicalValues', () => {
    const evo = fakeEvolution();
    const snapshot = readEvolutionField(evo, 'state0', true);
    snapshot.data[0] = -99; // e.g. plate owner consolidation
    const fresh = readEvolutionField(evo, 'state0', true);
    assert.strictEqual(snapshot.data, fresh.data);
    assert.equal(fresh.data[0], 2);
});
