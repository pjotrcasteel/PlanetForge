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
const landReliefExaggeration = 36.0;
const oceanReliefExaggeration = 4.0;
const visualNormalExaggeration = 44.0;

export function initialize(overlayCanvasId, inputCanvasId, snapshot) {
    const canvas = document.getElementById(overlayCanvasId);
    const inputCanvas = document.getElementById(inputCanvasId);
    const gl = canvas?.getContext('webgl2', { antialias: true, alpha: true, premultipliedAlpha: false });
    if (!canvas || !inputCanvas || !gl) return;

    const program = createProgram(gl);
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
            position: gl.getAttribLocation(program, 'aPosition'),
            normal: gl.getAttribLocation(program, 'aNormal')
        },
        uniforms: {
            viewProjection: gl.getUniformLocation(program, 'uViewProjection'),
            lightDirection: gl.getUniformLocation(program, 'uLightDirection'),
            cameraPosition: gl.getUniformLocation(program, 'uCameraPosition'),
            planetRadiusMeters: gl.getUniformLocation(program, 'uPlanetRadiusMeters'),
            seaLevelMeters: gl.getUniformLocation(program, 'uSeaLevelMeters'),
            surfaceTemperatureKelvin: gl.getUniformLocation(program, 'uSurfaceTemperatureKelvin'),
            seaIceFraction: gl.getUniformLocation(program, 'uSeaIceFraction'),
            landIceFraction: gl.getUniformLocation(program, 'uLandIceFraction'),
            snowCoverFraction: gl.getUniformLocation(program, 'uSnowCoverFraction'),
            fullSurface: gl.getUniformLocation(program, 'uFullSurface')
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
            if (!tile.positions?.length || !tile.normals?.length) continue;
            const existing = state.tiles.get(tile.key);
            if (existing) deleteTileBuffers(state.gl, existing);
            state.tiles.set(tile.key, createTileBuffer(state.gl, tile));
        }

        for (const [key, tile] of state.tiles) {
            if (activeKeys.has(key)) continue;
            deleteTileBuffers(state.gl, tile);
            state.tiles.delete(key);
        }
    }

    state.dirty = true;
}

export function dispose() {
    if (!state) return;
    if (window.__planetForgeCryosphereTest) delete window.__planetForgeCryosphereTest;
    for (const tile of state.tiles.values()) deleteTileBuffers(state.gl, tile);
    state.tiles.clear();
    state.gl.deleteProgram(state.program);
    state = null;
}

function createTileBuffer(gl, tile) {
    const positionBuffer = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, positionBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(tile.positions), gl.STATIC_DRAW);

    const normalBuffer = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, normalBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(tile.normals), gl.STATIC_DRAW);

    return { positionBuffer, normalBuffer, vertexCount: tile.surfaceVertexCount };
}

function deleteTileBuffers(gl, tile) {
    gl.deleteBuffer(tile.positionBuffer);
    gl.deleteBuffer(tile.normalBuffer);
}

function installVisualTestApi() {
    if (!new URLSearchParams(window.location.search).has('visualTest')) return;
    window.__planetForgeCryosphereTest = {
        setYaw(yaw) {
            if (!state) return;
            state.yaw = yaw;
            state.dirty = true;
            draw(state);
            state.dirty = false;
        },
        getYaw() { return state?.yaw ?? 0.0; },
        setPitch(pitch) {
            if (!state) return;
            state.pitch = clamp(pitch, -1.25, 1.25);
            state.dirty = true;
            draw(state);
            state.dirty = false;
        },
        getPitch() { return state?.pitch ?? 0.0; },
        measureCenterCoverage() {
            if (!state) return 0.0;
            draw(state);
            state.dirty = false;
            return measureIceCoverage(state, 0.18);
        },
        measureCenterTransparency() {
            if (!state) return 0.0;
            draw(state);
            state.dirty = false;
            return measureTransparency(state, 0.48);
        },
        measurePolarRegionCoverage() {
            if (!state) return 0.0;
            draw(state);
            state.dirty = false;
            return measureIceCoverage(state, 0.46);
        },
        measureFragmentation() {
            if (!state) return { transitions: 0, occupiedFraction: 0.0 };
            draw(state);
            state.dirty = false;
            return measureFragmentation(state);
        }
    };
}

