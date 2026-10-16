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

test('Playback_LongFrame_IsBoundedToThirtySeconds', () => {
    const delayed = playback(1, 40);
    const recovered = createThermalHistory();
    advancePlaybackThermalHistory(recovered, 1000, spacingMeters, 50e6);
    assert.ok(Math.abs(recovered.timeSeconds - playback(0.1, 30).timeSeconds) < 1e-5);
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

// Software WebGL may only produce one frame every 5-10 seconds. Full foreground
// elapsed time must be integrated: the magma ocean must not freeze in place.
test('Playback_LowFrameRate_AdvancesThroughLidAndPlateRegimes', () => {
    const regular = playback(0.1, 90);
    const slow = playback(5, 90);
    assert.equal(regular.regime, 'plates');
    assert.equal(slow.regime, 'plates');
    assert.ok(Math.abs(slow.timeSeconds - regular.timeSeconds) / regular.timeSeconds < 1e-5);
});

test('Playback_LongSuspension_DoesNotLeapThousandsOfYearsInOneFrame', () => {
    const recovered = createThermalHistory();
    advancePlaybackThermalHistory(recovered, 1000, spacingMeters, 50e6);
    const thirtySeconds = playback(0.1, 30);
    assert.ok(Math.abs(recovered.timeSeconds - thirtySeconds.timeSeconds) / thirtySeconds.timeSeconds < 1e-5);
});
