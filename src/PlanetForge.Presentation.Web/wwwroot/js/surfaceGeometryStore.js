let retainedSurfaceTiles = [];
let retainedPhysicalParameters;

export function retainSurfaceGeometry(snapshot) {
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

export function getRetainedSurfaceGeometry() {
    return {
        surfaceTiles: retainedSurfaceTiles,
        physicalParameters: retainedPhysicalParameters
    };
}

export function clearRetainedSurfaceGeometry() {
    retainedSurfaceTiles = [];
    retainedPhysicalParameters = undefined;
}

function hasGeometry(tile) {
    return Boolean(tile?.key && tile.surfaceVertexCount > 0 && tile.positions?.length >= 3 && tile.normals?.length >= 3);
}
