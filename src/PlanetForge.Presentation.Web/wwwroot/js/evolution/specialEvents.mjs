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

// E7: an intentionally small set of observable planetary turning points.
// Ordinary impacts continue in the GPU model; they are not automatically
// promoted to timeline markers or orbit changes.
const MAX_KEY_EVENTS = 64;

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

export function captureEarthMilestoneEvents(history, before, after, planetSeconds, seed) {
    if (!Array.isArray(history) || !before || !after ||
        !Number.isFinite(planetSeconds) || !Number.isFinite(seed) ||
        !Number.isFinite(before.water) || !Number.isFinite(after.water))
        throw new TypeError('Key moments require valid planetary state transitions.');
    const next = history.slice(-MAX_KEY_EVENTS);
    const seen = new Set(next.map(event => event.id));
    const milestones = [
        {
            key: 'solid-lid',
            title: 'Persistent rocky lid forms',
            crossed: !before.lithosphereReady && after.lithosphereReady,
            limitations: 'Global S0 solidification threshold, not an observed first crust location. No regional mesh or surface chemistry is resolved.'
        },
        {
            key: 'ocean-rainout',
            title: 'Steam rains out into oceans',
            crossed: before.water < 0.5 && after.water >= 0.5,
            limitations: 'The model ocean inventory crossed 50%. This is a model threshold, not a dated claim about Earth\'s very first water.'
        },
        {
            key: 'plate-onset',
            title: 'Plate tectonics begins (model)',
            crossed: before.regime !== 'plates' && after.regime === 'plates',
            limitations: 'Simplified S0 thermal regime transition. The real onset of modern-style plate tectonics is debated; this does not identify a local plate boundary.'
        }
    ];
    for (const milestone of milestones) {
        if (!milestone.crossed) continue;
        const id = `earth-${seed}-${milestone.key}`;
        if (seen.has(id)) continue;
        next.push({
            id, title: milestone.title, type: 'earth-milestone',
            scope: 'global', source: 'Model-state threshold',
            planetSeconds, limitations: milestone.limitations
        });
        seen.add(id);
        if (next.length > MAX_KEY_EVENTS) {
            seen.delete(next[0].id);
            next.shift();
        }
    }
    return next;
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
