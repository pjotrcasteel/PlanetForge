import { clearRetainedSurfaceGeometry, retainSurfaceGeometry } from './surfaceGeometryStore.js';

let state;

const verticalFieldOfViewRadians = Math.PI / 4.2;
const surfaceUpdateDebounceMilliseconds = 240;
const localSurfaceUpdateDebounceMilliseconds = 650;
const maximumCachedSurfaceTiles = 512;
const maximumRenderPixelRatio = 1.5;
const minimumCameraAltitudeMeters = 3.0;
const maximumCameraAltitudeRatio = 4.2;
const localTransitionAltitudeMeters = 20_000.0;
const localExitAltitudeMeters = 25_000.0;
const treeVisibilityAltitudeMeters = 3_000.0;
const maximumPlaceholderTrees = 36;
const placeholderTreeHeightMeters = 15.0;
const placeholderTreeHalfWidthMeters = 3.0;
const minimumLocalViewPitchRadians = 0.24;
const maximumLocalViewPitchRadians = 1.48;
const landReliefExaggeration = 36.0;
const oceanReliefExaggeration = 4.0;
const iceVisualExaggeration = 18.0;
const waterSurfaceClearanceMeters = 18.0;
const globeSkirtVisibilityAltitudeMeters = 500_000.0;

export function initialize(canvasId, snapshot, dotNetReference) {
    const canvas = document.getElementById(canvasId);
    const gl = canvas?.getContext('webgl2', { antialias: true, alpha: true });
    if (!canvas || !gl) throw new Error('PlanetForge requires WebGL 2.');

    document.title = 'PlanetForge 0.0.17.4 — Unified Surface Foundation';
    try {
        state = createState(canvas, gl, dotNetReference);
        installInput(state);
        setPlanet(snapshot);
        installVisualTestApi();
        requestAnimationFrame(render);
    } catch (error) {
        window.__planetForgeSurfaceError = error instanceof Error ? error.message : String(error);
        console.error('PlanetForge unified surface initialization failed.', error);
        throw error;
    }
}

export function setPlanet(snapshot) {
    if (!state) return;
    state.dirty = true;
    state.seaLevelMeters = snapshot.seaLevelMeters;
    state.planetRadiusMeters = snapshot.physicalParameters.radiusMeters;
    state.atmosphereDensity = snapshot.atmosphereDensity;
    state.equilibriumTemperature = snapshot.physics.equilibriumTemperatureKelvin;
    state.surfaceTemperature = snapshot.climate.surfaceTemperatureKelvin;
    state.solarFlux = snapshot.physics.solarFluxWattsPerSquareMeter;
    state.liquidFraction = snapshot.water.liquidFraction;
    state.vaporFraction = snapshot.water.vaporFraction;
    state.seaIceFraction = snapshot.climateFeedback?.seaIceFraction ?? snapshot.climateFeedback?.cryosphereFraction ?? state.seaIceFraction;
    state.landIceFraction = snapshot.climateFeedback?.landIceFraction ?? snapshot.climateFeedback?.cryosphereFraction ?? state.landIceFraction;
    state.snowCoverFraction = snapshot.climateFeedback?.snowCoverFraction ?? snapshot.climateFeedback?.cryosphereFraction ?? state.snowCoverFraction;

    const geometryKey = `${snapshot.seed}:${snapshot.physicalParameters.radiusMeters}`;
    if (state.geometryKey !== geometryKey) {
        clearRetainedSurfaceGeometry();
        clearSurfaceBufferCache(state);
        clearLocalSurfaceBuffer(state);
        state.geometryKey = geometryKey;
    }
    retainSurfaceGeometry(snapshot);

    if (snapshot.localSurface) {
        const enteringLocal = state.renderMode !== 'local';
        if (enteringLocal) {
            state.localYaw = -0.65;
            state.localPitch = 0.72;
            state.localCameraAltitudeMeters = snapshot.localSurface.cameraAltitudeMeters;
            state.distance = 1.0 + (state.localCameraAltitudeMeters / state.planetRadiusMeters);
        }

        state.renderMode = 'local';
        activateLocalSurface(state, snapshot.localSurface);
        updateLocalScaleHud(state);
        return;
    }

    state.renderMode = 'globe';
    state.localCameraAltitudeMeters = null;
    updateLocalScaleHud(state);
    const surfaceKey = snapshot.surfaceTiles.map(tile => tile.key).join('|');
    if (state.surfaceKey !== surfaceKey) {
        state.surfaceKey = surfaceKey;
        activateSurfaceTiles(state, snapshot.surfaceTiles);
    }
}

export function dispose() {
    if (!state) return;
    if (state.lodTimer !== null) clearTimeout(state.lodTimer);
    state.lodSequence++;
    state.surfaceRequestPending = false;
    state.dotNetReference = null;
    clearSurfaceBufferCache(state);
    clearLocalSurfaceBuffer(state);
    state.scaleHud?.remove();
    state.gl.deleteProgram(state.globeProgram);
    state.gl.deleteProgram(state.localProgram);
    clearRetainedSurfaceGeometry();
    if (window.__planetForgeSurfaceTest) delete window.__planetForgeSurfaceTest;
    if (window.__planetForgeSurfaceError) delete window.__planetForgeSurfaceError;
    state = null;
}

function createState(canvas, gl, dotNetReference) {
    const globeProgram = createProgram(gl, globeVertexShaderSource, globeFragmentShaderSource);
    const localProgram = createProgram(gl, localVertexShaderSource, localFragmentShaderSource);
    return {
        canvas, gl, dotNetReference, globeProgram, localProgram, renderMode: 'globe', geometryKey: null,
        surfaceKey: null, tileBufferCache: new Map(), tiles: [], localSurface: null,
        yaw: -0.65, pitch: 0.24, distance: 3.15, localYaw: -0.65, localPitch: 0.72,
        localCameraAltitudeMeters: null, scaleHud: createLocalScaleHud(canvas),
        dragging: false, lastX: 0, lastY: 0,
        lodTimer: null, lodSequence: 0, lastSurfaceRequestSignature: null,
        surfaceRequestInFlight: false, surfaceRequestPending: false, dirty: true,
        seaLevelMeters: 0, planetRadiusMeters: 6371000, atmosphereDensity: 0.6,
        equilibriumTemperature: 255, surfaceTemperature: 288, solarFlux: 1361,
        liquidFraction: 1, vaporFraction: 0, seaIceFraction: 1, landIceFraction: 1, snowCoverFraction: 1,
        globeAttributes: {
            position: gl.getAttribLocation(globeProgram, 'aPosition'),
            normal: gl.getAttribLocation(globeProgram, 'aNormal')
        },
        globeUniforms: {
            model: gl.getUniformLocation(globeProgram, 'uModel'),
            viewProjection: gl.getUniformLocation(globeProgram, 'uViewProjection'),
            light: gl.getUniformLocation(globeProgram, 'uLightDirection'),
            cameraPosition: gl.getUniformLocation(globeProgram, 'uCameraPosition'),
            seaLevelMeters: gl.getUniformLocation(globeProgram, 'uSeaLevelMeters'),
            planetRadiusMeters: gl.getUniformLocation(globeProgram, 'uPlanetRadiusMeters'),
            atmosphere: gl.getUniformLocation(globeProgram, 'uAtmosphere'),
            equilibriumTemperature: gl.getUniformLocation(globeProgram, 'uEquilibriumTemperature'),
            surfaceTemperature: gl.getUniformLocation(globeProgram, 'uSurfaceTemperature'),
            solarFlux: gl.getUniformLocation(globeProgram, 'uSolarFlux'),
            liquidFraction: gl.getUniformLocation(globeProgram, 'uLiquidFraction'),
            vaporFraction: gl.getUniformLocation(globeProgram, 'uVaporFraction'),
            seaIceFraction: gl.getUniformLocation(globeProgram, 'uSeaIceFraction'),
            landIceFraction: gl.getUniformLocation(globeProgram, 'uLandIceFraction'),
            snowCoverFraction: gl.getUniformLocation(globeProgram, 'uSnowCoverFraction'),
            mode: gl.getUniformLocation(globeProgram, 'uMode')
        },
        localAttributes: {
            position: gl.getAttribLocation(localProgram, 'aPositionMeters'),
            normal: gl.getAttribLocation(localProgram, 'aNormal'),
            elevation: gl.getAttribLocation(localProgram, 'aElevationMeters')
        },
        localUniforms: {
            viewProjection: gl.getUniformLocation(localProgram, 'uViewProjection'),
            light: gl.getUniformLocation(localProgram, 'uLightDirection'),
            seaLevelMeters: gl.getUniformLocation(localProgram, 'uSeaLevelMeters'),
            surfaceTemperature: gl.getUniformLocation(localProgram, 'uSurfaceTemperature'),
            liquidFraction: gl.getUniformLocation(localProgram, 'uLiquidFraction'),
            vaporFraction: gl.getUniformLocation(localProgram, 'uVaporFraction'),
            objectMode: gl.getUniformLocation(localProgram, 'uObjectMode')
        }
    };
}

