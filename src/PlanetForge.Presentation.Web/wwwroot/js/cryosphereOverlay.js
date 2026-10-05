let state;

const verticalFieldOfViewRadians = Math.PI / 4.2;
const minimumCameraAltitudeMeters = 3.0;
const maximumCameraAltitudeRatio = 4.2;
const localTransitionAltitudeMeters = 20_000.0;
const localExitAltitudeMeters = 25_000.0;
const latitudeCoolingKelvin = 18.0;
const elevationLapseRateKelvinPerMeter = 0.0065;
const finalSeaIceLatitudeDegrees = 68.0;
const finalLandIceLatitudeDegrees = 70.0;
const finalAlpineSnowlineMeters = 4_400.0;

export function initialize(overlayCanvasId, inputCanvasId, snapshot) {
    const canvas = document.getElementById(overlayCanvasId);
    const inputCanvas = document.getElementById(inputCanvasId);
    const gl = canvas?.getContext('webgl2', { antialias: true, alpha: true, premultipliedAlpha: false });
    if (!canvas || !inputCanvas || !gl) return;

    const program = createProgram(gl, vertexShaderSource, fragmentShaderSource);
    state = {
        canvas,
        inputCanvas,
        gl,
        program,
        yaw: -0.65,
        pitch: 0.24,
        distance: 3.15,
        dragging: false,
        lastX: 0,
        lastY: 0,
        planetRadiusMeters: 6_371_000.0,
        seaLevelMeters: 0.0,
        surfaceTemperatureKelvin: 250.0,
        seaIceFraction: 1.0,
        landIceFraction: 1.0,
        snowCoverFraction: 1.0,
        visible: true,
        tiles: new Map(),
        dirty: true,
        attributes: {
            position: gl.getAttribLocation(program, 'aPosition')
        },
        uniforms: {
            viewProjection: gl.getUniformLocation(program, 'uViewProjection'),
            planetRadiusMeters: gl.getUniformLocation(program, 'uPlanetRadiusMeters'),
            seaLevelMeters: gl.getUniformLocation(program, 'uSeaLevelMeters'),
            surfaceTemperatureKelvin: gl.getUniformLocation(program, 'uSurfaceTemperatureKelvin'),
            seaIceFraction: gl.getUniformLocation(program, 'uSeaIceFraction'),
            landIceFraction: gl.getUniformLocation(program, 'uLandIceFraction'),
            snowCoverFraction: gl.getUniformLocation(program, 'uSnowCoverFraction')
        }
    };

    installInput(state);
    setPlanet(snapshot);
    installVisualTestApi();
    requestAnimationFrame(render);
}

export function setPlanet(snapshot) {
    if (!state || !snapshot) return;

    state.planetRadiusMeters = snapshot.physicalParameters?.radiusMeters ?? state.planetRadiusMeters;
    state.seaLevelMeters = snapshot.seaLevelMeters ?? state.seaLevelMeters;
    state.surfaceTemperatureKelvin = snapshot.climate?.surfaceTemperatureKelvin ?? state.surfaceTemperatureKelvin;
    state.seaIceFraction = snapshot.climateFeedback?.seaIceFraction ?? snapshot.climateFeedback?.cryosphereFraction ?? state.seaIceFraction;
    state.landIceFraction = snapshot.climateFeedback?.landIceFraction ?? snapshot.climateFeedback?.cryosphereFraction ?? state.landIceFraction;
    state.snowCoverFraction = snapshot.climateFeedback?.snowCoverFraction ?? snapshot.climateFeedback?.cryosphereFraction ?? state.snowCoverFraction;
    state.visible = !snapshot.localSurface;

    const surfaceTiles = snapshot.surfaceTiles ?? [];
    if (surfaceTiles.length > 0) {
        const activeKeys = new Set();
        for (const tile of surfaceTiles) {
            if (!tile?.key || !tile.surfaceVertexCount) continue;

            activeKeys.add(tile.key);
            if (!tile.positions?.length) continue;

            const existing = state.tiles.get(tile.key);
            if (existing) state.gl.deleteBuffer(existing.positionBuffer);
            state.tiles.set(tile.key, createTileBuffer(state.gl, tile));
        }

        for (const [key, tile] of state.tiles) {
            if (activeKeys.has(key)) continue;
            state.gl.deleteBuffer(tile.positionBuffer);
            state.tiles.delete(key);
        }
    }

    state.dirty = true;
}

