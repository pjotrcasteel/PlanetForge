// Backward-compatible Node test entry point for the retained GitHub CI workflow.
// The old dimensionless prototype is not in the authoritative PlanetForge snapshot:
// exercise the snapshot's actual thermal and Earth-timeline models instead.
import assert from 'node:assert/strict';
import test from 'node:test';
import {
    PARAMETERS,
    SECONDS_PER_YEAR,
    crystalFraction,
    rheologicalTransitionKelvin,
    crustThicknessMeters,
    createThermalHistory,
    advanceThermalHistory,
    physicalRate,
} from '../../../src/PlanetForge.Presentation.Web/wwwroot/js/evolution/thermalHistory.js';
import {
    EARTH_START_MA,
    EARTH_LATEST_START_MA,
    earthAgeMa,
    earthStartMyr,
    earthLatestStartMyr,
} from '../../../src/PlanetForge.Presentation.Web/wwwroot/js/evolution/earthHistory.js';

test('ThermalHistory_InitialState_StartsAsMagmaOcean', () => {
    const state = createThermalHistory();
    assert.equal(state.regime, 'magma-ocean');
    assert.equal(state.timeSeconds, 0);
    assert.equal(state.potentialKelvin, PARAMETERS.initialPotentialKelvin);
    assert.equal(state.plateOnsetSeconds, null);
});

test('CrystalFraction_AtPhysicalBounds_RemainsClamped', () => {
    assert.equal(crystalFraction(PARAMETERS.liquidusPotentialKelvin), 0);
    assert.equal(crystalFraction(PARAMETERS.solidusPotentialKelvin), 1);
    assert.equal(crystalFraction(PARAMETERS.liquidusPotentialKelvin + 100), 0);
    assert.equal(crystalFraction(PARAMETERS.solidusPotentialKelvin - 100), 1);
    assert.equal(crystalFraction(rheologicalTransitionKelvin()), PARAMETERS.rheologicalTransition);
});

test('CrustGrowth_PositiveElapsedTime_IncreasesBySquareRoot', () => {
    assert.equal(crustThicknessMeters(-1), 0);
    const initial = crustThicknessMeters(SECONDS_PER_YEAR);
    assert.ok(initial > 0);
    assert.ok(Math.abs(crustThicknessMeters(4 * SECONDS_PER_YEAR) - 2 * initial) < 1e-8);
});

test('ThermalHistory_ShortFiniteStep_PreservesFiniteStateAndAdvancesClock', () => {
    const state = createThermalHistory();
    advanceThermalHistory(state, 1, 20000);
    assert.equal(state.timeSeconds, 1);
    assert.ok(Number.isFinite(state.potentialKelvin));
    assert.ok(Number.isFinite(state.heatFlux));
    assert.ok(state.potentialKelvin > 0);
    assert.ok(physicalRate(state.timeSeconds, 1) > 0);
});

test('EarthHistory_AgeMapping_MatchesGeologicalReferenceEpochs', () => {
    assert.equal(earthAgeMa(0), 4567);
    assert.equal(earthAgeMa(4567), 0);
    assert.equal(earthAgeMa(4700), 0);
    assert.equal(earthAgeMa(earthStartMyr()), EARTH_START_MA);
    assert.equal(earthAgeMa(earthLatestStartMyr()), EARTH_LATEST_START_MA);
});