function createLocalScaleHud(canvas) {
    const stage = canvas.closest('.planet-stage');
    if (!stage) return null;

    const hud = document.createElement('div');
    hud.className = 'local-scale-hud';
    hud.hidden = true;
    hud.innerHTML = '<span class="local-scale-trees" aria-hidden="true">🌲 🌲</span><span class="local-scale-copy"></span>';
    stage.appendChild(hud);
    return hud;
}

function updateLocalScaleHud(s) {
    if (!s.scaleHud) return;
    if (s.renderMode !== 'local' || s.localCameraAltitudeMeters === null) {
        s.scaleHud.hidden = true;
        return;
    }

    const altitude = Math.max(s.localCameraAltitudeMeters, minimumCameraAltitudeMeters);
    const altitudeText = altitude >= 1_000.0 ? `${(altitude / 1_000.0).toFixed(altitude >= 10_000.0 ? 0 : 1)} km` : `${Math.round(altitude)} m`;
    const treeScale = 1.0 - clamp(Math.log10(altitude) / Math.log10(localTransitionAltitudeMeters), 0.0, 1.0);
    const treeSizePixels = 14.0 + (treeScale * 28.0);
    const trees = s.scaleHud.querySelector('.local-scale-trees');
    const copy = s.scaleHud.querySelector('.local-scale-copy');
    if (trees) trees.style.fontSize = `${treeSizePixels.toFixed(0)}px`;
    if (copy) copy.textContent = `ALT ${altitudeText} · WORLD TREES ≈ 15 m`;
    s.scaleHud.hidden = false;
}

function activateSurfaceTiles(s, surfaceTiles) {
    const activeKeys = new Set();
    s.tiles = surfaceTiles.map(tile => {
        activeKeys.add(tile.key);
        let bufferedTile = s.tileBufferCache.get(tile.key);
        if (!bufferedTile) {
            bufferedTile = createBufferedTile(s.gl, tile);
            s.tileBufferCache.set(tile.key, bufferedTile);
        }

        return bufferedTile;
    });

    trimSurfaceBufferCache(s, activeKeys);
}

function createBufferedTile(gl, tile) {
    const positionBuffer = gl.createBuffer();
    const normalBuffer = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, positionBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(tile.positions), gl.STATIC_DRAW);
    gl.bindBuffer(gl.ARRAY_BUFFER, normalBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(tile.normals), gl.STATIC_DRAW);
    return {
        key: tile.key,
        positionBuffer,
        normalBuffer,
        surfaceVertexCount: tile.surfaceVertexCount,
        skirtVertexCount: tile.skirtVertexCount
    };
}

function activateLocalSurface(s, localSurface) {
    if (s.localSurface?.key === localSurface.key) return;
    if (!localSurface.positionsMeters?.length || !localSurface.normals?.length || !localSurface.elevationsMeters?.length) return;

    clearLocalSurfaceBuffer(s);
    const positionBuffer = createStaticBuffer(s.gl, localSurface.positionsMeters);
    const normalBuffer = createStaticBuffer(s.gl, localSurface.normals);
    const elevationBuffer = createStaticBuffer(s.gl, localSurface.elevationsMeters);
    const trees = createPlaceholderTrees(localSurface, s.seaLevelMeters);

    s.localSurface = {
        key: localSurface.key,
        positionBuffer,
        normalBuffer,
        elevationBuffer,
        vertexCount: localSurface.vertexCount,
        sizeMeters: localSurface.sizeMeters,
        treePositionBuffer: trees.vertexCount > 0 ? createStaticBuffer(s.gl, trees.positions) : null,
        treeNormalBuffer: trees.vertexCount > 0 ? createStaticBuffer(s.gl, trees.normals) : null,
        treeElevationBuffer: trees.vertexCount > 0 ? createStaticBuffer(s.gl, trees.elevations) : null,
        treeVertexCount: trees.vertexCount
    };
}

function createStaticBuffer(gl, values) {
    const buffer = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(values), gl.STATIC_DRAW);
    return buffer;
}

function createPlaceholderTrees(localSurface, seaLevelMeters) {
    const anchors = [];
    const positions = localSurface.positionsMeters;
    const elevations = localSurface.elevationsMeters;
    const vertexCount = Math.floor(positions.length / 3);
    const targetCount = Math.min(maximumPlaceholderTrees, Math.max(5, Math.round(localSurface.sizeMeters / 300.0)));
    const stride = Math.max(1, Math.floor(vertexCount / Math.max(targetCount * 6, 1)));
    const seen = new Set();

    for (let index = 0; index < vertexCount && anchors.length < targetCount; index += stride) {
        const elevation = elevations[index];
        if (!Number.isFinite(elevation) || elevation <= seaLevelMeters + 2.0 || elevation >= 3_200.0) continue;

        const offset = index * 3;
        const x = positions[offset];
        const y = positions[offset + 1];
        const z = positions[offset + 2];
        if (Math.hypot(x, z) > localSurface.sizeMeters * 0.46) continue;

        const key = `${Math.round(x)}:${Math.round(z)}`;
        if (seen.has(key)) continue;
        seen.add(key);
        anchors.push([x, y, z, elevation]);
    }

    const treePositions = [];
    const treeNormals = [];
    const treeElevations = [];
    for (const anchor of anchors) appendTree(treePositions, treeNormals, treeElevations, anchor);

    return {
        positions: treePositions,
        normals: treeNormals,
        elevations: treeElevations,
        vertexCount: treePositions.length / 3
    };
}