export function dispose() {
    if (!state) return;
    if (window.__planetForgeCryosphereTest) delete window.__planetForgeCryosphereTest;
    for (const tile of state.tiles.values()) state.gl.deleteBuffer(tile.positionBuffer);
    state.tiles.clear();
    state.gl.deleteProgram(state.program);
    state = null;
}

function createTileBuffer(gl, tile) {
    const positionBuffer = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, positionBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(tile.positions), gl.STATIC_DRAW);
    return { positionBuffer, vertexCount: tile.surfaceVertexCount };
}

function installVisualTestApi() {
    if (!new URLSearchParams(window.location.search).has('visualTest')) return;

    window.__planetForgeCryosphereTest = {
        setPitch(pitch) {
            if (!state) return;
            state.pitch = clamp(pitch, -1.25, 1.25);
            state.dirty = true;
            draw(state);
            state.dirty = false;
        },
        getPitch() {
            return state?.pitch ?? 0.0;
        },
        measureCenterCoverage() {
            if (!state) return 0.0;
            draw(state);
            state.dirty = false;
            return measureCoverage(state, 0.18);
        },
        measurePolarRegionCoverage() {
            if (!state) return 0.0;
            draw(state);
            state.dirty = false;
            return measureCoverage(state, 0.46);
        },
        measureFragmentation() {
            if (!state) return { transitions: 0, occupiedFraction: 0.0 };
            draw(state);
            state.dirty = false;
            return measureFragmentation(state);
        }
    };
}

function measureCoverage(s, fraction) {
    const { gl, canvas } = s;
    const sampleSize = Math.max(24, Math.min(240, Math.floor(Math.min(canvas.width, canvas.height) * fraction)));
    const x = Math.max(0, Math.floor((canvas.width - sampleSize) / 2));
    const y = Math.max(0, Math.floor((canvas.height - sampleSize) / 2));
    const pixels = new Uint8Array(sampleSize * sampleSize * 4);
    gl.readPixels(x, y, sampleSize, sampleSize, gl.RGBA, gl.UNSIGNED_BYTE, pixels);

    let covered = 0;
    for (let offset = 3; offset < pixels.length; offset += 4) {
        if (pixels[offset] >= 70) covered++;
    }

    return covered / (sampleSize * sampleSize);
}

function measureFragmentation(s) {
    const { gl, canvas } = s;
    const width = canvas.width;
    const height = canvas.height;
    const pixels = new Uint8Array(width * height * 4);
    gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);

    const yStart = Math.floor(height * 0.34);
    const yEnd = Math.floor(height * 0.66);
    const xStart = Math.floor(width * 0.25);
    const xEnd = Math.floor(width * 0.75);
    const step = Math.max(2, Math.floor(width / 120));
    let transitions = 0;
    let occupied = 0;
    let sampled = 0;

    for (let y = yStart; y <= yEnd; y += step) {
        let previousCovered = alphaAt(pixels, width, xStart, y) >= 70;
        for (let x = xStart; x <= xEnd; x += step) {
            const covered = alphaAt(pixels, width, x, y) >= 70;
            if (covered) occupied++;
            sampled++;
            if (covered !== previousCovered) transitions++;
            previousCovered = covered;
        }
    }

    return { transitions, occupiedFraction: sampled > 0 ? occupied / sampled : 0.0 };
}

function alphaAt(pixels, width, x, y) {
    return pixels[((y * width) + x) * 4 + 3];
}

