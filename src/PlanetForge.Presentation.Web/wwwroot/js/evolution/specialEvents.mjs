// M0: event handoff contract for the EXISTING Earth-history globe.
// Geological time is frozen during an inspection; elapsed event seconds are
// a separate wall-time clock. Never add these seconds to the 4.567 Gyr epoch.
const MAX_EVENTS = 8;

// The main globe remains at the event for 11 seconds, with smooth approach
// and retreat, before geological playback resumes automatically.
export const AUTOMATIC_EVENT_FOCUS_SECONDS = 14;

export function eventFocusCameraBlend(elapsedSeconds) {
    if (!Number.isFinite(elapsedSeconds) || elapsedSeconds < 0)
        throw new RangeError('Invalid automatic event focus time.');
    const smoothstep = t => t * t * (3 - 2 * t);
    const arriving = smoothstep(Math.min(1, elapsedSeconds / 1.2));
    const departing = smoothstep(Math.max(0, Math.min(1,
        (AUTOMATIC_EVENT_FOCUS_SECONDS - elapsedSeconds) / 2)));
    return arriving * departing;
}

// Legacy diagnostic only: these ordinary illustrative impact records must never
// be fed into the curated E7 timeline or trigger automatic camera focus.
export function captureImpactEvents(history, impacts, planetSeconds, seed) {
    if (!Array.isArray(history) || !Array.isArray(impacts) || !Number.isFinite(planetSeconds))
        throw new TypeError('Special-event capture requires a valid epoch and impact collection.');
    const next = history.slice(-MAX_EVENTS);
    const ids = new Set(next.map(event => event.id));
    for (const impact of impacts) {
        if (!Number.isFinite(impact.born) || !Number.isFinite(impact.seed) ||
            !Array.isArray(impact.direction) || impact.direction.length !== 3 ||
            impact.direction.some(value => !Number.isFinite(value)))
            continue;
        // Impact events come from the live seeded impact model, not a keyed
        // timeline or a cosmetic bookmark at a predetermined geological date.
        const id = `impact-${seed}-${impact.born.toFixed(6)}-${impact.seed.toFixed(6)}`;
        if (ids.has(id)) continue;
        const length = Math.hypot(...impact.direction);
        if (!(length > 0)) continue;
        next.push({
            id,
            title: 'Magma-ocean impact',
            type: 'magma-impact',
            planetSeconds,
            direction: impact.direction.map(value => value / length),
            born: impact.born, impactSeed: impact.seed,
            limitations: 'Illustrative, viewer-biased impact flux; no resolved crater chemistry or surface-scale physics.'
        });
        ids.add(id);
        if (next.length > MAX_EVENTS) {
            ids.delete(next[0].id);
            next.shift();
        }
    }
    return next;
}

// E7-B: only transitions that the live S0 model can actually witness.
// The catalogue is immutable. Historical reconstructions and hypotheses belong
// in the roadmap until they have vetted evidence and a compatible scene/state.
const MAX_KEY_EVENTS = 64;
export const EARTH_EVENT_CATALOG = Object.freeze([
    Object.freeze({
        key: 'solid-lid',
        title: 'Persistent rocky lid forms',
        authority: 'model-observed',
        trigger: 'lithosphereReady changes from false to true',
        scope: 'global',
        scale: 'S0 planet-wide thermal state',
        priority: 3,
        source: 'Model-state threshold · S0 lithosphere readiness',
        uncertainty: 'Model integration-step interval only. Does not date Earth’s first crust or locate a specific site.',
        limitations: 'Global S0 solidification threshold, not an observed first crust location. No regional mesh or surface chemistry is resolved.'
    }),
    Object.freeze({
        key: 'ocean-rainout',
        title: 'Steam rains out into oceans',
        authority: 'model-observed',
        trigger: 'model water inventory crosses 50% from below',
        scope: 'global',
        scale: 'S0 global water inventory',
        priority: 3,
        source: 'Model-state threshold · S0 ocean condensation',
        uncertainty: 'Model integration-step interval only. The 50% threshold is not a historical age for first liquid water.',
        limitations: 'The model ocean inventory crossed 50%. This is a model threshold, not a dated claim about Earth\'s very first water.'
    }),
    Object.freeze({
        key: 'plate-onset',
        title: 'Plate tectonics begins (model)',
        authority: 'model-observed',
        trigger: 'S0 thermal regime changes into plates',
        scope: 'global',
        scale: 'S0 planet-wide thermal regime',
        priority: 2,
        source: 'Model-state threshold · S0 plate regime',
        uncertainty: 'Model integration-step interval only. The onset of modern-style Earth plate tectonics remains debated.',
        limitations: 'S0 starts several synthetic plates together. The highlighted boundary belongs to this simulation, not a historically located first plate; the real onset of modern-style tectonics remains debated.'
    })
]);

export function snapshotEarthMilestoneState(evolution) {
    if (!evolution || !evolution.thermal ||
        !Number.isFinite(evolution.water) || typeof evolution.thermal.regime !== 'string')
        throw new TypeError('A milestone requires a valid Earth state.');
    return {
        lithosphereReady: evolution.lithosphereReady === true,
        water: evolution.water,
        regime: evolution.thermal.regime,
    };
}