function appendTree(positions, normals, elevations, anchor) {
    const [x, y, z, elevation] = anchor;
    const baseY = y + 0.25;
    const apex = [x, baseY + placeholderTreeHeightMeters, z];
    const a = [x - placeholderTreeHalfWidthMeters, baseY, z - placeholderTreeHalfWidthMeters];
    const b = [x + placeholderTreeHalfWidthMeters, baseY, z - placeholderTreeHalfWidthMeters];
    const c = [x + placeholderTreeHalfWidthMeters, baseY, z + placeholderTreeHalfWidthMeters];
    const d = [x - placeholderTreeHalfWidthMeters, baseY, z + placeholderTreeHalfWidthMeters];
    appendTreeTriangle(positions, normals, elevations, a, b, apex, elevation);
    appendTreeTriangle(positions, normals, elevations, b, c, apex, elevation);
    appendTreeTriangle(positions, normals, elevations, c, d, apex, elevation);
    appendTreeTriangle(positions, normals, elevations, d, a, apex, elevation);
}

function appendTreeTriangle(positions, normals, elevations, a, b, c, elevation) {
    const ab = [b[0] - a[0], b[1] - a[1], b[2] - a[2]];
    const ac = [c[0] - a[0], c[1] - a[1], c[2] - a[2]];
    const normal = normalize(cross(ab, ac));
    positions.push(...a, ...b, ...c);
    normals.push(...normal, ...normal, ...normal);
    elevations.push(elevation, elevation, elevation);
}

function clearSurfaceBufferCache(s) {
    for (const tile of s.tileBufferCache.values()) deleteBufferedTile(s.gl, tile);
    s.tileBufferCache.clear();
    s.tiles = [];
    s.surfaceKey = null;
}

function clearLocalSurfaceBuffer(s) {
    if (!s.localSurface) return;
    s.gl.deleteBuffer(s.localSurface.positionBuffer);
    s.gl.deleteBuffer(s.localSurface.normalBuffer);
    s.gl.deleteBuffer(s.localSurface.elevationBuffer);
    if (s.localSurface.treePositionBuffer) s.gl.deleteBuffer(s.localSurface.treePositionBuffer);
    if (s.localSurface.treeNormalBuffer) s.gl.deleteBuffer(s.localSurface.treeNormalBuffer);
    if (s.localSurface.treeElevationBuffer) s.gl.deleteBuffer(s.localSurface.treeElevationBuffer);
    s.localSurface = null;
}

function trimSurfaceBufferCache(s, activeKeys) {
    if (s.tileBufferCache.size <= maximumCachedSurfaceTiles) return;
    for (const [key, tile] of s.tileBufferCache) {
        if (activeKeys.has(key)) continue;
        deleteBufferedTile(s.gl, tile);
        s.tileBufferCache.delete(key);
        if (s.tileBufferCache.size <= maximumCachedSurfaceTiles) break;
    }
}

function deleteBufferedTile(gl, tile) {
    gl.deleteBuffer(tile.positionBuffer);
    gl.deleteBuffer(tile.normalBuffer);
}

function installInput(s) {
    const canvas = s.canvas;
    canvas.addEventListener('pointerdown', event => {
        s.dragging = true;
        s.dirty = true;
        s.lastX = event.clientX;
        s.lastY = event.clientY;
        canvas.setPointerCapture(event.pointerId);
    });
    canvas.addEventListener('pointerup', event => {
        s.dragging = false;
        canvas.releasePointerCapture(event.pointerId);
    });
    canvas.addEventListener('pointercancel', () => {
        s.dragging = false;
    });
    canvas.addEventListener('pointermove', event => {
        if (!s.dragging) return;
        s.dirty = true;
        const deltaX = event.clientX - s.lastX;
        const deltaY = event.clientY - s.lastY;
        s.lastX = event.clientX;
        s.lastY = event.clientY;

        if (s.renderMode === 'local') {
            s.localYaw += deltaX * 0.008;
            s.localPitch = clamp(s.localPitch - deltaY * 0.008, minimumLocalViewPitchRadians, maximumLocalViewPitchRadians);
            return;
        }

        s.yaw += deltaX * 0.008;
        s.pitch = clamp(s.pitch + deltaY * 0.008, -1.25, 1.25);
    });
    canvas.addEventListener('wheel', event => {
        event.preventDefault();
        s.dirty = true;
        const zoomFactor = Math.exp(event.deltaY * 0.0015);

        if (s.renderMode === 'local' && s.localSurface) {
            const currentAltitude = s.localCameraAltitudeMeters ?? localTransitionAltitudeMeters;
            const nextAltitude = clamp(currentAltitude * zoomFactor, minimumCameraAltitudeMeters, localExitAltitudeMeters);
            s.localCameraAltitudeMeters = nextAltitude;
            s.distance = 1.0 + (nextAltitude / Math.max(s.planetRadiusMeters, 1.0));

            if (nextAltitude >= localExitAltitudeMeters) {
                s.renderMode = 'globe';
                s.localCameraAltitudeMeters = null;
                updateLocalScaleHud(s);
                scheduleSurfaceUpdate(s, surfaceUpdateDebounceMilliseconds);
                return;
            }

            updateLocalScaleHud(s);
            scheduleSurfaceUpdate(s, localSurfaceUpdateDebounceMilliseconds);
            return;
        }

        const minimumCameraAltitudeRatio = minimumCameraAltitudeMeters / Math.max(s.planetRadiusMeters, 1.0);
        const altitudeRatio = clamp(s.distance - 1.0, minimumCameraAltitudeRatio, maximumCameraAltitudeRatio);
        s.distance = 1.0 + clamp(altitudeRatio * zoomFactor, minimumCameraAltitudeRatio, maximumCameraAltitudeRatio);
        if (shouldRequestSurfaceUpdate(s)) scheduleSurfaceUpdate(s, surfaceUpdateDebounceMilliseconds);
    }, { passive: false });
}

function shouldRequestSurfaceUpdate(s) {
    if (s.renderMode === 'local') return true;
    const altitudeMeters = Math.max(0.0, (s.distance - 1.0) * s.planetRadiusMeters);
    return altitudeMeters <= localTransitionAltitudeMeters;
}

function scheduleSurfaceUpdate(s, delayMilliseconds) {
    if (!s.dotNetReference) return;
    if (s.lodTimer !== null) clearTimeout(s.lodTimer);

    const sequence = ++s.lodSequence;
    s.surfaceRequestPending = true;
    if (s.surfaceRequestInFlight) return;
    s.lodTimer = setTimeout(() => requestSurfaceUpdate(s, sequence), delayMilliseconds);
}

