let retainedSurfaceTiles = [];
let retainedPhysicalParameters;
let retainedTerrainRevision = 0;
let retainedSeed;
const cachedGeometryByKey = new Map();
const maximumRetainedGeometryTiles = 512;

export function retainSurfaceGeometry(snapshot) {
    if (!snapshot) return snapshot;

    const incomingRadius = snapshot.physicalParameters?.radiusMeters;
    if ((Number.isInteger(snapshot.seed) && retainedSeed !== undefined && snapshot.seed !== retainedSeed)
        || (Number.isFinite(incomingRadius) && retainedPhysicalParameters && incomingRadius !== retainedPhysicalParameters.radiusMeters)
        || (Number.isFinite(snapshot.terrainRevision) && snapshot.terrainRevision !== retainedTerrainRevision)) {
        clearRetainedSurfaceGeometry();
    }

    if (Number.isInteger(snapshot.seed)) retainedSeed = snapshot.seed;
    if (snapshot.physicalParameters) retainedPhysicalParameters = snapshot.physicalParameters;
    if (Number.isFinite(snapshot.terrainRevision)) retainedTerrainRevision = snapshot.terrainRevision;

    const requested = snapshot.surfaceTiles ?? [];
    for (const tile of requested) {
        if (!hasGeometry(tile)) continue;
        cachedGeometryByKey.delete(tile.key);
        cachedGeometryByKey.set(tile.key, tile);
    }

    if (requested.length > 0) {
        const completeView = requested.map(tile => cachedGeometryByKey.get(tile.key)).filter(Boolean);
        // Incomplete references can occur during a cache reset. Keep the previous complete
        // terrain view instead of presenting a partial coastline/index to overlay consumers.
        if (completeView.length === requested.length) retainedSurfaceTiles = completeView;
    }

    while (cachedGeometryByKey.size > maximumRetainedGeometryTiles) {
        const oldest = cachedGeometryByKey.keys().next().value;
        if (oldest === undefined) break;
        cachedGeometryByKey.delete(oldest);
    }

    return {
        ...snapshot,
        surfaceTiles: requested.length > 0 ? retainedSurfaceTiles : requested,
        physicalParameters: snapshot.physicalParameters ?? retainedPhysicalParameters
    };
}

export function getRetainedSurfaceGeometry() {
    return {
        surfaceTiles: retainedSurfaceTiles,
        physicalParameters: retainedPhysicalParameters,
        terrainRevision: retainedTerrainRevision
    };
}

export function clearRetainedSurfaceGeometry() {
    retainedSurfaceTiles = [];
    retainedPhysicalParameters = undefined;
    retainedTerrainRevision = 0;
    retainedSeed = undefined;
    cachedGeometryByKey.clear();
}

function hasGeometry(tile) {
    return Boolean(tile?.key && tile.surfaceVertexCount > 0 && tile.positions?.length >= 3 && tile.normals?.length >= 3);
}
