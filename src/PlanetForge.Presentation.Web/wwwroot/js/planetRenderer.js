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

export function initialize(canvasId, snapshot, dotNetReference) {
    const canvas = document.getElementById(canvasId);
    const gl = canvas?.getContext('webgl2', { antialias: true, alpha: true });
    if (!canvas || !gl) throw new Error('PlanetForge requires WebGL 2.');

    document.title = 'PlanetForge 0.0.3.12 — Transition Hitch Fix';
    state = createState(canvas, gl, dotNetReference);
    installInput(state);
    setPlanet(snapshot);
    requestAnimationFrame(render);
}

export function setPlanet(snapshot) {
    if (!state) return;
    state.seaLevelMeters = snapshot.seaLevelMeters;
    state.planetRadiusMeters = snapshot.physicalParameters.radiusMeters;
    state.atmosphereDensity = snapshot.atmosphereDensity;
    state.equilibriumTemperature = snapshot.physics.equilibriumTemperatureKelvin;
    state.surfaceTemperature = snapshot.climate.surfaceTemperatureKelvin;
    state.solarFlux = snapshot.physics.solarFluxWattsPerSquareMeter;
    state.liquidFraction = snapshot.water.liquidFraction;
    state.vaporFraction = snapshot.water.vaporFraction;

    const geometryKey = `${snapshot.seed}:${snapshot.physicalParameters.radiusMeters}`;
    if (state.geometryKey !== geometryKey) {
        clearSurfaceBufferCache(state);
        clearLocalSurfaceBuffer(state);
        state.geometryKey = geometryKey;
    }

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
        surfaceRequestInFlight: false, surfaceRequestPending: false,
        seaLevelMeters: 0, planetRadiusMeters: 6371000, atmosphereDensity: 0.6,
        equilibriumTemperature: 255, surfaceTemperature: 288, solarFlux: 1361,
        liquidFraction: 1, vaporFraction: 0,
        globeAttributes: {
            position: gl.getAttribLocation(globeProgram, 'aPosition'),
            normal: gl.getAttribLocation(globeProgram, 'aNormal')
        },
        globeUniforms: {
            model: gl.getUniformLocation(globeProgram, 'uModel'),
            viewProjection: gl.getUniformLocation(globeProgram, 'uViewProjection'),
            light: gl.getUniformLocation(globeProgram, 'uLightDirection'),
            seaLevelMeters: gl.getUniformLocation(globeProgram, 'uSeaLevelMeters'),
            planetRadiusMeters: gl.getUniformLocation(globeProgram, 'uPlanetRadiusMeters'),
            atmosphere: gl.getUniformLocation(globeProgram, 'uAtmosphere'),
            equilibriumTemperature: gl.getUniformLocation(globeProgram, 'uEquilibriumTemperature'),
            surfaceTemperature: gl.getUniformLocation(globeProgram, 'uSurfaceTemperature'),
            solarFlux: gl.getUniformLocation(globeProgram, 'uSolarFlux'),
            liquidFraction: gl.getUniformLocation(globeProgram, 'uLiquidFraction'),
            vaporFraction: gl.getUniformLocation(globeProgram, 'uVaporFraction'),
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

function render() {
    if (!state) return;
    resize(state);
    if (state.renderMode === 'local' && state.localSurface) renderLocal(state);
    else renderGlobe(state);
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
    gl.uniform1f(s.globeUniforms.seaLevelMeters, s.seaLevelMeters);
    gl.uniform1f(s.globeUniforms.planetRadiusMeters, s.planetRadiusMeters);
    gl.uniform1f(s.globeUniforms.atmosphere, s.atmosphereDensity);
    gl.uniform1f(s.globeUniforms.equilibriumTemperature, s.equilibriumTemperature);
    gl.uniform1f(s.globeUniforms.surfaceTemperature, s.surfaceTemperature);
    gl.uniform1f(s.globeUniforms.solarFlux, s.solarFlux);
    gl.uniform1f(s.globeUniforms.liquidFraction, s.liquidFraction);
    gl.uniform1f(s.globeUniforms.vaporFraction, s.vaporFraction);
    gl.uniform1i(s.globeUniforms.mode, isPreBiologicalSurface(s) ? 2 : 0);
    drawTerrain(s);

    gl.enable(gl.BLEND);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    gl.disable(gl.CULL_FACE);
    gl.uniform1i(s.globeUniforms.mode, 1);
    gl.uniformMatrix4fv(s.globeUniforms.model, false, scaleMatrix(1.065));
    drawAtmosphere(s);
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

    gl.disable(gl.CULL_FACE);
    for (const tile of s.tiles) {
        if (tile.skirtVertexCount <= 0) continue;
        bindTileAttributes(s, tile);
        gl.drawArrays(gl.TRIANGLES, tile.surfaceVertexCount, tile.skirtVertexCount);
    }
    gl.enable(gl.CULL_FACE);
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
in vec3 aPosition;
in vec3 aNormal;
uniform mat4 uModel;
uniform mat4 uViewProjection;
uniform float uPlanetRadiusMeters;
out vec3 vNormal;
out vec3 vWorldPosition;
out float vElevationMeters;
void main() {
    vec4 world = uModel * vec4(aPosition, 1.0);
    vWorldPosition = world.xyz;
    vNormal = normalize(mat3(uModel) * aNormal);
    vElevationMeters = (length(aPosition) - 1.0) * uPlanetRadiusMeters;
    gl_Position = uViewProjection * world;
}`;

const globeFragmentShaderSource = `#version 300 es
precision highp float;
in vec3 vNormal;
in vec3 vWorldPosition;
in float vElevationMeters;
uniform vec3 uLightDirection;
uniform float uSeaLevelMeters;
uniform float uAtmosphere;
uniform float uEquilibriumTemperature;
uniform float uSurfaceTemperature;
uniform float uSolarFlux;
uniform float uLiquidFraction;
uniform float uVaporFraction;
uniform int uMode;
out vec4 outColor;
void main() {
    if (uMode == 1) {
        float rim = pow(1.0 - abs(dot(normalize(vNormal), normalize(-vWorldPosition))), 2.2);
        float fluxGlow = clamp(sqrt(max(uSolarFlux, 1.0) / 1361.0), 0.65, 1.35);
        float steam = clamp(uVaporFraction * 0.7, 0.0, 0.7);
        vec3 atmosphereColor = mix(vec3(0.20, 0.72, 0.72), vec3(0.72, 0.78, 0.72), steam);
        outColor = vec4(atmosphereColor, rim * 0.24 * uAtmosphere * fluxGlow);
        return;
    }

    bool preBiological = uMode == 2;
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
    baseColor = mix(baseColor, vec3(0.56, 0.45, 0.31), clamp(uVaporFraction * 0.28, 0.0, 0.28));

    float light = max(dot(normalize(vNormal), normalize(uLightDirection)), 0.0);
    float fluxFactor = clamp(sqrt(max(uSolarFlux, 1.0) / 1361.0), 0.45, 1.55);
    float ambient = 0.14 + 0.04 * fluxFactor;
    float terminator = smoothstep(-0.12, 0.18, light);
    vec3 color = baseColor * (ambient + (0.78 + 0.16 * fluxFactor) * terminator);
    outColor = vec4(color, 1.0);
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