async function requestSurfaceUpdate(s, sequence) {
    s.lodTimer = null;
    if (state !== s || sequence !== s.lodSequence) return;
    if (s.surfaceRequestInFlight) {
        s.surfaceRequestPending = true;
        return;
    }

    const eye = orbitEye(s.yaw, s.pitch, s.distance);
    const direction = normalize(eye);
    const viewportWidth = Math.max(s.canvas.width, 1);
    const viewportHeight = Math.max(s.canvas.height, 1);
    const signature = `${direction[0].toFixed(5)}:${direction[1].toFixed(5)}:${direction[2].toFixed(5)}:${s.distance.toFixed(7)}:${viewportWidth}:${viewportHeight}`;
    if (signature === s.lastSurfaceRequestSignature) {
        s.surfaceRequestPending = false;
        return;
    }

    s.surfaceRequestInFlight = true;
    s.surfaceRequestPending = false;
    s.lastSurfaceRequestSignature = signature;
    try {
        const snapshot = await s.dotNetReference.invokeMethodAsync(
            'UpdateSurfaceView',
            direction[0], direction[1], direction[2], s.distance,
            viewportWidth, viewportHeight, verticalFieldOfViewRadians);

        if (state === s && sequence === s.lodSequence) setPlanet(snapshot);
    } catch (error) {
        if (state === s) console.error('PlanetForge surface update failed.', error);
    } finally {
        s.surfaceRequestInFlight = false;
        if (state === s && s.surfaceRequestPending) {
            s.surfaceRequestPending = false;
            scheduleSurfaceUpdate(s, 0);
        }
    }
}

function installVisualTestApi() {
    if (!new URLSearchParams(window.location.search).has('visualTest')) return;
    window.__planetForgeSurfaceTest = {
        setPitch(pitch) {
            if (!state) return;
            state.pitch = clamp(pitch, -1.25, 1.25);
            state.dirty = true;
            renderGlobe(state);
            state.dirty = false;
        },
        getPitch() { return state?.pitch ?? 0.0; },
        setYaw(yaw) {
            if (!state) return;
            state.yaw = yaw;
            state.dirty = true;
            renderGlobe(state);
            state.dirty = false;
        },
        getYaw() { return state?.yaw ?? 0.0; },
        measure() {
            if (!state) return null;
            renderGlobe(state);
            state.dirty = false;
            return measureSurface(state);
        }
    };
}