export function captureEarthMilestoneEvents(history, before, after, planetSeconds, seed,
    stepStartSeconds = planetSeconds) {
    if (!Array.isArray(history) || !before || !after ||
        !Number.isFinite(planetSeconds) || !Number.isFinite(seed) ||
        !Number.isFinite(stepStartSeconds) || stepStartSeconds < 0 ||
        stepStartSeconds > planetSeconds || !Number.isFinite(before.water) ||
        !Number.isFinite(after.water) ||
        typeof before.regime !== 'string' || typeof after.regime !== 'string')
        throw new TypeError('Key moments require valid planetary state transitions and a time interval.');
    const next = history.slice(-MAX_KEY_EVENTS);
    const seen = new Set(next.map(event => event.id));
    const crossed = {
        'solid-lid': !before.lithosphereReady && after.lithosphereReady,
        'ocean-rainout': before.water < 0.5 && after.water >= 0.5,
        'plate-onset': before.regime !== 'plates' && after.regime === 'plates'
    };
    for (const milestone of EARTH_EVENT_CATALOG) {
        if (!crossed[milestone.key]) continue;
        const id = `earth-${seed}-${milestone.key}`;
        if (seen.has(id)) continue;
        next.push({
            ...milestone,
            id,
            type: 'earth-milestone',
            interval: { startSeconds: stepStartSeconds, endSeconds: planetSeconds },
            planetSeconds
        });
        seen.add(id);
        if (next.length > MAX_KEY_EVENTS) {
            seen.delete(next[0].id);
            next.shift();
        }
    }
    return next;
}

// At the global mobile-lid transition S0 partitions its actual GPU surface
// into plates. Select a boundary on the first seeded plate using the ownership
// map read back by physics during consolidation; never invent a geographic
// target or use the incidental camera direction. Nearby thick crust biases the
// camera toward more legible terrain, but an oceanic boundary is still valid.
export function selectFirstPlateFocus({ width, height, data } = {}) {
    if (!Number.isInteger(width) || width < 16 || !Number.isInteger(height) ||
        height < 16 || !data || data.length !== width * height * 4)
        return null;
    const step = Math.max(1, Math.floor(width / 256));
    let best = null;
    for (let y = step; y < height - step; y += step) {
        const latitude = (y + 0.5) / height - 0.5;
        // Avoid the poles: a nearly tangential camera hides the boundary.
        if (Math.abs(latitude) > 0.43) continue;
        for (let x = 0; x < width; x += step) {
            const i = y * width + x;
            if (Math.round(data[i * 4]) !== 0) continue;
            const neighbours = [
                y * width + (x + step) % width,
                y * width + (x + width - step) % width,
                Math.min(height - 1, y + step) * width + x,
                Math.max(0, y - step) * width + x
            ];
            for (const n of neighbours) {
                const adjacentPlateId = Math.round(data[n * 4]);
                if (adjacentPlateId < 1) continue;
                const crustKm = Math.max(0, Number(data[i * 4 + 2]) || 0,
                    Number(data[n * 4 + 2]) || 0);
                const score = Math.min(crustKm, 30) * 0.09
                    + (1 - Math.abs(latitude) * 2) * 1.1;
                if (best && score <= best.score) continue;
                const longitude = ((x + 0.5) / width - 0.5) * 2 * Math.PI;
                const lat = latitude * Math.PI;
                best = {
                    direction: [Math.cos(lat) * Math.cos(longitude),
                        Math.sin(lat), Math.cos(lat) * Math.sin(longitude)],
                    plateId: 0, adjacentPlateId, gridX: x, gridY: y, score
                };
            }
        }
    }
    return best && {
        direction: best.direction, plateId: best.plateId,
        adjacentPlateId: best.adjacentPlateId,
        gridX: best.gridX, gridY: best.gridY,
        basis: 'S0 GPU plate-ownership boundary at regime onset'
    };
}

export function orbitForEvent(direction, distance = 1.125) {
    if (!Array.isArray(direction) || direction.length !== 3 || direction.some(x => !Number.isFinite(x)))
        throw new TypeError('An event requires a finite geographic direction.');
    const norm = Math.hypot(...direction);
    if (norm <= 0 || !Number.isFinite(distance) || distance < 1.12 || distance > 10.5)
        throw new RangeError('Invalid event direction or orbital close-up distance.');
    const [x, y, z] = direction.map(v => v / norm);
    return { yaw: Math.atan2(x, z), pitch: Math.asin(Math.max(-0.98, Math.min(0.98, y))), distance };
}

export function advanceRealtimeSeconds(elapsedSeconds, frameSeconds) {
    if (!Number.isFinite(elapsedSeconds) || elapsedSeconds < 0 ||
        !Number.isFinite(frameSeconds) || frameSeconds < 0)
        throw new RangeError('Invalid real-time event interval.');
    // Caller excludes hidden-tab time using visibilitychange; frame delays while
    // the tab remains visible must not slow the promised 1x event clock.
    return elapsedSeconds + frameSeconds;
}
