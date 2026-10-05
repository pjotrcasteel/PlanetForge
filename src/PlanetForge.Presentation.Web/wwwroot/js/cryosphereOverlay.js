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
const landReliefExaggeration = 22.0;
const oceanReliefExaggeration = 4.0;
const visualNormalExaggeration = 20.0;

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
            position: gl.getAttribLocation(program, 'aPosition'),
            normal: gl.getAttribLocation(program, 'aNormal')
        },
        uniforms: {
            viewProjection: gl.getUniformLocation(program, 'uViewProjection'),
            lightDirection: gl.getUniformLocation(program, 'uLightDirection'),
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
in vec3 aNormal;
uniform mat4 uViewProjection;
uniform float uPlanetRadiusMeters;
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
    float exaggeration = vElevationMeters >= 0.0 ? ${landReliefExaggeration.toFixed(1)} : ${oceanReliefExaggeration.toFixed(1)};
    float visualRadius = 1.0 + ((vElevationMeters * exaggeration) / uPlanetRadiusMeters);
    gl_Position = uViewProjection * vec4(radial * visualRadius, 1.0);
}`;

const fragmentShaderSource = `#version 300 es
precision highp float;
in vec3 vDirection;
in vec3 vVisualNormal;
in float vPhysicalSlope;
in float vElevationMeters;
uniform vec3 uLightDirection;
uniform float uSeaLevelMeters;
uniform float uSurfaceTemperatureKelvin;
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

float broadField(vec3 direction) {
    float a = valueNoise((direction * 2.6) + vec3(4.0, -2.0, 7.0));
    float b = valueNoise((direction * 5.2) + vec3(-6.0, 4.0, 1.0));
    float c = valueNoise((direction * 9.5) + vec3(2.0, 7.0, -5.0));
    return (a * 0.58) + (b * 0.29) + (c * 0.13);
}

float breakupField(vec3 direction, vec3 offset) {
    float a = valueNoise((direction * 7.0) + offset);
    float b = valueNoise((direction * 15.0) + (offset.yzx * 1.7));
    return (a * 0.68) + (b * 0.32);
}

