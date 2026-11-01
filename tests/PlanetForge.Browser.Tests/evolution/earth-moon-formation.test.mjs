import assert from 'node:assert/strict';
import test from 'node:test';
import {
    createEarthMoonFormation, stepEarthMoonFormation, earthMoonFormationStatus,
    moonFormationImpactEvent, IMPACT_AGE_YEARS, HANDOFF_AGE_YEARS,
    IMPACT_SCENE_SECONDS, MOON_SCENE_SECONDS, AFTERMATH_SECONDS, SECONDS_PER_YEAR,
} from '../../../src/PlanetForge.Presentation.Web/wwwroot/js/evolution/earthMoonFormation.mjs';

test('F0 starts before a complete planet: Earth grows prior to the giant impact', () => {
    const formation = createEarthMoonFormation(1945);
    assert.equal(formation.ageYears, 0);
    assert.equal(formation.stage, 'accretion');
    assert.match(earthMoonFormationStatus(formation).phase, /Proto-Earth/);
    assert.equal(formation.complete, false);
    stepEarthMoonFormation(formation, 0.5, 20e6);
    assert.equal(formation.ageYears, 10e6);
    assert.equal(formation.stage, 'accretion');
});

test('F0 impact cannot be skipped by the fast control and is witnessed once', () => {
    const formation = createEarthMoonFormation(13);
    stepEarthMoonFormation(formation, 1, 50e6);
    assert.equal(formation.stage, 'accretion');
    stepEarthMoonFormation(formation, 1, 50e6);
    assert.equal(formation.stage, 'impact');
    assert.equal(formation.ageYears, IMPACT_AGE_YEARS);
    assert.equal(formation.impactJustBegun, true);
    formation.impactJustBegun = false; // renderer consumes the event
    stepEarthMoonFormation(formation, 1, 50e6);
    assert.equal(formation.stage, 'impact');
    assert.equal(formation.impactJustBegun, false);
    assert.equal(formation.ageYears, IMPACT_AGE_YEARS); // local collision, frozen geology
});

test('F0 collision, debris, and aftermath hand over at a monotonically increasing geological age', () => {
    const formation = createEarthMoonFormation(5);
    for (let i = 0; i < 12; i++) stepEarthMoonFormation(formation, 1, 5e6);
    assert.equal(formation.stage, 'impact');
    for (let i = 0; i < IMPACT_SCENE_SECONDS; i++) stepEarthMoonFormation(formation, 1, 5e6);
    assert.equal(formation.stage, 'moon-assembly');
    let previous = formation.ageYears;
    for (let i = 0; i < MOON_SCENE_SECONDS; i++) {
        stepEarthMoonFormation(formation, 1, 5e6);
        assert.ok(formation.ageYears >= previous);
        previous = formation.ageYears;
    }
    assert.equal(formation.stage, 'aftermath');
    assert.equal(formation.ageYears, HANDOFF_AGE_YEARS);
    for (let i = 0; i < AFTERMATH_SECONDS; i++) stepEarthMoonFormation(formation, 1, 5e6);
    assert.equal(formation.complete, true);
    assert.equal(earthMoonFormationStatus(formation).timeSeconds, HANDOFF_AGE_YEARS * SECONDS_PER_YEAR);
    assert.equal(stepEarthMoonFormation(formation, 1, 5e6), false);
});

test('F0 reconstructed milestone carries disputed chronology and is never labeled model-observed', () => {
    const event = moonFormationImpactEvent(13);
    assert.equal(event.authority, 'reconstruction');
    assert.equal(event.type, 'earth-milestone');
    assert.equal(event.planetSeconds, IMPACT_AGE_YEARS * SECONDS_PER_YEAR);
    assert.ok(event.interval.startSeconds < event.planetSeconds);
    assert.ok(event.interval.endSeconds > event.planetSeconds);
    assert.match(event.limitations, /not an impact solved/);
    assert.equal(moonFormationImpactEvent(13).id, event.id);
    assert.notEqual(moonFormationImpactEvent(14).id, event.id);
});

test('F0 deterministic reset and invalid clock input', () => {
    const original = createEarthMoonFormation(18);
    const repeated = createEarthMoonFormation(18);
    for (const time of [0.05, 0.1, 0.2, 0.3, 0.4]) {
        stepEarthMoonFormation(original, time, 10e6);
        stepEarthMoonFormation(repeated, time, 10e6);
    }
    assert.deepEqual(earthMoonFormationStatus(original), earthMoonFormationStatus(repeated));
    assert.throws(() => stepEarthMoonFormation(original, -1, 5e6), RangeError);
    assert.throws(() => stepEarthMoonFormation(original, 1, 0), RangeError);
    assert.throws(() => stepEarthMoonFormation(original, NaN, 5e6), RangeError);
});
