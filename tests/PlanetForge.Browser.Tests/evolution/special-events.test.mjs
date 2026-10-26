import assert from 'node:assert/strict';
import test from 'node:test';
import { captureImpactEvents, orbitForEvent, advanceRealtimeSeconds } from
    '../../../src/PlanetForge.Presentation.Web/wwwroot/js/evolution/specialEvents.mjs';

test('Witnessed impact bookmarks follow actual seeded model state, deduplicate and remain bounded', () => {
    const first = { direction: [0, 0, 1], born: 2.5, seed: 4 };
    const recorded = captureImpactEvents([], [first], 9000, 24061984);
    assert.equal(recorded.length, 1);
    assert.equal(recorded[0].planetSeconds, 9000);
    assert.deepEqual(recorded[0].direction, [0, 0, 1]);
    assert.deepEqual(captureImpactEvents(recorded, [first], 9010, 24061984), recorded);

    let events = recorded;
    for (let i = 0; i < 12; i++)
        events = captureImpactEvents(events, [{ direction: [1, 1, 1], born: 5 + i, seed: i }], 9100 + i, 24061984);
    assert.equal(events.length, 8);
    assert.equal(events.at(-1).planetSeconds, 9111);
    assert.ok(!events.some(event => event.id === recorded[0].id));
});

test('Close-up camera follows physical event direction without changing parent epoch', () => {
    const north = orbitForEvent([0, 1, 0]);
    assert.ok(north.pitch > 1.3);
    assert.equal(north.distance, 1.125);
    const equator = orbitForEvent([1, 0, 0], 1.2);
    assert.ok(Math.abs(equator.yaw - Math.PI / 2) < 1e-10);
    assert.equal(equator.pitch, 0);
    assert.throws(() => orbitForEvent([0, 0, 0]), RangeError);
    assert.throws(() => orbitForEvent([NaN, 0, 1]), TypeError);
});

test('Real-time event clock advances one second per wall second without catch-up on hidden tabs', () => {
    assert.equal(advanceRealtimeSeconds(0, 0.05), 0.05);
    assert.equal(advanceRealtimeSeconds(7, 0.1), 7.1);
    assert.equal(advanceRealtimeSeconds(7, 0.5), 7.5);
    assert.equal(advanceRealtimeSeconds(7, 120), 8);
    assert.throws(() => advanceRealtimeSeconds(0, -1), RangeError);
    assert.throws(() => advanceRealtimeSeconds(Infinity, 1), RangeError);
});