function measureSurface(s) {
    const { gl, canvas } = s;
    const pixels = new Uint8Array(canvas.width * canvas.height * 4);
    gl.readPixels(0, 0, canvas.width, canvas.height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
    let planetPixels = 0;
    let oceanPixels = 0;
    let icePixels = 0;
    let minimumOceanLuminance = Number.POSITIVE_INFINITY;
    let maximumOceanLuminance = Number.NEGATIVE_INFINITY;

    for (let offset = 0; offset < pixels.length; offset += 4) {
        const alpha = pixels[offset + 3] / 255.0;
        if (alpha < 0.20) continue;
        const red = pixels[offset] / 255.0;
        const green = pixels[offset + 1] / 255.0;
        const blue = pixels[offset + 2] / 255.0;
        const luminance = (red * 0.2126) + (green * 0.7152) + (blue * 0.0722);
        planetPixels++;

        const isIce = luminance > 0.46 && green >= red * 0.82 && blue >= red * 0.82;
        if (isIce) icePixels++;

        const isOcean = blue > red * 1.35 && green > red * 1.30 && blue >= green * 0.72;
        if (!isOcean) continue;
        oceanPixels++;
        minimumOceanLuminance = Math.min(minimumOceanLuminance, luminance);
        maximumOceanLuminance = Math.max(maximumOceanLuminance, luminance);
    }

    return {
        planetPixels,
        oceanPixels,
        icePixels,
        iceFraction: planetPixels > 0 ? icePixels / planetPixels : 0.0,
        oceanFraction: planetPixels > 0 ? oceanPixels / planetPixels : 0.0,
        oceanLuminanceRange: oceanPixels > 0 ? maximumOceanLuminance - minimumOceanLuminance : 0.0,
        glError: gl.getError()
    };
}

function render() {
    if (!state) return;
    resize(state);
    if (state.dirty) {
        if (state.renderMode === 'local' && state.localSurface) renderLocal(state);
        else renderGlobe(state);
        state.dirty = false;
    }
    requestAnimationFrame(render);
}

function isPreBiologicalSurface(s) {
    return s.canvas.closest('.planet-stage')?.classList.contains('pre-vegetation-world') ?? false;
}

function renderGlobe(s) {
    const { gl, canvas, globeProgram } = s;
    prepareFrame(gl, canvas);
    gl.useProgram(globeProgram);

    const aspect = canvas.width / Math.max(canvas.height, 1);
    const projection = perspective(verticalFieldOfViewRadians, aspect, 0.002, 20);
    const eye = orbitEye(s.yaw, s.pitch, s.distance);
    const view = lookAt(eye, [0, 0, 0], [0, 1, 0]);
    const viewProjection = multiply(projection, view);

    gl.uniformMatrix4fv(s.globeUniforms.model, false, identityMatrix());
    gl.uniformMatrix4fv(s.globeUniforms.viewProjection, false, viewProjection);
    gl.uniform3f(s.globeUniforms.light, 0.7, 0.35, 0.6);
    gl.uniform3f(s.globeUniforms.cameraPosition, eye[0], eye[1], eye[2]);
    gl.uniform1f(s.globeUniforms.seaLevelMeters, s.seaLevelMeters);
    gl.uniform1f(s.globeUniforms.planetRadiusMeters, s.planetRadiusMeters);
    gl.uniform1f(s.globeUniforms.atmosphere, s.atmosphereDensity);
    gl.uniform1f(s.globeUniforms.equilibriumTemperature, s.equilibriumTemperature);
    gl.uniform1f(s.globeUniforms.surfaceTemperature, s.surfaceTemperature);
    gl.uniform1f(s.globeUniforms.solarFlux, s.solarFlux);
    gl.uniform1f(s.globeUniforms.liquidFraction, s.liquidFraction);
    gl.uniform1f(s.globeUniforms.vaporFraction, s.vaporFraction);
    gl.uniform1f(s.globeUniforms.seaIceFraction, s.seaIceFraction);
    gl.uniform1f(s.globeUniforms.landIceFraction, s.landIceFraction);
    gl.uniform1f(s.globeUniforms.snowCoverFraction, s.snowCoverFraction);

    gl.disable(gl.BLEND);
    gl.depthMask(true);
    gl.uniform1i(s.globeUniforms.mode, isPreBiologicalSurface(s) ? 2 : 0);
    drawTerrain(s);

    if (s.liquidFraction > 0.001) {
        gl.uniform1i(s.globeUniforms.mode, 3);
        drawSurface(s);
    }

    gl.enable(gl.BLEND);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    gl.depthMask(true);
    gl.enable(gl.POLYGON_OFFSET_FILL);
    gl.polygonOffset(-2.0, -2.0);
    gl.uniform1i(s.globeUniforms.mode, 4);
    drawSurface(s);
    gl.disable(gl.POLYGON_OFFSET_FILL);

    gl.depthMask(false);
    gl.disable(gl.CULL_FACE);
    gl.uniform1i(s.globeUniforms.mode, 1);
    gl.uniformMatrix4fv(s.globeUniforms.model, false, scaleMatrix(1.065));
    drawAtmosphere(s);
    gl.uniformMatrix4fv(s.globeUniforms.model, false, identityMatrix());
    gl.depthMask(true);
    gl.enable(gl.CULL_FACE);
    gl.disable(gl.BLEND);
}

function renderLocal(s) {
    const { gl, canvas, localProgram, localSurface } = s;
    prepareFrame(gl, canvas);
    gl.useProgram(localProgram);

    const aspect = canvas.width / Math.max(canvas.height, 1);
    const cameraHeightMeters = Math.max(s.localCameraAltitudeMeters ?? localTransitionAltitudeMeters, minimumCameraAltitudeMeters);
    const horizontalDistanceMeters = cameraHeightMeters / Math.tan(s.localPitch);
    const eye = [Math.sin(s.localYaw) * horizontalDistanceMeters, cameraHeightMeters, Math.cos(s.localYaw) * horizontalDistanceMeters];
    const nearMeters = Math.max(0.02, Math.min(1.0, cameraHeightMeters * 0.001));
    const farMeters = Math.max(localSurface.sizeMeters * 3.0, Math.hypot(horizontalDistanceMeters, cameraHeightMeters) * 4.0);
    const projection = perspective(verticalFieldOfViewRadians, aspect, nearMeters, farMeters);
    const view = lookAt(eye, [0, 0, 0], [0, 1, 0]);
    const viewProjection = multiply(projection, view);
    const preBiological = isPreBiologicalSurface(s);

    gl.uniformMatrix4fv(s.localUniforms.viewProjection, false, viewProjection);
    gl.uniform3f(s.localUniforms.light, 0.45, 0.82, 0.35);
    gl.uniform1f(s.localUniforms.seaLevelMeters, s.seaLevelMeters);
    gl.uniform1f(s.localUniforms.surfaceTemperature, s.surfaceTemperature);
    gl.uniform1f(s.localUniforms.liquidFraction, s.liquidFraction);
    gl.uniform1f(s.localUniforms.vaporFraction, s.vaporFraction);

    gl.uniform1i(s.localUniforms.objectMode, preBiological ? 2 : 0);
    bindLocalAttributes(s, localSurface.positionBuffer, localSurface.normalBuffer, localSurface.elevationBuffer);
    gl.drawArrays(gl.TRIANGLES, 0, localSurface.vertexCount);

    if (!preBiological && cameraHeightMeters <= treeVisibilityAltitudeMeters && localSurface.treeVertexCount > 0) {
        gl.uniform1i(s.localUniforms.objectMode, 1);
        gl.disable(gl.CULL_FACE);
        bindLocalAttributes(s, localSurface.treePositionBuffer, localSurface.treeNormalBuffer, localSurface.treeElevationBuffer);
        gl.drawArrays(gl.TRIANGLES, 0, localSurface.treeVertexCount);
        gl.enable(gl.CULL_FACE);
    }
}

function prepareFrame(gl, canvas) {
    gl.viewport(0, 0, canvas.width, canvas.height);
    gl.enable(gl.DEPTH_TEST);
    gl.enable(gl.CULL_FACE);
    gl.clearColor(0, 0, 0, 0);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
}

function drawTerrain(s) {
    const gl = s.gl;
    gl.enable(gl.CULL_FACE);
    for (const tile of s.tiles) {
        bindTileAttributes(s, tile);
        gl.drawArrays(gl.TRIANGLES, 0, tile.surfaceVertexCount);
    }

    const altitudeMeters = Math.max(0.0, (s.distance - 1.0) * s.planetRadiusMeters);
    if (altitudeMeters > globeSkirtVisibilityAltitudeMeters) return;

    gl.disable(gl.CULL_FACE);
    for (const tile of s.tiles) {
        if (tile.skirtVertexCount <= 0) continue;
        bindTileAttributes(s, tile);
        gl.drawArrays(gl.TRIANGLES, tile.surfaceVertexCount, tile.skirtVertexCount);
    }
    gl.enable(gl.CULL_FACE);
}

function drawSurface(s) {
    const gl = s.gl;
    gl.enable(gl.CULL_FACE);
    for (const tile of s.tiles) {
        bindTileAttributes(s, tile);
        gl.drawArrays(gl.TRIANGLES, 0, tile.surfaceVertexCount);
    }
}

function drawAtmosphere(s) {
    for (const tile of s.tiles) {
        bindTileAttributes(s, tile);
        s.gl.drawArrays(s.gl.TRIANGLES, 0, tile.surfaceVertexCount);
    }
}

function bindTileAttributes(s, tile) {
    const gl = s.gl;
    gl.bindBuffer(gl.ARRAY_BUFFER, tile.positionBuffer);
    gl.enableVertexAttribArray(s.globeAttributes.position);
    gl.vertexAttribPointer(s.globeAttributes.position, 3, gl.FLOAT, false, 0, 0);
    gl.bindBuffer(gl.ARRAY_BUFFER, tile.normalBuffer);
    gl.enableVertexAttribArray(s.globeAttributes.normal);
    gl.vertexAttribPointer(s.globeAttributes.normal, 3, gl.FLOAT, false, 0, 0);
}

function bindLocalAttributes(s, positionBuffer, normalBuffer, elevationBuffer) {
    const gl = s.gl;
    gl.bindBuffer(gl.ARRAY_BUFFER, positionBuffer);
    gl.enableVertexAttribArray(s.localAttributes.position);
    gl.vertexAttribPointer(s.localAttributes.position, 3, gl.FLOAT, false, 0, 0);
    gl.bindBuffer(gl.ARRAY_BUFFER, normalBuffer);
    gl.enableVertexAttribArray(s.localAttributes.normal);
    gl.vertexAttribPointer(s.localAttributes.normal, 3, gl.FLOAT, false, 0, 0);
    gl.bindBuffer(gl.ARRAY_BUFFER, elevationBuffer);
    gl.enableVertexAttribArray(s.localAttributes.elevation);
    gl.vertexAttribPointer(s.localAttributes.elevation, 1, gl.FLOAT, false, 0, 0);
}

function resize(s) {
    const ratio = Math.min(window.devicePixelRatio || 1, maximumRenderPixelRatio);
    const width = Math.floor(s.canvas.clientWidth * ratio);
    const height = Math.floor(s.canvas.clientHeight * ratio);
    if (s.canvas.width === width && s.canvas.height === height) return;

    s.canvas.width = width;
    s.canvas.height = height;
    s.dirty = true;
    if (s.renderMode === 'local') scheduleSurfaceUpdate(s, localSurfaceUpdateDebounceMilliseconds);
}

function createProgram(gl, vertexSource, fragmentSource) {
    const vertex = compile(gl, gl.VERTEX_SHADER, vertexSource);
    const fragment = compile(gl, gl.FRAGMENT_SHADER, fragmentSource);
    const program = gl.createProgram();
    gl.attachShader(program, vertex);
    gl.attachShader(program, fragment);
    gl.linkProgram(program);
    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(program));
    return program;
}

function compile(gl, type, source) {
    const shader = gl.createShader(type);
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(shader));
    return shader;
}

function orbitEye(yaw, pitch, distance) {
    const cp = Math.cos(pitch);
    return [distance * cp * Math.sin(yaw), distance * Math.sin(pitch), distance * cp * Math.cos(yaw)];
}

function perspective(fov, aspect, near, far) {
    const f = 1 / Math.tan(fov / 2), nf = 1 / (near - far);
    return new Float32Array([f/aspect,0,0,0, 0,f,0,0, 0,0,(far+near)*nf,-1, 0,0,2*far*near*nf,0]);
}

