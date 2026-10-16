import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createThermalHistory, advancePlaybackThermalHistory } from '../../src/PlanetForge.Presentation.Web/wwwroot/js/evolution/thermalHistory.js';

const spacingMeters = 40_000;

function playback(frameSeconds, wallSeconds, yearsPerSecond = 50e6) {
    const state = createThermalHistory();
    for (let elapsed = 0; elapsed < wallSeconds - 1e-9; elapsed += frameSeconds) {
        advancePlaybackThermalHistory(state, Math.min(frameSeconds, wallSeconds - elapsed), spacingMeters, yearsPerSecond);
    }
    return state;
}

test('Playback_SlowAndFastFrames_ReachSameThermalRegimeWithoutDiscardingWallTime', () => {
    const slow = playback(0.5, 44);
    const fast = playback(0.1, 44);
    assert.equal(slow.regime, 'stagnant-lid');
    assert.equal(fast.regime, 'stagnant-lid');
    assert.ok(Math.abs(slow.timeSeconds - fast.timeSeconds) / fast.timeSeconds < 1e-5);
    assert.ok(Math.abs(slow.potentialKelvin - fast.potentialKelvin) < 0.05);
});

test('Playback_BackgroundRecovery_IsBoundedToOneSecond', () => {
    const delayed = playback(1, 10);
    const recovered = createThermalHistory();
    advancePlaybackThermalHistory(recovered, 1000, spacingMeters, 50e6);
    assert.ok(Math.abs(recovered.timeSeconds - playback(0.1, 1).timeSeconds) < 1e-5);
    assert.ok(delayed.timeSeconds > recovered.timeSeconds);
});

test('Playback_PauseOrInvalidDuration_CannotAdvanceThermalState', () => {
    const state = createThermalHistory();
    advancePlaybackThermalHistory(state, 0, spacingMeters, 50e6);
    assert.equal(state.timeSeconds, 0);
    assert.throws(() => advancePlaybackThermalHistory(state, -1, spacingMeters, 50e6), RangeError);
    assert.throws(() => advancePlaybackThermalHistory(state, NaN, spacingMeters, 50e6), RangeError);
    assert.equal(state.timeSeconds, 0);
});