function installInput(s) {
    const canvas = s.inputCanvas;
    canvas.addEventListener('pointerdown', event => {
        s.dragging = true;
        s.lastX = event.clientX;
        s.lastY = event.clientY;
    });
    canvas.addEventListener('pointerup', () => s.dragging = false);
    canvas.addEventListener('pointercancel', () => s.dragging = false);
    canvas.addEventListener('pointermove', event => {
        if (!s.dragging) return;
        const deltaX = event.clientX - s.lastX;
        const deltaY = event.clientY - s.lastY;
        s.lastX = event.clientX;
        s.lastY = event.clientY;

        const altitudeMeters = Math.max(0.0, (s.distance - 1.0) * s.planetRadiusMeters);
        if (altitudeMeters <= localExitAltitudeMeters) return;

        s.yaw += deltaX * 0.008;
        s.pitch = clamp(s.pitch + deltaY * 0.008, -1.25, 1.25);
        s.dirty = true;
    });
    canvas.addEventListener('wheel', event => {
        const zoomFactor = Math.exp(event.deltaY * 0.0015);
        const minimumCameraAltitudeRatio = minimumCameraAltitudeMeters / Math.max(s.planetRadiusMeters, 1.0);
        const altitudeRatio = clamp(s.distance - 1.0, minimumCameraAltitudeRatio, maximumCameraAltitudeRatio);
        s.distance = 1.0 + clamp(altitudeRatio * zoomFactor, minimumCameraAltitudeRatio, maximumCameraAltitudeRatio);
        s.dirty = true;
    }, { passive: true });
}

function render() {
    if (!state) return;
    resize(state);
    if (state.dirty) {
        draw(state);
        state.dirty = false;
    }
    requestAnimationFrame(render);
}

function resize(s) {
    const ratio = Math.min(window.devicePixelRatio || 1, 1.5);
    const width = Math.floor(s.canvas.clientWidth * ratio);
    const height = Math.floor(s.canvas.clientHeight * ratio);
    if (s.canvas.width === width && s.canvas.height === height) return;
    s.canvas.width = width;
    s.canvas.height = height;
    s.dirty = true;
}

function draw(s) {
    const { gl, canvas, program } = s;
    gl.viewport(0, 0, canvas.width, canvas.height);
    gl.clearColor(0, 0, 0, 0);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    if (!s.visible) return;

    const altitudeMeters = Math.max(0.0, (s.distance - 1.0) * s.planetRadiusMeters);
    if (altitudeMeters <= localTransitionAltitudeMeters) return;

    const aspect = canvas.width / Math.max(canvas.height, 1);
    const projection = perspective(verticalFieldOfViewRadians, aspect, 0.002, 20.0);
    const eye = orbitEye(s.yaw, s.pitch, s.distance);
    const view = lookAt(eye, [0, 0, 0], [0, 1, 0]);
    const viewProjection = multiply(projection, view);

    gl.enable(gl.DEPTH_TEST);
    gl.enable(gl.CULL_FACE);
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    gl.useProgram(program);
    gl.uniformMatrix4fv(s.uniforms.viewProjection, false, viewProjection);
    gl.uniform1f(s.uniforms.planetRadiusMeters, s.planetRadiusMeters);
    gl.uniform1f(s.uniforms.seaLevelMeters, s.seaLevelMeters);
    gl.uniform1f(s.uniforms.surfaceTemperatureKelvin, s.surfaceTemperatureKelvin);
    gl.uniform1f(s.uniforms.seaIceFraction, s.seaIceFraction);
    gl.uniform1f(s.uniforms.landIceFraction, s.landIceFraction);
    gl.uniform1f(s.uniforms.snowCoverFraction, s.snowCoverFraction);

    for (const tile of s.tiles.values()) {
        gl.bindBuffer(gl.ARRAY_BUFFER, tile.positionBuffer);
        gl.enableVertexAttribArray(s.attributes.position);
        gl.vertexAttribPointer(s.attributes.position, 3, gl.FLOAT, false, 0, 0);
        gl.drawArrays(gl.TRIANGLES, 0, tile.vertexCount);
    }

    gl.disable(gl.BLEND);
}

function createProgram(gl, vertexSource, fragmentSource) {
    const vertex = compile(gl, gl.VERTEX_SHADER, vertexSource);
    const fragment = compile(gl, gl.FRAGMENT_SHADER, fragmentSource);
    const program = gl.createProgram();
    gl.attachShader(program, vertex);
    gl.attachShader(program, fragment);
    gl.linkProgram(program);
    gl.deleteShader(vertex);
    gl.deleteShader(fragment);
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
    for (let column = 0; column < 4; column++) {
        for (let row = 0; row < 4; row++) {
            out[column*4+row] = a[row]*b[column*4] + a[4+row]*b[column*4+1] + a[8+row]*b[column*4+2] + a[12+row]*b[column*4+3];
        }
    }
    return out;
}

