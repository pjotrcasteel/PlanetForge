// M0: event handoff contract for the EXISTING Earth-history globe.
// Geological time is frozen during an inspection; elapsed event seconds are
// a separate wall-time clock. Never add these seconds to the 4.567 Gyr epoch.
const MAX_EVENTS = 8;

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
    // A suspended/background tab never fast-forwards the close-up.
    return elapsedSeconds + Math.min(frameSeconds, 0.1);
}