function lookAt(eye, center, up) {
    const z = normalize([eye[0]-center[0], eye[1]-center[1], eye[2]-center[2]]);
    const x = normalize(cross(up, z));
    const y = cross(z, x);
    return new Float32Array([x[0],y[0],z[0],0, x[1],y[1],z[1],0, x[2],y[2],z[2],0, -dot(x,eye),-dot(y,eye),-dot(z,eye),1]);
}

function multiply(a, b) {
    const out = new Float32Array(16);
    for (let column=0; column<4; column++) for (let row=0; row<4; row++) out[column*4+row] = a[row]*b[column*4] + a[4+row]*b[column*4+1] + a[8+row]*b[column*4+2] + a[12+row]*b[column*4+3];
    return out;
}

function identityMatrix() { return new Float32Array([1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1]); }
function scaleMatrix(s) { return new Float32Array([s,0,0,0, 0,s,0,0, 0,0,s,0, 0,0,0,1]); }
function normalize(v) { const l=Math.hypot(v[0],v[1],v[2])||1; return [v[0]/l,v[1]/l,v[2]/l]; }
function cross(a,b) { return [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]; }
function dot(a,b) { return a[0]*b[0]+a[1]*b[1]+a[2]*b[2]; }
function clamp(value,min,max) { return Math.max(min, Math.min(max, value)); }

const globeVertexShaderSource = `#version 300 es
precision highp float;
precision highp int;
in vec3 aPosition;
in vec3 aNormal;
uniform mat4 uModel;
uniform mat4 uViewProjection;
uniform float uPlanetRadiusMeters;
uniform float uSeaLevelMeters;
uniform float uSeaIceFraction;
uniform float uLandIceFraction;
uniform float uSnowCoverFraction;
uniform int uMode;
out vec3 vNormal;
out vec3 vPhysicalNormal;
out vec3 vDirection;
out vec3 vWorldPosition;
out float vElevationMeters;
out float vPhysicalSlope;

void main() {
    float physicalRadius = length(aPosition);
    vec3 radial = normalize(aPosition);
    vec3 physicalNormal = normalize(aNormal);
    if (dot(physicalNormal, radial) < 0.0) physicalNormal = -physicalNormal;
    vec3 tangentNormal = physicalNormal - (radial * dot(physicalNormal, radial));
    float elevationMeters = (physicalRadius - 1.0) * uPlanetRadiusMeters;
    float elevationAboveSeaLevel = elevationMeters - uSeaLevelMeters;
    float visualRadius = physicalRadius;

    if (uMode == 0 || uMode == 2) {
        float exaggeration = elevationAboveSeaLevel >= 0.0 ? 36.0 : 4.0;
        visualRadius = 1.0 + ((elevationMeters * exaggeration) / uPlanetRadiusMeters);
    } else if (uMode == 3) {
        visualRadius = 1.0 + ((uSeaLevelMeters + 18.0) / uPlanetRadiusMeters);
    } else if (uMode == 4) {
        float latitude = abs(radial.y);
        float polarSupport = smoothstep(0.40, 0.95, latitude);
        float highlandSupport = smoothstep(900.0, 4200.0, max(elevationAboveSeaLevel, 0.0));
        float seaIceThicknessMeters = (18.0 + (52.0 * polarSupport)) * clamp(uSeaIceFraction, 0.0, 1.0);
        float landSupport = clamp((0.14 + (0.66 * polarSupport) + (0.36 * highlandSupport))
            * max(clamp(uLandIceFraction, 0.0, 1.0), clamp(uSnowCoverFraction, 0.0, 1.0) * 0.55), 0.0, 1.0);
        float landIceThicknessMeters = (90.0 + (760.0 * polarSupport) + (340.0 * highlandSupport)) * landSupport;
        float seaIceVisualElevationMeters = uSeaLevelMeters + 18.0 + (seaIceThicknessMeters * 18.0);
        float landIceVisualElevationMeters = (elevationMeters * 36.0) + (landIceThicknessMeters * 18.0);
        float shorelineBlend = smoothstep(-900.0, 900.0, elevationAboveSeaLevel);
        float iceVisualElevationMeters = mix(seaIceVisualElevationMeters, landIceVisualElevationMeters, shorelineBlend);
        visualRadius = 1.0 + (iceVisualElevationMeters / uPlanetRadiusMeters);
    }

    vec4 world = uModel * vec4(radial * visualRadius, 1.0);
    vDirection = radial;
    vPhysicalNormal = physicalNormal;
    vNormal = normalize(radial + (tangentNormal * 44.0));
    vPhysicalSlope = clamp(1.0 - dot(physicalNormal, radial), 0.0, 0.5);
    vElevationMeters = elevationMeters;
    vWorldPosition = world.xyz;
    gl_Position = uViewProjection * world;
}`;