function measureIceCoverage(s, fraction) {
    const { gl, canvas } = s;
    const sampleSize = Math.max(24, Math.min(240, Math.floor(Math.min(canvas.width, canvas.height) * fraction)));
    const x = Math.max(0, Math.floor((canvas.width - sampleSize) / 2));
    const y = Math.max(0, Math.floor((canvas.height - sampleSize) / 2));
    const pixels = new Uint8Array(sampleSize * sampleSize * 4);
    gl.readPixels(x, y, sampleSize, sampleSize, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
    let covered = 0;
    for (let offset = 0; offset < pixels.length; offset += 4) if (isIcePixel(pixels, offset)) covered++;
    return covered / (sampleSize * sampleSize);
}

function measureTransparency(s, fraction) {
    const { gl, canvas } = s;
    const sampleSize = Math.max(32, Math.min(300, Math.floor(Math.min(canvas.width, canvas.height) * fraction)));
    const x = Math.max(0, Math.floor((canvas.width - sampleSize) / 2));
    const y = Math.max(0, Math.floor((canvas.height - sampleSize) / 2));
    const pixels = new Uint8Array(sampleSize * sampleSize * 4);
    gl.readPixels(x, y, sampleSize, sampleSize, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
    let transparent = 0;
    for (let offset = 3; offset < pixels.length; offset += 4) if (pixels[offset] < 24) transparent++;
    return transparent / (sampleSize * sampleSize);
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
        let previousCovered = isIcePixelAt(pixels, width, xStart, y);
        for (let x = xStart; x <= xEnd; x += step) {
            const covered = isIcePixelAt(pixels, width, x, y);
            if (covered) occupied++;
            sampled++;
            if (covered !== previousCovered) transitions++;
            previousCovered = covered;
        }
    }

    return { transitions, occupiedFraction: sampled > 0 ? occupied / sampled : 0.0 };
}

function isIcePixelAt(pixels, width, x, y) { return isIcePixel(pixels, ((y * width) + x) * 4); }

function isIcePixel(pixels, offset) {
    if (pixels[offset + 3] < 70) return false;
    const red = pixels[offset] / 255.0;
    const green = pixels[offset + 1] / 255.0;
    const blue = pixels[offset + 2] / 255.0;
    const luminance = (red * 0.2126) + (green * 0.7152) + (blue * 0.0722);
    return luminance > 0.40 && green >= red * 0.80 && blue >= red * 0.80;
}

function installInput(s) {
    const canvas = s.inputCanvas;
    canvas.addEventListener('pointerdown', event => { s.dragging = true; s.lastX = event.clientX; s.lastY = event.clientY; });
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
    const fullSurface = s.inputCanvas.closest('.planet-stage')?.classList.contains('pre-vegetation-world') ?? false;

    gl.enable(gl.DEPTH_TEST);
    gl.enable(gl.CULL_FACE);
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    gl.useProgram(program);
    gl.uniformMatrix4fv(s.uniforms.viewProjection, false, viewProjection);
    gl.uniform3f(s.uniforms.lightDirection, 0.72, 0.42, 0.55);
    gl.uniform3f(s.uniforms.cameraPosition, eye[0], eye[1], eye[2]);
    gl.uniform1f(s.uniforms.planetRadiusMeters, s.planetRadiusMeters);
    gl.uniform1f(s.uniforms.seaLevelMeters, s.seaLevelMeters);
    gl.uniform1f(s.uniforms.surfaceTemperatureKelvin, s.surfaceTemperatureKelvin);
    gl.uniform1f(s.uniforms.seaIceFraction, s.seaIceFraction);
    gl.uniform1f(s.uniforms.landIceFraction, s.landIceFraction);
    gl.uniform1f(s.uniforms.snowCoverFraction, s.snowCoverFraction);
    gl.uniform1i(s.uniforms.fullSurface, fullSurface ? 1 : 0);

    for (const tile of s.tiles.values()) {
        gl.bindBuffer(gl.ARRAY_BUFFER, tile.positionBuffer);
        gl.enableVertexAttribArray(s.attributes.position);
        gl.vertexAttribPointer(s.attributes.position, 3, gl.FLOAT, false, 0, 0);
        gl.bindBuffer(gl.ARRAY_BUFFER, tile.normalBuffer);
        gl.enableVertexAttribArray(s.attributes.normal);
        gl.vertexAttribPointer(s.attributes.normal, 3, gl.FLOAT, false, 0, 0);
        gl.drawArrays(gl.TRIANGLES, 0, tile.vertexCount);
    }

    gl.disable(gl.BLEND);
}

function createProgram(gl) {
    const vertex = compile(gl, gl.VERTEX_SHADER, vertexShaderSource);
    const fragment = compile(gl, gl.FRAGMENT_SHADER, fragmentShaderSource);
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
in vec3 aNormal;
uniform mat4 uViewProjection;
uniform float uPlanetRadiusMeters;
uniform float uSeaLevelMeters;
uniform float uSeaIceFraction;
uniform float uLandIceFraction;
uniform float uSnowCoverFraction;
out vec3 vDirection;
out vec3 vVisualNormal;
out float vPhysicalSlope;
out float vElevationMeters;
void main() {
    float physicalRadius = length(aPosition);
    vec3 radial = normalize(aPosition);
    vec3 physicalNormal = normalize(aNormal);
    if (dot(physicalNormal, radial) < 0.0) physicalNormal = -physicalNormal;
    vec3 tangentNormal = physicalNormal - (radial * dot(physicalNormal, radial));
    vDirection = radial;
    vVisualNormal = normalize(radial + (tangentNormal * ${visualNormalExaggeration.toFixed(1)}));
    vPhysicalSlope = clamp(1.0 - dot(physicalNormal, radial), 0.0, 0.5);
    vElevationMeters = (physicalRadius - 1.0) * uPlanetRadiusMeters;
    float elevationAboveSeaLevel = vElevationMeters - uSeaLevelMeters;
    float exaggeration = elevationAboveSeaLevel >= 0.0 ? ${landReliefExaggeration.toFixed(1)} : ${oceanReliefExaggeration.toFixed(1)};
    float latitude = abs(radial.y);
    float polarSupport = smoothstep(0.40, 0.95, latitude);
    float highlandSupport = smoothstep(900.0, 4200.0, max(elevationAboveSeaLevel, 0.0));
    float seaIce = clamp(uSeaIceFraction, 0.0, 1.0);
    float landIce = clamp(uLandIceFraction, 0.0, 1.0);
    float snow = clamp(uSnowCoverFraction, 0.0, 1.0);
    float seaIceThicknessMeters = (18.0 + (52.0 * polarSupport)) * seaIce;
    float landSupport = clamp((0.14 + (0.66 * polarSupport) + (0.36 * highlandSupport)) * max(landIce, snow * 0.55), 0.0, 1.0);
    float landIceThicknessMeters = (90.0 + (760.0 * polarSupport) + (340.0 * highlandSupport)) * landSupport;
    float iceThicknessMeters = elevationAboveSeaLevel < 0.0 ? seaIceThicknessMeters : landIceThicknessMeters;
    float visualRadius = 1.0 + (((vElevationMeters * exaggeration) + (iceThicknessMeters * 18.0)) / uPlanetRadiusMeters);
    gl_Position = uViewProjection * vec4(radial * visualRadius, 1.0);
}`;

const fragmentShaderSource = `#version 300 es
precision highp float;
in vec3 vDirection;
in vec3 vVisualNormal;
in float vPhysicalSlope;
in float vElevationMeters;
uniform vec3 uLightDirection;
uniform vec3 uCameraPosition;
uniform float uSurfaceTemperatureKelvin;
uniform float uSeaLevelMeters;
uniform float uSeaIceFraction;
uniform float uLandIceFraction;
uniform float uSnowCoverFraction;
uniform int uFullSurface;
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
    return valueNoise(p) * 0.56 + valueNoise((p * 2.07) + vec3(4.0, -7.0, 2.0)) * 0.29 + valueNoise((p * 4.21) + vec3(-3.0, 5.0, 8.0)) * 0.15;
}

void main() {
    vec3 radial = normalize(vDirection);
    float horizonVisibility = dot(radial, uCameraPosition);
    if (horizonVisibility <= 1.002) discard;
    vec3 normal = normalize(vVisualNormal);
    vec3 light = normalize(uLightDirection);
    float elevationAboveSeaLevel = vElevationMeters - uSeaLevelMeters;
    bool ocean = elevationAboveSeaLevel < 0.0;
    float elevation = max(elevationAboveSeaLevel, 0.0);
    float latitudeDegrees = degrees(asin(clamp(abs(radial.y), 0.0, 1.0)));
    float latitudeFactor = sin(radians(latitudeDegrees));
    float slope = smoothstep(0.002, 0.036, vPhysicalSlope);
    float steep = smoothstep(0.010, 0.054, vPhysicalSlope);
    float cliff = smoothstep(0.025, 0.082, vPhysicalSlope);

    float macro = fbm((radial * 5.0) + vec3(4.0, -2.0, 7.0));
    float meso = fbm((radial * 13.0) + vec3(-6.0, 3.0, 1.0));
    float detail = valueNoise((radial * 34.0) + vec3(2.0, 7.0, -5.0));
    float edgeNoise = (macro * 0.52) + (meso * 0.34) + (detail * 0.14);
    float contourSignal = 1.0 - clamp(abs(((meso * 0.62) + (macro * 0.38)) - 0.5) * 3.8, 0.0, 1.0);
    float corridor = smoothstep(0.58, 0.92, contourSignal);

    float localTemperature = uSurfaceTemperatureKelvin - (${latitudeCoolingKelvin.toFixed(1)} * pow(latitudeFactor, 1.45)) - (elevation * ${elevationLapseRateKelvinPerMeter.toFixed(4)});
    float elevationColdBiasDegrees = ocean ? 0.0 : clamp(elevation / 780.0, 0.0, 7.5);
    float warpedLatitudeDegrees = latitudeDegrees + ((macro - 0.5) * 16.0) + elevationColdBiasDegrees;

    float seaRetreat = pow(1.0 - clamp(uSeaIceFraction, 0.0, 1.0), 0.82);
    float landRetreat = pow(1.0 - clamp(uLandIceFraction, 0.0, 1.0), 0.70);
    float snowRetreat = pow(1.0 - clamp(uSnowCoverFraction, 0.0, 1.0), 0.78);
    float seaBreakup = smoothstep(0.10, 0.52, seaRetreat);
    float landBreakup = smoothstep(0.08, 0.52, landRetreat);

    float seaIceLineDegrees = mix(-25.0, ${finalSeaIceLatitudeDegrees.toFixed(1)}, seaRetreat);
    float frozenSeaSheet = smoothstep(seaIceLineDegrees - 1.5, seaIceLineDegrees + 5.0, warpedLatitudeDegrees);
    float seaPatch = fbm((radial * 9.0) + vec3(-3.0, 7.0, 4.0));
    float polarSeaBias = smoothstep(58.0, 84.0, warpedLatitudeDegrees);
    float fragmentedSea = smoothstep(seaIceLineDegrees - 4.0, seaIceLineDegrees + 7.5, warpedLatitudeDegrees)
        * smoothstep(0.56, 0.70, seaPatch + (polarSeaBias * 0.09));
    float seaCoverage = mix(frozenSeaSheet, fragmentedSea, seaBreakup);

    float highland = smoothstep(850.0, 2750.0, elevation);
    float alpine = smoothstep(2450.0, 4300.0, elevation);
    float summit = smoothstep(4100.0, 6200.0, elevation);
    float ridgeStrength = steep * smoothstep(1200.0, 3600.0, elevation);
    float exposedRidge = cliff * smoothstep(1500.0, 4300.0, elevation);
    float valleyShelter = (1.0 - cliff) * (1.0 - (steep * 0.38)) * smoothstep(950.0, 3200.0, elevation);
    float shoulder = (1.0 - cliff) * smoothstep(1800.0, 3800.0, elevation);

    float snowlineMeters = mix(-1400.0, ${finalAlpineSnowlineMeters.toFixed(1)}, snowRetreat);
    float localSnowline = snowlineMeters + ((0.5 - macro) * 360.0) + ((0.5 - edgeNoise) * 620.0) + (exposedRidge * 680.0) - (valleyShelter * 390.0);
    float crestSnow = smoothstep(localSnowline - 280.0, localSnowline + 430.0, elevation);
    crestSnow *= 1.0 - smoothstep(264.0, 271.0, localTemperature);
    crestSnow *= mix(1.0, 0.52, exposedRidge);
    crestSnow *= mix(0.76, 1.0, smoothstep(0.28, 0.68, edgeNoise + (alpine * 0.12)));

    float tongueElevation = smoothstep(localSnowline - 1900.0, localSnowline - 420.0, elevation);
    float tongueSource = smoothstep(0.24, 0.82, (highland * 0.74) + (ridgeStrength * 0.26));
    float glacierTongue = corridor * valleyShelter * tongueElevation * tongueSource;
    glacierTongue *= 1.0 - smoothstep(266.0, 273.0, localTemperature);
    glacierTongue *= smoothstep(0.18, 0.62, landBreakup);

    float snowSystem = max(crestSnow, glacierTongue * 0.92);
    float polarLand = smoothstep(71.0, 87.0, warpedLatitudeDegrees);
    float polarPatch = fbm((radial * 8.0) + vec3(8.0, -5.0, 2.0));
    float polarRemnant = polarLand * smoothstep(0.48, 0.67, polarPatch + (highland * 0.16) + (ridgeStrength * 0.08));

    float finalLandSurvival = max(snowSystem, max(polarRemnant, summit * 0.82));
    finalLandSurvival = max(finalLandSurvival, shoulder * highland * smoothstep(0.54, 0.73, edgeNoise) * 0.72);
    finalLandSurvival *= 1.0 - (cliff * (1.0 - summit) * 0.72);

    float landIceLineDegrees = mix(-25.0, ${finalLandIceLatitudeDegrees.toFixed(1)}, landRetreat);
    float broadLandSheet = smoothstep(landIceLineDegrees - 2.5, landIceLineDegrees + 6.0, warpedLatitudeDegrees);
    float frozenWorldLand = max(broadLandSheet, smoothstep(-40.0, -8.0, warpedLatitudeDegrees));
    float retreatDrivenLand = mix(frozenWorldLand, finalLandSurvival, landBreakup);
    float lowlandMelt = landBreakup * (1.0 - smoothstep(550.0, 1850.0, elevation)) * (1.0 - polarLand);
    float landCoverage = clamp(retreatDrivenLand - (lowlandMelt * 0.88), 0.0, 1.0);
    landCoverage = max(landCoverage, glacierTongue * 0.88);

    float coverage = ocean ? smoothstep(0.16, 0.74, seaCoverage) : smoothstep(0.10, 0.80, landCoverage);

    float direct = max(dot(normal, light), 0.0);
    float radialDirect = max(dot(radial, light), 0.0);
    float slopeDelta = clamp(direct - radialDirect, -0.48, 0.48);
    float hillshade = 0.12 + (0.88 * smoothstep(0.0, 0.92, direct));
    float faceShadow = 1.0 - (max(-slopeDelta, 0.0) * 0.58) - (cliff * 0.16);
    float illumination = clamp((0.36 + (0.72 * hillshade) + (slopeDelta * 0.28)) * faceShadow, 0.28, 1.12);

    float normalizedDepth = clamp(max(-elevationAboveSeaLevel, 0.0) / 6000.0, 0.0, 1.0);
    float lowland = 1.0 - smoothstep(420.0, 1150.0, elevation);
    float upland = smoothstep(380.0, 1450.0, elevation);
    float high = smoothstep(1250.0, 2700.0, elevation);
    float rockyAlpine = smoothstep(2800.0, 4700.0, elevation);
    float rockNoise = ((macro - 0.5) * 0.060) + ((meso - 0.5) * 0.035) + ((detail - 0.5) * 0.015);

    vec3 deepOcean = vec3(0.025, 0.13, 0.19);
    vec3 shallowOcean = vec3(0.045, 0.29, 0.34);
    vec3 basinRock = vec3(0.23, 0.19, 0.15);
    vec3 lowRock = vec3(0.33, 0.25, 0.18);
    vec3 uplandRock = vec3(0.44, 0.34, 0.23);
    vec3 highRock = vec3(0.50, 0.43, 0.34);
    vec3 ridgeRock = vec3(0.29, 0.29, 0.28);
    vec3 alpineRock = vec3(0.58, 0.54, 0.47);
    vec3 summitRock = vec3(0.69, 0.67, 0.62);

    vec3 oceanMaterial = mix(shallowOcean, deepOcean, normalizedDepth);
    vec3 landMaterial = mix(basinRock, lowRock, smoothstep(0.0, 800.0, elevation));
    landMaterial = mix(landMaterial, uplandRock, upland);
    landMaterial = mix(landMaterial, highRock, high);
    landMaterial = mix(landMaterial, ridgeRock, ridgeStrength * 0.86);
    landMaterial = mix(landMaterial, alpineRock, rockyAlpine);
    landMaterial = mix(landMaterial, summitRock, summit);
    landMaterial = mix(landMaterial, vec3(0.27, 0.27, 0.26), exposedRidge * 0.66);
    landMaterial = mix(landMaterial, vec3(0.40, 0.30, 0.20), lowland * (1.0 - slope) * smoothstep(0.34, 0.72, macro) * 0.10);
    landMaterial *= 1.0 + rockNoise;
    float elevationContrast = 0.92 + (smoothstep(600.0, 4200.0, elevation) * 0.12);
    float ruggedShadow = 1.0 - (steep * 0.10) - (cliff * 0.14);
    vec3 terrainMaterial = ocean ? oceanMaterial * illumination : landMaterial * illumination * elevationContrast * ruggedShadow;

    float iceMacro = fbm((radial * 4.6) + vec3(-4.0, 2.0, 9.0));
    float iceFlow = fbm((radial * 11.5) + vec3(7.0, -6.0, 1.0));
    float iceTexture = (iceMacro * 0.68) + (iceFlow * 0.32);
    vec3 seaIce = mix(vec3(0.48, 0.64, 0.70), vec3(0.79, 0.87, 0.89), 0.47 + (iceTexture * 0.22));
    vec3 landIce = mix(vec3(0.65, 0.69, 0.69), vec3(0.91, 0.92, 0.89), 0.44 + (iceTexture * 0.24));
    vec3 snow = mix(vec3(0.81, 0.83, 0.81), vec3(0.98, 0.97, 0.93), 0.52 + (iceTexture * 0.17));
    float snowInfluence = ocean ? 0.0 : clamp((crestSnow * 0.76) + (glacierTongue * 0.64) + (valleyShelter * highland * 0.10), 0.0, 0.94);
    vec3 iceMaterial = ocean ? seaIce : mix(landIce, snow, snowInfluence);
    float iceLighting = clamp(0.64 + (0.42 * hillshade) + (slopeDelta * 0.18), 0.50, 1.08);
    iceMaterial *= iceLighting;
    iceMaterial *= mix(0.975, 1.025, iceTexture);

    float frozenStrength = min(min(clamp(uSeaIceFraction, 0.0, 1.0), clamp(uLandIceFraction, 0.0, 1.0)), clamp(uSnowCoverFraction, 0.0, 1.0));
    float frozenRockExposure = ocean ? 0.0 : frozenStrength * exposedRidge * smoothstep(1500.0, 4700.0, elevation) * 0.48;
    float meltRockExposure = ocean ? 0.0 : landBreakup * exposedRidge * smoothstep(1200.0, 4300.0, elevation) * 0.72;
    iceMaterial = mix(iceMaterial, terrainMaterial * 1.08, clamp(frozenRockExposure + meltRockExposure, 0.0, 0.76));

    float iceBlend = smoothstep(0.08, 0.88, coverage);
    if (ocean) {
        if (coverage < 0.05) discard;
        outColor = vec4(iceMaterial, clamp(iceBlend * 0.98, 0.0, 0.98));
        return;
    }

    if (coverage < 0.05) {
        if (uFullSurface == 0) discard;
        outColor = vec4(terrainMaterial, 1.0);
        return;
    }

    vec3 material = uFullSurface == 1 ? mix(terrainMaterial, iceMaterial, iceBlend) : iceMaterial;
    float alpha = uFullSurface == 1 ? 1.0 : clamp(coverage, 0.0, 0.98);
    outColor = vec4(material, alpha);
}`;