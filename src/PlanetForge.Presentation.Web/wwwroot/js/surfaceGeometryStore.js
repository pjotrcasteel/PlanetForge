let retainedSurfaceTiles = [];
let retainedPhysicalParameters;
let retainedTerrainRevision = 0;

export function retainSurfaceGeometry(snapshot) {
    if (!snapshot) return snapshot;

    const geometryTiles = (snapshot.surfaceTiles ?? []).filter(hasGeometry);
    if (geometryTiles.length > 0) retainedSurfaceTiles = geometryTiles;
    if (snapshot.physicalParameters) retainedPhysicalParameters = snapshot.physicalParameters;
    if (Number.isFinite(snapshot.terrainRevision)) retainedTerrainRevision = snapshot.terrainRevision;

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
}

function hasGeometry(tile) {
    return Boolean(tile?.key && tile.surfaceVertexCount > 0 && tile.positions?.length >= 3 && tile.normals?.length >= 3);
}
