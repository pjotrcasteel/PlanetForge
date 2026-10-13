// The versioned main renderer and unversioned overlays must observe ONE live
// camera and geological revision. Browser ESM treats ?v=... URLs as distinct
// modules; this stable dependency owns only a read-only camera binding.
let activeRendererState = null;

export function bindPlanetCameraState(rendererState) {
    activeRendererState = rendererState;
}

export function clearPlanetCameraState(rendererState) {
    if (activeRendererState === rendererState) activeRendererState = null;
}

export function getPlanetCameraSnapshot() {
    const state = activeRendererState;
    if (!state) return null;
    return {
        yaw: state.yaw,
        pitch: state.pitch,
        distance: state.distance,
        mode: state.renderMode,
        geometryKey: state.geometryKey,
        seed: state.seed,
        planetRadiusMeters: state.planetRadiusMeters,
        seaLevelMeters: state.seaLevelMeters
    };
}
