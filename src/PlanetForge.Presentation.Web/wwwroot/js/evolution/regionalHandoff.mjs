// S0 -> S1 handoff: immutable regional evidence from the ACTUAL GPU-derived
// elevation/material field, not a separately generated map. This captures the
// source-grid resolution; it must not be rendered as resolved sub-grid terrain.
const TWO_PI = 2 * Math.PI;
const MAX_FOOTPRINT = 7;

export function locatePlanetTexel(direction, width, height) {
    if (!Array.isArray(direction) || direction.length !== 3 ||
        direction.some(v => !Number.isFinite(v)) || Math.hypot(...direction) === 0)
        throw new TypeError('A finite, nonzero geocentric direction is required.');
    if (!Number.isSafeInteger(width) || width < 2 || !Number.isSafeInteger(height) || height < 2)
        throw new RangeError('The source field must have valid equirectangular dimensions.');

    const length = Math.hypot(...direction);
    const [x, y, z] = direction.map(v => v / length);
    const longitudeRadians = Math.atan2(z, x);
    const latitudeRadians = Math.asin(Math.max(-1, Math.min(1, y)));
    const u = (longitudeRadians / TWO_PI + 0.5) % 1;
    const v = latitudeRadians / Math.PI + 0.5;
    return Object.freeze({
        longitudeRadians, latitudeRadians,
        column: Math.min(width - 1, Math.floor(u * width)),
        row: Math.min(height - 1, Math.floor(v * height)),
        direction: Object.freeze([x, y, z])
    });
}

export function captureRegionalHandoff({
    field, direction, seed, epochSeconds, planetRadiusMeters, seaLevelKilometers = 0, footprint = 5
}) {
    if (!field || !Number.isSafeInteger(field.width) || !Number.isSafeInteger(field.height) ||
        !field.data || field.data.length !== field.width * field.height * 4)
        throw new TypeError('Regional handoff requires a complete RGBA parent-derived field.');
    if (!Number.isSafeInteger(footprint) || footprint < 3 ||
        footprint > MAX_FOOTPRINT || footprint % 2 === 0)
        throw new RangeError('The immutable regional footprint must be an odd 3, 5 or 7.');
    if (!Number.isSafeInteger(seed) || !Number.isFinite(epochSeconds) || epochSeconds < 0 ||
        !Number.isFinite(planetRadiusMeters) || planetRadiusMeters <= 0 ||
        !Number.isFinite(seaLevelKilometers))
        throw new RangeError('Parent seed, geological epoch and physical radius must be finite.');

    const { width, height, data } = field;
    const center = locatePlanetTexel(direction, width, height);
    const radius = (footprint - 1) / 2;
    const rows = [];
    for (let dy = -radius; dy <= radius; dy++) {
        const row = Math.max(0, Math.min(height - 1, center.row + dy));
        const samples = [];
        for (let dx = -radius; dx <= radius; dx++) {
            const column = (center.column + dx + width) % width;
            const start = (row * width + column) * 4;
            const elevationKilometers = data[start];
            const incandescence = data[start + 1];
            const felsicProxy = data[start + 2];
            const surfaceFreshness = data[start + 3];
            if (![elevationKilometers, incandescence, felsicProxy, surfaceFreshness].every(Number.isFinite))
                throw new RangeError('The parent source contains a non-finite sample.');
            samples.push(Object.freeze({
                column, row, elevationMeters: elevationKilometers * 1000,
                heightAboveSeaLevelMeters: (elevationKilometers - seaLevelKilometers) * 1000,
                incandescence, felsicProxy, surfaceFreshness
            }));
        }
        rows.push(Object.freeze(samples));
    }

    const latScaleMeters = Math.PI * planetRadiusMeters / height;
    const lonScaleMeters = TWO_PI * planetRadiusMeters *
        Math.max(0, Math.cos(center.latitudeRadians)) / width;
    return Object.freeze({
        seed, epochSeconds, planetRadiusMeters,
        source: 'S0 GPU-derived lithosphere atlas · direct samples',
        sourceWidth: width, sourceHeight: height,
        center, footprint, samples: Object.freeze(rows),
        cellResolutionMeters: Object.freeze({ northSouth: latScaleMeters, eastWest: lonScaleMeters }),
        limits: 'Parent resolution only. No resolved vents, river channels, local terrain, chemistry or 1x geodynamics.'
    });
}
