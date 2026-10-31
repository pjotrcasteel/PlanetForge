import assert from 'node:assert/strict';
import test from 'node:test';
import { locatePlanetTexel, captureRegionalHandoff } from
    '../../../src/PlanetForge.Presentation.Web/wwwroot/js/evolution/regionalHandoff.mjs';

function field(width = 12, height = 6) {
    const data = new Float32Array(width * height * 4);
    for (let y = 0; y < height; y++)
        for (let x = 0; x < width; x++) {
            const offset = (y * width + x) * 4;
            data.set([y * 100 + x, 0.25, 0.5, 0.75], offset);
        }
    return { width, height, data };
}

const options = {
    seed: 24061984, epochSeconds: 1_000_000,
    planetRadiusMeters: 6_371_000, direction: [1, 0, 0],
    seaLevelKilometers: 100
};

test('S0 globe coordinates address the exact equirectangular parent cell', () => {
    assert.deepEqual([locatePlanetTexel([1, 0, 0], 12, 6).column,
        locatePlanetTexel([1, 0, 0], 12, 6).row], [6, 3]);
    assert.deepEqual([locatePlanetTexel([0, 1, 0], 12, 6).column,
        locatePlanetTexel([0, 1, 0], 12, 6).row], [6, 5]);
    assert.equal(locatePlanetTexel([-1, 0, 0], 12, 6).column, 0);
    assert.throws(() => locatePlanetTexel([0, 0, 0], 12, 6), TypeError);
    assert.throws(() => locatePlanetTexel([NaN, 0, 1], 12, 6), TypeError);
});

test('A regional sample is a copy of the physical GPU grid, frozen at parent time and seed', () => {
    const parent = field();
    const prior = parent.data.slice();
    const region = captureRegionalHandoff({ ...options, field: parent });
    assert.equal(region.epochSeconds, options.epochSeconds);
    assert.equal(region.seed, options.seed);
    assert.equal(region.sourceWidth, 12);
    assert.equal(region.samples.length, 5);
    assert.equal(region.samples[0].length, 5);
    assert.equal(region.samples[2][2].elevationMeters, 306_000);
    assert.equal(region.samples[2][2].heightAboveSeaLevelMeters, 206_000);
    assert.equal(region.samples[2][2].incandescence, 0.25);
    assert.ok(region.cellResolutionMeters.northSouth > 0);
    assert.ok(region.cellResolutionMeters.eastWest > 0);
    assert.ok(Object.isFrozen(region));
    assert.ok(Object.isFrozen(region.samples));
    assert.ok(Object.isFrozen(region.samples[2][2]));
    assert.deepEqual(parent.data, prior);
    parent.data[(3 * 12 + 6) * 4] = -999;
    assert.equal(region.samples[2][2].elevationMeters, 306_000,
        'The parent GPU scratch readback must never mutate an in-progress close-up.');
});

test('The region remains geographically complete across the longitude seam and at the poles', () => {
    const parent = field();
    const antimeridian = captureRegionalHandoff({ ...options, direction: [-1, 0, 0], field: parent });
    assert.deepEqual(antimeridian.samples[2].map(cell => cell.column), [10, 11, 0, 1, 2]);
    const northPole = captureRegionalHandoff({ ...options, direction: [0, 1, 0], field: parent });
    assert.deepEqual(northPole.samples.map(row => row[2].row), [3, 4, 5, 5, 5]);
    assert.ok(northPole.cellResolutionMeters.eastWest < 0.000001);
});

test('The source cannot silently invent finer pixels, chemistry or nonfinite elevation', () => {
    const parent = field();
    assert.throws(() => captureRegionalHandoff({ ...options, field: parent, footprint: 20 }), RangeError);
    assert.throws(() => captureRegionalHandoff({ ...options, field: parent, epochSeconds: NaN }), RangeError);
    assert.throws(() => captureRegionalHandoff({ ...options, field: { ...parent, data: [] } }), TypeError);
    parent.data[0] = Infinity;
    assert.throws(() => captureRegionalHandoff({ ...options, direction: [-1, -1, 0], field: parent }), RangeError);
});