const globeFragmentShaderSource = `#version 300 es
precision highp float;
precision highp int;
in vec3 vNormal;
in vec3 vPhysicalNormal;
in vec3 vDirection;
in vec3 vWorldPosition;
in float vElevationMeters;
in float vPhysicalSlope;
uniform vec3 uLightDirection;
uniform vec3 uCameraPosition;
uniform float uSeaLevelMeters;
uniform float uAtmosphere;
uniform float uEquilibriumTemperature;
uniform float uSurfaceTemperature;
uniform float uSolarFlux;
uniform float uLiquidFraction;
uniform float uVaporFraction;
uniform float uSeaIceFraction;
uniform float uLandIceFraction;
uniform float uSnowCoverFraction;
uniform int uMode;
out vec4 outColor;

float hash31(vec3 p) {
    p = fract(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}

float valueNoise(vec3 p) {
    vec3 cell = floor(p);
    vec3 local = fract(p);
    vec3 f = local * local * (3.0 - (2.0 * local));
    float n000 = hash31(cell + vec3(0.0, 0.0, 0.0));
    float n100 = hash31(cell + vec3(1.0, 0.0, 0.0));
    float n010 = hash31(cell + vec3(0.0, 1.0, 0.0));
    float n110 = hash31(cell + vec3(1.0, 1.0, 0.0));
    float n001 = hash31(cell + vec3(0.0, 0.0, 1.0));
    float n101 = hash31(cell + vec3(1.0, 0.0, 1.0));
    float n011 = hash31(cell + vec3(0.0, 1.0, 1.0));
    float n111 = hash31(cell + vec3(1.0, 1.0, 1.0));
    float nx00 = mix(n000, n100, f.x);
    float nx10 = mix(n010, n110, f.x);
    float nx01 = mix(n001, n101, f.x);
    float nx11 = mix(n011, n111, f.x);
    return mix(mix(nx00, nx10, f.y), mix(nx01, nx11, f.y), f.z);
}

float fbm(vec3 p) {
    return valueNoise(p) * 0.56
        + valueNoise((p * 2.07) + vec3(4.0, -7.0, 2.0)) * 0.29
        + valueNoise((p * 4.21) + vec3(-3.0, 5.0, 8.0)) * 0.15;
}

vec3 terrainMaterial(vec3 radial, vec3 normal, float elevationAboveSeaLevel, bool preBiological) {
    float elevation = max(elevationAboveSeaLevel, 0.0);
    float slope = smoothstep(0.002, 0.038, vPhysicalSlope);
    float steep = smoothstep(0.014, 0.075, vPhysicalSlope);
    float cliff = smoothstep(0.070, 0.210, vPhysicalSlope);
    float macro = valueNoise((radial * 6.0) + vec3(4.0, -2.0, 7.0));
    float subtleVariation = (macro - 0.5) * 0.045;

    vec3 lowland = preBiological ? vec3(0.36, 0.285, 0.195) : vec3(0.18, 0.38, 0.22);
    vec3 upland = preBiological ? vec3(0.36, 0.31, 0.255) : vec3(0.39, 0.36, 0.22);
    vec3 highland = preBiological ? vec3(0.46, 0.42, 0.36) : vec3(0.62, 0.65, 0.59);
    vec3 ridge = preBiological ? vec3(0.255, 0.255, 0.245) : vec3(0.48, 0.48, 0.43);
    vec3 beach = vec3(0.73, 0.64, 0.46);
    vec3 coastalRock = vec3(0.30, 0.29, 0.27);
    vec3 cliffRock = vec3(0.16, 0.17, 0.17);

    vec3 material = mix(lowland, upland, smoothstep(500.0, 1650.0, elevation));
    material = mix(material, highland, smoothstep(1500.0, 3600.0, elevation));
    material = mix(material, ridge, steep * smoothstep(900.0, 3000.0, elevation) * 0.72);

    float coastalBand = 1.0 - smoothstep(25.0, 780.0, elevation);
    float beachStrength = coastalBand * (1.0 - smoothstep(0.002, 0.020, vPhysicalSlope));
    float rockStrength = coastalBand * slope * (1.0 - cliff * 0.55);
    float cliffStrength = coastalBand * cliff;
    material = mix(material, beach, beachStrength * 0.92);
    material = mix(material, coastalRock, rockStrength * 0.72);
    material = mix(material, cliffRock, cliffStrength * 0.94);
    float wetEdge = (1.0 - smoothstep(0.0, 180.0, elevation)) * (1.0 - smoothstep(0.002, 0.020, vPhysicalSlope));
    material = mix(material, vec3(0.30, 0.27, 0.21), wetEdge * 0.24);
    material *= 1.0 + subtleVariation;

    vec3 lightDirection = normalize(uLightDirection);
    float direct = max(dot(normal, lightDirection), 0.0);
    float radialDirect = max(dot(radial, lightDirection), 0.0);
    float faceDelta = clamp(direct - radialDirect, -0.45, 0.45);
    float illumination = clamp(0.34 + (0.72 * smoothstep(0.0, 0.94, direct)) + (faceDelta * 0.24), 0.28, 1.10);
    illumination *= 1.0 - (steep * 0.08) - (cliff * 0.14);
    return material * illumination;
}

vec4 oceanMaterial(vec3 radial, vec3 normal, float elevationAboveSeaLevel) {
    if (elevationAboveSeaLevel >= 0.0 || uLiquidFraction <= 0.001) discard;

    float depth = max(-elevationAboveSeaLevel, 0.0);
    float shelf = 1.0 - smoothstep(220.0, 2100.0, depth);
    float coast = 1.0 - smoothstep(0.0, 780.0, depth);
    float deepening = smoothstep(1450.0, 3900.0, depth);

    vec3 deepOcean = vec3(0.006, 0.052, 0.115);
    vec3 midOcean = vec3(0.012, 0.125, 0.190);
    vec3 shelfOcean = vec3(0.028, 0.255, 0.315);
    vec3 coastalOcean = vec3(0.070, 0.390, 0.405);

    vec3 color = mix(midOcean, deepOcean, deepening);
    color = mix(color, shelfOcean, shelf * 0.86);
    color = mix(color, coastalOcean, coast * 0.72);

    float broadVariation = valueNoise((radial * 8.0) + vec3(3.0, -4.0, 8.0)) - 0.5;
    float fineVariation = valueNoise((radial * 24.0) + vec3(-2.0, 9.0, 5.0)) - 0.5;
    color += vec3(0.0, broadVariation * 0.008, broadVariation * 0.012);
    color += vec3(0.0, fineVariation * 0.003, fineVariation * 0.005);

    vec3 viewDirection = normalize(uCameraPosition - vWorldPosition);
    vec3 lightDirection = normalize(uLightDirection);
    float diffuse = 0.92 + (0.08 * max(dot(radial, lightDirection), 0.0));
    float fresnel = pow(1.0 - max(dot(radial, viewDirection), 0.0), 3.6);
    vec3 halfVector = normalize(lightDirection + viewDirection);
    float specular = pow(max(dot(radial, halfVector), 0.0), 104.0) * 0.045;

    color *= diffuse;
    color = mix(color, vec3(0.025, 0.085, 0.135), fresnel * 0.08);
    color += vec3(specular * 0.60, specular * 0.76, specular);
    return vec4(clamp(color, 0.0, 1.0), 1.0);
}

vec4 cryosphereMaterial(vec3 radial, vec3 normal, float elevationAboveSeaLevel) {
    bool ocean = elevationAboveSeaLevel < 0.0;
    float elevation = max(elevationAboveSeaLevel, 0.0);
    float latitudeDegrees = degrees(asin(clamp(abs(radial.y), 0.0, 1.0)));
    float latitudeFactor = sin(radians(latitudeDegrees));
    float macro = valueNoise((radial * 5.0) + vec3(4.0, -2.0, 7.0));
    float meso = valueNoise((radial * 11.0) + vec3(-5.0, 4.0, 1.0));
    float edgeNoise = (macro * 0.66) + (meso * 0.34);

    float seaRetreat = pow(1.0 - clamp(uSeaIceFraction, 0.0, 1.0), 0.82);
    float landRetreat = pow(1.0 - clamp(uLandIceFraction, 0.0, 1.0), 0.70);
    float snowRetreat = pow(1.0 - clamp(uSnowCoverFraction, 0.0, 1.0), 0.78);
    float localTemperature = uSurfaceTemperature - (18.0 * pow(latitudeFactor, 1.45)) - (elevation * 0.0065);
    float highland = smoothstep(850.0, 2750.0, elevation);
    float summit = smoothstep(4100.0, 6200.0, elevation);
    float steep = smoothstep(0.010, 0.060, vPhysicalSlope);
    float cliff = smoothstep(0.060, 0.190, vPhysicalSlope);
    float warpedLatitude = latitudeDegrees + ((macro - 0.5) * 16.0) + (ocean ? 0.0 : clamp(elevation / 780.0, 0.0, 7.5));

    float seaIceLine = mix(-25.0, 68.0, seaRetreat);
    float seaSheet = smoothstep(seaIceLine - 1.5, seaIceLine + 5.0, warpedLatitude);
    float seaPatch = valueNoise((radial * 9.0) + vec3(-3.0, 7.0, 4.0));
    float seaFragmented = smoothstep(seaIceLine - 4.0, seaIceLine + 7.5, warpedLatitude)
        * smoothstep(0.56, 0.70, seaPatch + (smoothstep(58.0, 84.0, warpedLatitude) * 0.09));
    float seaCoverage = uSeaIceFraction >= 0.999 ? 1.0 : mix(seaSheet, seaFragmented, smoothstep(0.10, 0.52, seaRetreat));

    float snowline = mix(-1400.0, 4400.0, snowRetreat);
    float localSnowline = snowline + ((0.5 - macro) * 360.0) + ((0.5 - edgeNoise) * 620.0) + (cliff * 480.0);
    float crestSnow = smoothstep(localSnowline - 280.0, localSnowline + 430.0, elevation);
    crestSnow *= 1.0 - smoothstep(264.0, 271.0, localTemperature);
    float polarLand = smoothstep(71.0, 87.0, warpedLatitude);
    float polarPatch = valueNoise((radial * 8.0) + vec3(8.0, -5.0, 2.0));
    float polarRemnant = polarLand * smoothstep(0.48, 0.67, polarPatch + (highland * 0.16) + (steep * 0.08));
    float finalLandSurvival = max(crestSnow, max(polarRemnant, summit * 0.82));
    finalLandSurvival *= 1.0 - (cliff * (1.0 - summit) * 0.62);
    float landIceLine = mix(-25.0, 70.0, landRetreat);
    float broadLandSheet = smoothstep(landIceLine - 2.5, landIceLine + 6.0, warpedLatitude);
    float frozenWorldLand = max(broadLandSheet, smoothstep(-40.0, -8.0, warpedLatitude));
    float landCoverage = (uLandIceFraction >= 0.999 && uSnowCoverFraction >= 0.999)
        ? 1.0
        : mix(frozenWorldLand, finalLandSurvival, smoothstep(0.08, 0.52, landRetreat));

    float coverage = ocean ? smoothstep(0.16, 0.74, seaCoverage) : smoothstep(0.10, 0.80, landCoverage);
    if (coverage < 0.025) discard;

    float iceTexture = (macro * 0.68) + (meso * 0.32);
    vec3 seaIce = mix(vec3(0.48, 0.64, 0.70), vec3(0.79, 0.87, 0.89), 0.47 + (iceTexture * 0.22));
    vec3 landIce = mix(vec3(0.65, 0.69, 0.69), vec3(0.91, 0.92, 0.89), 0.44 + (iceTexture * 0.24));
    vec3 snow = mix(vec3(0.81, 0.83, 0.81), vec3(0.98, 0.97, 0.93), 0.52 + (iceTexture * 0.17));
    bool snowballWorld = uSeaIceFraction >= 0.999 && uLandIceFraction >= 0.999 && uSnowCoverFraction >= 0.999;
    vec3 snowballIce = mix(vec3(0.69, 0.73, 0.73), vec3(0.94, 0.95, 0.92), 0.54 + (iceTexture * 0.18));
    vec3 material = snowballWorld ? snowballIce : (ocean ? seaIce : mix(landIce, snow, clamp(crestSnow * 0.82, 0.0, 0.94)));

    float direct = max(dot(normalize(normal), normalize(uLightDirection)), 0.0);
    material *= clamp(0.62 + (0.42 * direct), 0.52, 1.08);
    float alpha = coverage >= 0.98 ? 1.0 : clamp(smoothstep(0.05, 0.90, coverage) * 0.98, 0.0, 0.98);
    return vec4(material, alpha);
}

void main() {
    vec3 radial = normalize(vDirection);
    vec3 normal = normalize(vNormal);
    float elevationAboveSeaLevel = vElevationMeters - uSeaLevelMeters;

    if (uMode == 1) {
        float rim = pow(1.0 - abs(dot(normalize(vNormal), normalize(-vWorldPosition))), 2.2);
        float fluxGlow = clamp(sqrt(max(uSolarFlux, 1.0) / 1361.0), 0.65, 1.35);
        float steam = clamp(uVaporFraction * 0.7, 0.0, 0.7);
        vec3 atmosphereColor = mix(vec3(0.20, 0.72, 0.72), vec3(0.72, 0.78, 0.72), steam);
        outColor = vec4(atmosphereColor, rim * 0.24 * uAtmosphere * fluxGlow);
        return;
    }

    if (uMode == 3) {
        outColor = oceanMaterial(radial, normal, elevationAboveSeaLevel);
        return;
    }

    if (uMode == 4) {
        outColor = cryosphereMaterial(radial, normal, elevationAboveSeaLevel);
        return;
    }

    bool preBiological = uMode == 2;
    if (elevationAboveSeaLevel < 0.0 && uLiquidFraction > 0.001) {
        discard;
    }

    vec3 baseColor = terrainMaterial(radial, normal, elevationAboveSeaLevel, preBiological);
    float heat = smoothstep(315.0, 430.0, uSurfaceTemperature);
    baseColor = mix(baseColor, vec3(0.48, 0.25, 0.11), heat * 0.52);
    baseColor = mix(baseColor, vec3(0.56, 0.45, 0.31), clamp(uVaporFraction * 0.20, 0.0, 0.20));
    outColor = vec4(clamp(baseColor, 0.0, 1.0), 1.0);
}`;