void main() {
    float latitudeDegrees = degrees(asin(clamp(abs(vDirection.y), 0.0, 1.0)));
    bool ocean = vElevationMeters < 0.0;
    float latitudeFactor = sin(radians(latitudeDegrees));
    float elevation = max(vElevationMeters, 0.0);
    float localTemperature = uSurfaceTemperatureKelvin - (${latitudeCoolingKelvin.toFixed(1)} * pow(latitudeFactor, 1.45)) - (elevation * ${elevationLapseRateKelvinPerMeter.toFixed(4)});
    float broad = broadField(vDirection);
    float elevationColdBiasDegrees = ocean ? 0.0 : clamp(elevation / 780.0, 0.0, 7.0);
    float warpedLatitudeDegrees = latitudeDegrees + ((broad - 0.5) * 17.0) + elevationColdBiasDegrees;
    float seaRetreat = pow(1.0 - clamp(uSeaIceFraction, 0.0, 1.0), 0.82);
    float landRetreat = pow(1.0 - clamp(uLandIceFraction, 0.0, 1.0), 0.70);
    float snowRetreat = pow(1.0 - clamp(uSnowCoverFraction, 0.0, 1.0), 0.78);
    float seaBreakup = smoothstep(0.10, 0.52, seaRetreat);
    float landBreakup = smoothstep(0.12, 0.58, landRetreat);
    float seaIceLineDegrees = mix(-25.0, ${finalSeaIceLatitudeDegrees.toFixed(1)}, seaRetreat);
    float landIceLineDegrees = mix(-25.0, ${finalLandIceLatitudeDegrees.toFixed(1)}, landRetreat);
    float frozenSeaSheet = smoothstep(seaIceLineDegrees - 1.5, seaIceLineDegrees + 5.0, warpedLatitudeDegrees);
    float frozenLandSheet = smoothstep(landIceLineDegrees - 1.5, landIceLineDegrees + 5.5, warpedLatitudeDegrees);

    float seaPatchNoise = breakupField(vDirection, vec3(-3.0, 7.0, 4.0));
    float seaPolarBias = smoothstep(57.0, 84.0, warpedLatitudeDegrees);
    float fragmentedSea = smoothstep(seaIceLineDegrees - 3.5, seaIceLineDegrees + 7.0, warpedLatitudeDegrees)
        * smoothstep(0.55, 0.64, seaPatchNoise + (seaPolarBias * 0.10));
    float seaCoverage = mix(frozenSeaSheet, fragmentedSea, seaBreakup);

    float landPatchNoise = breakupField(vDirection, vec3(8.0, -5.0, 2.0));
    float landPolarBias = smoothstep(58.0, 83.0, warpedLatitudeDegrees);
    float terrainRetention = 1.0 - smoothstep(0.10, 0.24, vPhysicalSlope);
    float ruggedRetention = smoothstep(1100.0, 3400.0, elevation) * (1.0 - smoothstep(0.20, 0.34, vPhysicalSlope));
    float fragmentedLand = smoothstep(landIceLineDegrees - 4.5, landIceLineDegrees + 8.0, warpedLatitudeDegrees)
        * smoothstep(0.58, 0.69, landPatchNoise + (landPolarBias * 0.08) + (ruggedRetention * 0.08));

    float snowlineMeters = mix(-1200.0, ${finalAlpineSnowlineMeters.toFixed(1)}, snowRetreat);
    float localSnowlineMeters = snowlineMeters + ((0.5 - broad) * 360.0) - (ruggedRetention * 420.0);
    float alpineSnow = ocean ? 0.0 : smoothstep(localSnowlineMeters, localSnowlineMeters + 700.0, elevation);
    alpineSnow *= 1.0 - smoothstep(264.0, 271.0, localTemperature);
    alpineSnow *= mix(0.58, 1.0, terrainRetention);

    float residualLandSheet = frozenLandSheet * (1.0 - landBreakup);
    float mountainIce = max(alpineSnow, ruggedRetention * clamp(uSnowCoverFraction, 0.0, 1.0) * 0.58);
    float brokenLandCoverage = max(fragmentedLand, mountainIce) * landBreakup;
    float landCoverage = max(residualLandSheet, brokenLandCoverage);
    float coverage = ocean ? smoothstep(0.30, 0.58, seaCoverage) : smoothstep(0.38, 0.67, landCoverage);

    vec3 normal = normalize(vVisualNormal);
    vec3 radial = normalize(vDirection);
    float direct = max(dot(normal, normalize(uLightDirection)), 0.0);
    float hillshade = 0.38 + (0.62 * smoothstep(0.0, 0.90, direct));
    float reliefShadow = 1.0 - (clamp(1.0 - dot(normal, radial), 0.0, 0.78) * 0.48);
    float illumination = (0.62 + (0.38 * hillshade)) * reliefShadow;

    float normalizedDepth = clamp(max(-vElevationMeters, 0.0) / 6000.0, 0.0, 1.0);
    float upland = smoothstep(450.0, 1500.0, elevation);
    float highland = smoothstep(1500.0, 3000.0, elevation);
    float alpine = smoothstep(3000.0, 4800.0, elevation);
    float summit = smoothstep(4800.0, 6800.0, elevation);
    float rockVariation = (valueNoise((vDirection * 18.0) + vec3(5.0, -3.0, 6.0)) - 0.5) * 0.08;
    vec3 deepOcean = vec3(0.025, 0.13, 0.19);
    vec3 shallowOcean = vec3(0.045, 0.29, 0.34);
    vec3 lowRock = vec3(0.25, 0.21, 0.17);
    vec3 uplandRock = vec3(0.39, 0.32, 0.24);
    vec3 highRock = vec3(0.53, 0.44, 0.33);
    vec3 alpineRock = vec3(0.64, 0.57, 0.47);
    vec3 summitRock = vec3(0.72, 0.68, 0.60);
    vec3 oceanMaterial = mix(shallowOcean, deepOcean, normalizedDepth);
    vec3 landMaterial = mix(lowRock, uplandRock, upland);
    landMaterial = mix(landMaterial, highRock, highland);
    landMaterial = mix(landMaterial, alpineRock, alpine);
    landMaterial = mix(landMaterial, summitRock, summit);
    landMaterial *= 1.0 + rockVariation;
    vec3 terrainMaterial = (ocean ? oceanMaterial : landMaterial) * illumination;

    float glacierMacro = valueNoise((vDirection * 6.5) + vec3(-4.0, 2.0, 9.0));
    float glacierFlow = valueNoise((vDirection * 16.0) + vec3(7.0, -6.0, 1.0));
    float glacierFine = valueNoise((vDirection * 42.0) + vec3(-8.0, 3.0, -5.0));
    float glacierTexture = (glacierMacro * 0.50) + (glacierFlow * 0.34) + (glacierFine * 0.16);
    float sparseFracture = 1.0 - smoothstep(0.018, 0.070, abs(valueNoise((vDirection * 54.0) + vec3(9.0, 2.0, -4.0)) - 0.5));
    vec3 seaIce = mix(vec3(0.48, 0.64, 0.70), vec3(0.78, 0.87, 0.89), 0.50 + (glacierTexture * 0.22));
    vec3 landIce = mix(vec3(0.66, 0.70, 0.70), vec3(0.91, 0.92, 0.89), 0.48 + (glacierTexture * 0.24));
    vec3 snow = mix(vec3(0.80, 0.82, 0.80), vec3(0.98, 0.97, 0.93), 0.55 + (glacierTexture * 0.18));
    float snowInfluence = ocean ? 0.0 : clamp((alpineSnow * 0.80) + (ruggedRetention * 0.18), 0.0, 0.88);
    vec3 iceMaterial = ocean ? seaIce : mix(landIce, snow, snowInfluence);
    iceMaterial *= 0.78 + (0.30 * hillshade);
    iceMaterial *= mix(0.96, 1.035, glacierTexture);
    iceMaterial *= 1.0 - (sparseFracture * (ocean ? 0.055 : 0.025) * mix(0.25, 1.0, seaBreakup));

    float frozenWorldStrength = min(min(clamp(uSeaIceFraction, 0.0, 1.0), clamp(uLandIceFraction, 0.0, 1.0)), clamp(uSnowCoverFraction, 0.0, 1.0));
    float windScour = ocean ? 0.0 : frozenWorldStrength * smoothstep(0.06, 0.20, vPhysicalSlope) * smoothstep(1800.0, 5000.0, elevation);
    iceMaterial = mix(iceMaterial, terrainMaterial * 1.12, windScour * 0.34);

    if (coverage < 0.08) {
        if (uFullSurface == 0) discard;
        outColor = vec4(terrainMaterial, 1.0);
        return;
    }

    float iceBlend = smoothstep(0.08, 0.64, coverage);
    vec3 material = uFullSurface == 1 ? mix(terrainMaterial, iceMaterial, iceBlend) : iceMaterial;
    float alpha = uFullSurface == 1 ? 1.0 : clamp(coverage, 0.0, 0.98);
    outColor = vec4(material, alpha);
}`;