function normalize(v) {
    const magnitude = Math.hypot(v[0], v[1], v[2]) || 1.0;
    return [v[0]/magnitude, v[1]/magnitude, v[2]/magnitude];
}

function cross(a,b) { return [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]; }
function dot(a,b) { return a[0]*b[0] + a[1]*b[1] + a[2]*b[2]; }
function clamp(value,min,max) { return Math.max(min, Math.min(max, value)); }

const vertexShaderSource = `#version 300 es
precision highp float;
in vec3 aPosition;
uniform mat4 uViewProjection;
uniform float uPlanetRadiusMeters;
out vec3 vDirection;
out float vElevationMeters;
void main() {
    float radius = length(aPosition);
    vDirection = normalize(aPosition);
    vElevationMeters = (radius - 1.0) * uPlanetRadiusMeters;
    gl_Position = uViewProjection * vec4(aPosition, 1.0);
}`;

const fragmentShaderSource = `#version 300 es
precision highp float;
in vec3 vDirection;
in float vElevationMeters;
uniform float uSeaLevelMeters;
uniform float uSurfaceTemperatureKelvin;
uniform float uSeaIceFraction;
uniform float uLandIceFraction;
uniform float uSnowCoverFraction;
out vec4 outColor;

float hash31(vec3 p) {
    p = fract(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}

float valueNoise(vec3 p) {
    vec3 cell = floor(p);
    vec3 local = fract(p);
    vec3 smoothLocal = local * local * (3.0 - (2.0 * local));

    float n000 = hash31(cell + vec3(0.0, 0.0, 0.0));
    float n100 = hash31(cell + vec3(1.0, 0.0, 0.0));
    float n010 = hash31(cell + vec3(0.0, 1.0, 0.0));
    float n110 = hash31(cell + vec3(1.0, 1.0, 0.0));
    float n001 = hash31(cell + vec3(0.0, 0.0, 1.0));
    float n101 = hash31(cell + vec3(1.0, 0.0, 1.0));
    float n011 = hash31(cell + vec3(0.0, 1.0, 1.0));
    float n111 = hash31(cell + vec3(1.0, 1.0, 1.0));

    float nx00 = mix(n000, n100, smoothLocal.x);
    float nx10 = mix(n010, n110, smoothLocal.x);
    float nx01 = mix(n001, n101, smoothLocal.x);
    float nx11 = mix(n011, n111, smoothLocal.x);
    float nxy0 = mix(nx00, nx10, smoothLocal.y);
    float nxy1 = mix(nx01, nx11, smoothLocal.y);
    return mix(nxy0, nxy1, smoothLocal.z);
}

float broadIceField(vec3 direction) {
    float a = valueNoise((direction * 2.8) + vec3(4.0, -2.0, 7.0));
    float b = valueNoise((direction * 5.6) + vec3(-6.0, 4.0, 1.0));
    float c = valueNoise((direction * 10.5) + vec3(2.0, 7.0, -5.0));
    return (a * 0.56) + (b * 0.30) + (c * 0.14);
}

float patchField(vec3 direction, vec3 offset) {
    float a = valueNoise((direction * 7.5) + offset);
    float b = valueNoise((direction * 15.5) + (offset.yzx * 1.7));
    float c = valueNoise((direction * 29.0) + (offset.zxy * 2.3));
    return (a * 0.50) + (b * 0.34) + (c * 0.16);
}

void main() {
    float latitudeDegrees = degrees(asin(clamp(abs(vDirection.y), 0.0, 1.0)));
    bool ocean = vElevationMeters < uSeaLevelMeters;
    float latitudeFactor = sin(radians(latitudeDegrees));
    float localTemperature = uSurfaceTemperatureKelvin
        - (${latitudeCoolingKelvin.toFixed(1)} * pow(latitudeFactor, 1.45))
        - (max(vElevationMeters, 0.0) * ${elevationLapseRateKelvinPerMeter.toFixed(4)});

    float broad = broadIceField(vDirection);
    float elevationColdBiasDegrees = ocean ? 0.0 : clamp(max(vElevationMeters, 0.0) / 900.0, 0.0, 5.0);
    float warpedLatitudeDegrees = latitudeDegrees + ((broad - 0.5) * 20.0) + elevationColdBiasDegrees;

    float seaRetreat = pow(1.0 - clamp(uSeaIceFraction, 0.0, 1.0), 0.82);
    float landRetreat = pow(1.0 - clamp(uLandIceFraction, 0.0, 1.0), 0.70);
    float snowRetreat = pow(1.0 - clamp(uSnowCoverFraction, 0.0, 1.0), 0.78);
    float seaBreakup = smoothstep(0.10, 0.52, seaRetreat);
    float landBreakup = smoothstep(0.12, 0.58, landRetreat);

    float seaIceLineDegrees = mix(-25.0, ${finalSeaIceLatitudeDegrees.toFixed(1)}, seaRetreat);
    float landIceLineDegrees = mix(-25.0, ${finalLandIceLatitudeDegrees.toFixed(1)}, landRetreat);
    float frozenSeaSheet = smoothstep(seaIceLineDegrees - 1.5, seaIceLineDegrees + 5.0, warpedLatitudeDegrees);
    float frozenLandSheet = smoothstep(landIceLineDegrees - 1.5, landIceLineDegrees + 5.5, warpedLatitudeDegrees);

    float seaPatchNoise = patchField(vDirection, vec3(-3.0, 7.0, 4.0));
    float seaPolarBias = smoothstep(57.0, 84.0, warpedLatitudeDegrees);
    float seaPatchSignal = seaPatchNoise + (seaPolarBias * 0.15);
    float seaPatchMask = smoothstep(0.535, 0.585, seaPatchSignal);
    float seaEdgeEnvelope = smoothstep(seaIceLineDegrees - 3.5, seaIceLineDegrees + 7.0, warpedLatitudeDegrees);
    float fragmentedSea = seaEdgeEnvelope * seaPatchMask;
    float seaCoverage = mix(frozenSeaSheet, fragmentedSea, seaBreakup);

    float landPatchNoise = patchField(vDirection, vec3(8.0, -5.0, 2.0));
    float landPolarBias = smoothstep(58.0, 83.0, warpedLatitudeDegrees);
    float landPatchSignal = landPatchNoise + (landPolarBias * 0.20);
    float landPatchMask = smoothstep(0.505, 0.565, landPatchSignal);
    float landEdgeEnvelope = smoothstep(landIceLineDegrees - 4.5, landIceLineDegrees + 8.0, warpedLatitudeDegrees);
    float fragmentedLand = landEdgeEnvelope * landPatchMask;

    float snowlineMeters = mix(-1200.0, ${finalAlpineSnowlineMeters.toFixed(1)}, snowRetreat);
    float localSnowlineMeters = snowlineMeters + ((0.5 - broad) * 320.0);
    float alpineSnow = ocean ? 0.0 : smoothstep(localSnowlineMeters, localSnowlineMeters + 600.0, vElevationMeters);
    alpineSnow *= 1.0 - smoothstep(264.0, 271.0, localTemperature);

    float landCoverage = mix(frozenLandSheet, max(fragmentedLand, alpineSnow * 0.38), landBreakup);
    float coverage = ocean
        ? smoothstep(0.30, 0.58, seaCoverage)
        : smoothstep(0.24, 0.54, landCoverage);
    if (coverage < 0.08) discard;

    float surfaceTexture = valueNoise((vDirection * 18.0) + vec3(5.0, -3.0, 6.0));
    float detailTexture = valueNoise((vDirection * 42.0) + vec3(-7.0, 4.0, 2.0));
    float visualTexture = mix(0.95, 1.025, (surfaceTexture * 0.72) + (detailTexture * 0.28));
    vec3 frozenSeaColor = vec3(0.88, 0.93, 0.94);
    vec3 meltingSeaColor = vec3(0.76, 0.86, 0.89);
    vec3 frozenLandColor = vec3(0.94, 0.95, 0.93);
    vec3 meltingLandColor = vec3(0.84, 0.86, 0.83);
    vec3 iceColor = ocean
        ? mix(frozenSeaColor, meltingSeaColor, seaBreakup)
        : mix(frozenLandColor, meltingLandColor, landBreakup);
    iceColor *= visualTexture;

    float seaAlpha = mix(0.94, 0.84, seaBreakup) * coverage;
    float landAlpha = mix(0.92, 0.78, landBreakup) * coverage;
    float alpha = clamp(ocean ? seaAlpha : landAlpha, 0.0, 0.94);
    outColor = vec4(iceColor, alpha);
}`;