const localVertexShaderSource = `#version 300 es
precision highp float;
in vec3 aPositionMeters;
in vec3 aNormal;
in float aElevationMeters;
uniform mat4 uViewProjection;
out vec3 vNormal;
out float vElevationMeters;
void main() {
    vNormal = normalize(aNormal);
    vElevationMeters = aElevationMeters;
    gl_Position = uViewProjection * vec4(aPositionMeters, 1.0);
}`;

const localFragmentShaderSource = `#version 300 es
precision highp float;
precision highp int;
in vec3 vNormal;
in float vElevationMeters;
uniform vec3 uLightDirection;
uniform float uSeaLevelMeters;
uniform float uSurfaceTemperature;
uniform float uLiquidFraction;
uniform float uVaporFraction;
uniform int uObjectMode;
out vec4 outColor;
void main() {
    float light = max(dot(normalize(vNormal), normalize(uLightDirection)), 0.0);
    if (uObjectMode == 1) {
        vec3 treeColor = vec3(0.055, 0.30, 0.12);
        outColor = vec4(treeColor * (0.35 + 0.65 * light), 1.0);
        return;
    }

    bool preBiological = uObjectMode == 2;
    vec3 deepOcean = vec3(0.035, 0.16, 0.23);
    vec3 shallowOcean = vec3(0.06, 0.31, 0.36);
    vec3 lowland = preBiological ? vec3(0.34, 0.30, 0.24) : vec3(0.18, 0.38, 0.22);
    vec3 highland = preBiological ? vec3(0.42, 0.36, 0.29) : vec3(0.39, 0.36, 0.22);
    vec3 peak = preBiological ? vec3(0.48, 0.43, 0.37) : vec3(0.62, 0.65, 0.59);
    vec3 baseColor;

    if (uLiquidFraction > 0.001 && vElevationMeters < uSeaLevelMeters - 1500.0) baseColor = deepOcean;
    else if (uLiquidFraction > 0.001 && vElevationMeters < uSeaLevelMeters) baseColor = shallowOcean;
    else if (vElevationMeters < 1200.0) baseColor = lowland;
    else if (vElevationMeters < 3500.0) baseColor = highland;
    else baseColor = peak;

    float heat = smoothstep(315.0, 430.0, uSurfaceTemperature);
    if (vElevationMeters >= uSeaLevelMeters || uLiquidFraction <= 0.001) baseColor = mix(baseColor, vec3(0.48, 0.25, 0.11), heat * 0.76);
    baseColor = mix(baseColor, vec3(0.56, 0.45, 0.31), clamp(uVaporFraction * 0.22, 0.0, 0.22));

    vec3 color = baseColor * (0.18 + 0.82 * light);
    outColor = vec4(color, 1.0);
}`;