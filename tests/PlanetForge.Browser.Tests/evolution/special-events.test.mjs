import assert from 'node:assert/strict';
import test from 'node:test';
import { captureImpactEvents, captureEarthMilestoneEvents, snapshotEarthMilestoneState, EARTH_EVENT_CATALOG, orbitForEvent, advanceRealtimeSeconds, eventFocusCameraBlend, AUTOMATIC_EVENT_FOCUS_SECONDS } from
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
    assert.equal(advanceRealtimeSeconds(7, 120), 127);
    assert.throws(() => advanceRealtimeSeconds(0, -1), RangeError);
    assert.throws(() => advanceRealtimeSeconds(Infinity, 1), RangeError);
});

test('Automatic zoom arrives, briefly holds, then returns to the original globe', () => {
    assert.equal(AUTOMATIC_EVENT_FOCUS_SECONDS, 14);
    assert.equal(eventFocusCameraBlend(0), 0);
    assert.ok(eventFocusCameraBlend(0.6) > 0);
    assert.equal(eventFocusCameraBlend(1.2), 1);
    assert.equal(eventFocusCameraBlend(11), 1);
    assert.ok(eventFocusCameraBlend(13) > 0 && eventFocusCameraBlend(13) < 1);
    assert.equal(eventFocusCameraBlend(14), 0);
    assert.equal(eventFocusCameraBlend(30), 0);
    assert.throws(() => eventFocusCameraBlend(-1), RangeError);
});

test('Ordinary Hadean impacts are background activity, never key-moment transitions', () => {
    const initial = { lithosphereReady: false, water: 0, regime: 'magma-ocean' };
    assert.deepEqual(captureEarthMilestoneEvents([], initial, initial, 5000, 42), []);
    // An actual impact entering S0's optical buffer changes none of these
    // physical milestones and therefore cannot cause an automatic zoom.
    const model = { thermal: { regime: 'magma-ocean' }, lithosphereReady: false,
        water: 0, magmaImpacts: [{ born: 5, seed: 3, direction: [0, 1, 0] }] };
    assert.deepEqual(snapshotEarthMilestoneState(model), initial);
    assert.deepEqual(captureEarthMilestoneEvents([], initial,
        snapshotEarthMilestoneState(model), 7000, 42), []);
});

test('Only once-per-run modeled planetary thresholds create major timeline markers', () => {
    const magma = { lithosphereReady: false, water: 0, regime: 'magma-ocean' };
    const lid = { lithosphereReady: true, water: 0, regime: 'stagnant-lid' };
    const ocean = { ...lid, water: 0.52 };
    const plates = { ...ocean, water: 1, regime: 'plates' };
    let events = captureEarthMilestoneEvents([], magma, lid, 1e12, 42);
    assert.equal(events.length, 1);
    assert.equal(events[0].title, 'Persistent rocky lid forms');
    assert.equal(events[0].scope, 'global');
    assert.match(events[0].source, /^Model-state threshold/);
    assert.equal(events[0].authority, 'model-observed');
    assert.equal(events[0].scale, 'S0 planet-wide thermal state');
    assert.equal(events[0].priority, 3);
    assert.deepEqual(events[0].interval, { startSeconds: 1e12, endSeconds: 1e12 });
    assert.match(events[0].uncertainty, /not date Earth/);

    events = captureEarthMilestoneEvents(events, lid, ocean, 2e12, 42);
    assert.equal(events.length, 2);
    assert.equal(events[1].title, 'Steam rains out into oceans');

    events = captureEarthMilestoneEvents(events, ocean, plates, 3e12, 42);
    assert.equal(events.length, 3);
    assert.equal(events[2].title, 'Plate tectonics begins (model)');
    assert.deepEqual(captureEarthMilestoneEvents(events, plates, plates, 4e12, 42), events);
    assert.deepEqual(captureEarthMilestoneEvents([], magma, magma, 0, 42), []);
    assert.throws(() => snapshotEarthMilestoneState({ water: NaN, thermal: { regime: 'plates' } }), TypeError);
    assert.throws(() => captureEarthMilestoneEvents([], magma, { ...ocean, water: NaN }, 1e12, 42), TypeError);
});

test('E7-B catalog is immutable, bounded to live observed states, and honestly reports uncertainty', () => {
    assert.equal(EARTH_EVENT_CATALOG.length, 3);
    assert.ok(Object.isFrozen(EARTH_EVENT_CATALOG));
    for (const entry of EARTH_EVENT_CATALOG) {
        assert.ok(Object.isFrozen(entry));
        assert.equal(entry.authority, 'model-observed');
        assert.equal(entry.scope, 'global');
        assert.ok(entry.priority >= 1);
        assert.ok(entry.trigger && entry.scale && entry.source && entry.uncertainty);
        assert.ok(!/first cell|giant impact|GOE|land animals/i.test(entry.title));
    }

    const before = { lithosphereReady: false, water: 0, regime: 'magma-ocean' };
    const after = { ...before, lithosphereReady: true };
    const events = captureEarthMilestoneEvents([], before, after, 5e12, 42, 4e12);
    assert.deepEqual(events[0].interval, { startSeconds: 4e12, endSeconds: 5e12 });
    assert.equal(events[0].planetSeconds, events[0].interval.endSeconds);
    assert.equal(events[0].authority, 'model-observed');
    assert.deepEqual(captureEarthMilestoneEvents(events, before, after, 6e12, 42, 5e12), events);
    assert.throws(() => captureEarthMilestoneEvents([], before, after, 5e12, 42, 6e12), TypeError);
    assert.throws(() => captureEarthMilestoneEvents([], before, after, 5e12, 42, -1), TypeError);

    // A populated (bounded) history may carry already-witnessed milestones.
    // The catalogue alone never creates a future event or decorative impact.
    assert.deepEqual(captureEarthMilestoneEvents([], after, after, 7e12, 42, 6e12), []);
    const prefilled = Array.from({ length: 70 }, (_, i) => ({ id: `existing-${i}` }));
    const bounded = captureEarthMilestoneEvents(prefilled, before, after, 5e12, 42, 4e12);
    assert.equal(bounded.length, 64);
    assert.equal(bounded.at(-1).title, 'Persistent rocky lid forms');
});
