import * as surface from './cryosphereSurface.js';
import * as ocean from './oceanDepthSurface.js';
import * as coast from './coastalReliefSurface.js';

let retainedSurfaceTiles = [];
let retainedPhysicalParameters;

export function initialize(overlayCanvasId, inputCanvasId, snapshot) {
    ensureCoastalCanvas(inputCanvasId);
    const adapted = adaptSnapshot(retainGeometry(snapshot));
    exposeCoastalMeshStats(adapted);
    surface.initialize(overlayCanvasId, inputCanvasId, adapted);
    ocean.initialize(inputCanvasId, adapted);
    coast.initialize(inputCanvasId, adapted);
}

export function setPlanet(snapshot) {
    const adapted = adaptSnapshot(retainGeometry(snapshot));
    exposeCoastalMeshStats(adapted);
    surface.setPlanet(adapted);
    ocean.setPlanet(adapted);
    coast.setPlanet(adapted);
}

export function getRetainedSurfaceGeometry() {
    return {
        surfaceTiles: retainedSurfaceTiles,
        physicalParameters: retainedPhysicalParameters
    };
}

export function dispose() {
    coast.dispose();
    document.getElementById('coastal-relief-canvas')?.remove();
    if (window.__planetForgeCoastMeshStats) delete window.__planetForgeCoastMeshStats;
    ocean.dispose();
    surface.dispose();
}

function ensureCoastalCanvas(inputCanvasId) {
    if (document.getElementById('coastal-relief-canvas')) return;
    const inputCanvas = document.getElementById(inputCanvasId);
    const parent = inputCanvas?.parentElement;
    if (!parent) return;

    const canvas = document.createElement('canvas');
    canvas.id = 'coastal-relief-canvas';
    canvas.setAttribute('aria-hidden', 'true');
    canvas.style.position = 'absolute';
    canvas.style.inset = '0';
    canvas.style.width = '100%';
    canvas.style.height = '100%';
    canvas.style.zIndex = '4';
    canvas.style.pointerEvents = 'none';

    const waterOverlay = document.getElementById('water-overlay-canvas');
    parent.insertBefore(canvas, waterOverlay ?? null);
}

function retainGeometry(snapshot) {
    if (!snapshot) return snapshot;

    const geometryTiles = (snapshot.surfaceTiles ?? []).filter(hasGeometry);
    if (geometryTiles.length > 0) retainedSurfaceTiles = geometryTiles;
    if (snapshot.physicalParameters) retainedPhysicalParameters = snapshot.physicalParameters;

    const hasIncomingGeometry = geometryTiles.length > 0;
    const hasPhysicalParameters = Boolean(snapshot.physicalParameters);
    if ((hasIncomingGeometry || retainedSurfaceTiles.length === 0) && (hasPhysicalParameters || !retainedPhysicalParameters)) {
        return hasIncomingGeometry && geometryTiles.length !== snapshot.surfaceTiles?.length ? { ...snapshot, surfaceTiles: geometryTiles } : snapshot;
    }

    return {
        ...snapshot,
        surfaceTiles: hasIncomingGeometry ? geometryTiles : retainedSurfaceTiles,
        physicalParameters: snapshot.physicalParameters ?? retainedPhysicalParameters
    };
}

function hasGeometry(tile) {
    return Boolean(tile?.key && tile.surfaceVertexCount > 0 && tile.positions?.length >= 3 && tile.normals?.length >= 3);
}

function exposeCoastalMeshStats(snapshot) {
    if (!new URLSearchParams(window.location.search).has('visualTest')) return;
    const radiusMeters = snapshot?.physicalParameters?.radiusMeters ?? 6_371_000.0;
    let minimum = Number.POSITIVE_INFINITY;
    let maximum = Number.NEGATIVE_INFINITY;
    let nearSea = 0;
    let lowLand = 0;
    let land = 0;
    let ocean = 0;
    let vertices = 0;

    for (const tile of snapshot?.surfaceTiles ?? []) {
        const positions = tile?.positions ?? [];
        for (let index = 0; index + 2 < positions.length; index += 3) {
            const radius = Math.hypot(positions[index], positions[index + 1], positions[index + 2]);
            const elevation = (radius - 1.0) * radiusMeters;
            minimum = Math.min(minimum, elevation);
            maximum = Math.max(maximum, elevation);
            if (Math.abs(elevation) <= 2_000.0) nearSea++;
            if (elevation >= 0.0 && elevation <= 5_000.0) lowLand++;
            if (elevation >= 0.0) land++; else ocean++;
            vertices++;
        }
    }

    window.__planetForgeCoastMeshStats = { minimum, maximum, nearSea, lowLand, land, ocean, vertices };
}

function adaptSnapshot(snapshot) {
    const feedback = snapshot?.climateFeedback;
    if (!feedback) return snapshot;

    const inherited = feedback.landIceFraction ?? feedback.cryosphereFraction;
    if (!Number.isFinite(inherited)) return snapshot;

    const physicalFraction = Math.max(0.0, Math.min(1.0, inherited));
    const visibleLandIceFraction = physicalFraction >= 0.999 ? 1.0 : Math.pow(physicalFraction, 2.2);
    return {
        ...snapshot,
        climateFeedback: {
            ...feedback,
            landIceFraction: visibleLandIceFraction
        }
    };
}
