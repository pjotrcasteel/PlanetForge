let state;

const verticalFieldOfViewRadians = Math.PI / 4.2;
const minimumCameraAltitudeMeters = 3.0;
const maximumCameraAltitudeRatio = 4.2;
const localTransitionAltitudeMeters = 20_000.0;
const localExitAltitudeMeters = 25_000.0;
const waterSurfaceOffset = 1.00018;
const finalSeaIceLatitudeDegrees = 68.0;

export function initialize(inputCanvasId, snapshot) {
    const inputCanvas = document.getElementById(inputCanvasId);
    const stage = inputCanvas?.closest('.planet-stage');
    if (!inputCanvas || !stage) return;

    let canvas = document.getElementById('ocean-depth-canvas');
    if (!canvas) {
        canvas = document.createElement('canvas');
        canvas.id = 'ocean-depth-canvas';
        canvas.setAttribute('aria-hidden', 'true');
        stage.appendChild(canvas);
    }

    const gl = canvas.getContext('webgl2', { antialias: true, alpha: true, premultipliedAlpha: false });
    if (!gl) return;

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
        seaIceFraction: 1.0,
        visible: true,
        tiles: new Map(),
        dirty: true,
        positionAttribute: gl.getAttribLocation(program, 'aPosition'),
        uniforms: {
            viewProjection: gl.getUniformLocation(program, 'uViewProjection'),
            lightDirection: gl.getUniformLocation(program, 'uLightDirection'),
            cameraPosition: gl.getUniformLocation(program, 'uCameraPosition'),
            planetRadiusMeters: gl.getUniformLocation(program, 'uPlanetRadiusMeters'),
            seaIceFraction: gl.getUniformLocation(program, 'uSeaIceFraction')
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
    state.seaIceFraction = snapshot.climateFeedback?.seaIceFraction ?? snapshot.climateFeedback?.cryosphereFraction ?? state.seaIceFraction;
    state.visible = !snapshot.localSurface;

    const activeKeys = new Set();
    for (const tile of snapshot.surfaceTiles ?? []) {
        if (!tile?.key || !tile.surfaceVertexCount || !tile.positions?.length) continue;
        activeKeys.add(tile.key);
        const existing = state.tiles.get(tile.key);
        if (existing) state.gl.deleteBuffer(existing.positionBuffer);
        const positionBuffer = state.gl.createBuffer();
        state.gl.bindBuffer(state.gl.ARRAY_BUFFER, positionBuffer);
        state.gl.bufferData(state.gl.ARRAY_BUFFER, new Float32Array(tile.positions), state.gl.STATIC_DRAW);
        state.tiles.set(tile.key, { positionBuffer, vertexCount: tile.surfaceVertexCount });
    }

    for (const [key, tile] of state.tiles) {
        if (activeKeys.has(key)) continue;
        state.gl.deleteBuffer(tile.positionBuffer);
        state.tiles.delete(key);
    }

    state.dirty = true;
}

export function dispose() {
    if (!state) return;
    if (window.__planetForgeOceanTest) delete window.__planetForgeOceanTest;
    for (const tile of state.tiles.values()) state.gl.deleteBuffer(tile.positionBuffer);
    state.tiles.clear();
    state.gl.deleteProgram(state.program);
    state.canvas.remove();
    state = null;
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
        if ((s.distance - 1.0) * s.planetRadiusMeters <= localExitAltitudeMeters) return;
        s.yaw += deltaX * 0.008;
        s.pitch = clamp(s.pitch + deltaY * 0.008, -1.25, 1.25);
        s.dirty = true;
    });
    canvas.addEventListener('wheel', event => {
        const zoomFactor = Math.exp(event.deltaY * 0.0015);
        const minimumAltitudeRatio = minimumCameraAltitudeMeters / Math.max(s.planetRadiusMeters, 1.0);
        const altitudeRatio = clamp(s.distance - 1.0, minimumAltitudeRatio, maximumCameraAltitudeRatio);
        s.distance = 1.0 + clamp(altitudeRatio * zoomFactor, minimumAltitudeRatio, maximumCameraAltitudeRatio);
        s.dirty = true;
    }, { passive: true });
}

function installVisualTestApi() {
    if (!new URLSearchParams(window.location.search).has('visualTest')) return;
    window.__planetForgeOceanTest = {
        measureDepthVariation() {
            if (!state) return { sampledPixels: 0, luminanceRange: 0.0, maximumAlpha: 0.0 };
            draw(state);
            state.dirty = false;
            return measureDepthVariation(state);
        }
    };
}

function measureDepthVariation(s) {
    const { gl, canvas } = s;
    if (canvas.width === 0 || canvas.height === 0) return { sampledPixels: 0, luminanceRange: 0.0, maximumAlpha: 0.0 };
    const pixels = new Uint8Array(canvas.width * canvas.height * 4);
    gl.readPixels(0, 0, canvas.width, canvas.height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
    let minimum = 1.0;
    let maximum = 0.0;
    let maximumAlpha = 0.0;
    let sampledPixels = 0;

    for (let y = 0; y < canvas.height; y += 4) {
        for (let x = 0; x < canvas.width; x += 4) {
            const offset = ((y * canvas.width) + x) * 4;
            const alpha = pixels[offset + 3] / 255.0;
            maximumAlpha = Math.max(maximumAlpha, alpha);
            if (alpha < 0.20) continue;
            const red = pixels[offset] / 255.0;
            const green = pixels[offset + 1] / 255.0;
            const blue = pixels[offset + 2] / 255.0;
            const luminance = (red * 0.2126) + (green * 0.7152) + (blue * 0.0722);
            minimum = Math.min(minimum, luminance);
            maximum = Math.max(maximum, luminance);
            sampledPixels++;
        }
    }

    return { sampledPixels, luminanceRange: sampledPixels > 0 ? maximum - minimum : 0.0, maximumAlpha };
}

function render() {
    if (!state) return;
    resize(state);
    if (state.dirty) { draw(state); state.dirty = false; }
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
    if (!s.visible || (s.distance - 1.0) * s.planetRadiusMeters <= localTransitionAltitudeMeters) return;

    const eye = orbitEye(s.yaw, s.pitch, s.distance);
    const projection = perspective(verticalFieldOfViewRadians, canvas.width / Math.max(canvas.height, 1), 0.002, 20.0);
    const viewProjection = multiply(projection, lookAt(eye, [0, 0, 0], [0, 1, 0]));

    gl.enable(gl.DEPTH_TEST);
    gl.enable(gl.CULL_FACE);
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    gl.useProgram(program);
    gl.uniformMatrix4fv(s.uniforms.viewProjection, false, viewProjection);
    gl.uniform3f(s.uniforms.lightDirection, 0.72, 0.42, 0.55);
    gl.uniform3f(s.uniforms.cameraPosition, eye[0], eye[1], eye[2]);
    gl.uniform1f(s.uniforms.planetRadiusMeters, s.planetRadiusMeters);
    gl.uniform1f(s.uniforms.seaIceFraction, s.seaIceFraction);

    for (const tile of s.tiles.values()) {
        gl.bindBuffer(gl.ARRAY_BUFFER, tile.positionBuffer);
        gl.enableVertexAttribArray(s.positionAttribute);
        gl.vertexAttribPointer(s.positionAttribute, 3, gl.FLOAT, false, 0, 0);
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
    for (let column = 0; column < 4; column++) for (let row = 0; row < 4; row++) out[column*4+row] = a[row]*b[column*4] + a[4+row]*b[column*4+1] + a[8+row]*b[column*4+2] + a[12+row]*b[column*4+3];
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
    float physicalRadius = length(aPosition);
    vec3 radial = normalize(aPosition);
    vDirection = radial;
    vElevationMeters = (physicalRadius - 1.0) * uPlanetRadiusMeters;
    gl_Position = uViewProjection * vec4(radial * ${waterSurfaceOffset.toFixed(6)}, 1.0);
}`;

const fragmentShaderSource = `#version 300 es
precision highp float;
in vec3 vDirection;
in float vElevationMeters;
uniform vec3 uLightDirection;
uniform vec3 uCameraPosition;
uniform float uSeaIceFraction;
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
    float n000 = hash31(cell + vec3(0.0));
    float n100 = hash31(cell + vec3(1.0,0.0,0.0));
    float n010 = hash31(cell + vec3(0.0,1.0,0.0));
    float n110 = hash31(cell + vec3(1.0,1.0,0.0));
    float n001 = hash31(cell + vec3(0.0,0.0,1.0));
    float n101 = hash31(cell + vec3(1.0,0.0,1.0));
    float n011 = hash31(cell + vec3(0.0,1.0,1.0));
    float n111 = hash31(cell + vec3(1.0));
    float nx00 = mix(n000,n100,f.x), nx10 = mix(n010,n110,f.x), nx01 = mix(n001,n101,f.x), nx11 = mix(n011,n111,f.x);
    return mix(mix(nx00,nx10,f.y),mix(nx01,nx11,f.y),f.z);
}

float fbm(vec3 p) {
    return valueNoise(p) * 0.56 + valueNoise((p * 2.07) + vec3(4.0,-7.0,2.0)) * 0.29 + valueNoise((p * 4.21) + vec3(-3.0,5.0,8.0)) * 0.15;
}

void main() {
    if (vElevationMeters >= 20.0) discard;

    vec3 radial = normalize(vDirection);
    float latitudeDegrees = degrees(asin(clamp(abs(radial.y), 0.0, 1.0)));
    float macro = fbm((radial * 5.0) + vec3(4.0,-2.0,7.0));
    float warpedLatitudeDegrees = latitudeDegrees + ((macro - 0.5) * 16.0);
    float seaRetreat = pow(1.0 - clamp(uSeaIceFraction, 0.0, 1.0), 0.82);
    float seaBreakup = smoothstep(0.10, 0.52, seaRetreat);
    float seaIceLineDegrees = mix(-25.0, ${finalSeaIceLatitudeDegrees.toFixed(1)}, seaRetreat);
    float frozenSeaSheet = smoothstep(seaIceLineDegrees - 1.5, seaIceLineDegrees + 5.0, warpedLatitudeDegrees);
    float seaPatch = fbm((radial * 9.0) + vec3(-3.0,7.0,4.0));
    float polarSeaBias = smoothstep(58.0, 84.0, warpedLatitudeDegrees);
    float fragmentedSea = smoothstep(seaIceLineDegrees - 4.0, seaIceLineDegrees + 7.5, warpedLatitudeDegrees) * smoothstep(0.56, 0.70, seaPatch + (polarSeaBias * 0.09));
    float seaCoverage = mix(frozenSeaSheet, fragmentedSea, seaBreakup);
    float openWater = 1.0 - smoothstep(0.30, 0.58, seaCoverage);
    if (openWater <= 0.01) discard;

    float depthMeters = max(-vElevationMeters, 0.0);
    float shelfTransition = smoothstep(650.0, 2400.0, depthMeters);
    float slopeTransition = smoothstep(2100.0, 4100.0, depthMeters);
    float abyssTransition = smoothstep(3900.0, 5700.0, depthMeters);
    float coastGlow = 1.0 - smoothstep(0.0, 1250.0, depthMeters);

    vec3 coastalWater = vec3(0.075, 0.47, 0.50);
    vec3 shelfWater = vec3(0.024, 0.33, 0.43);
    vec3 slopeWater = vec3(0.010, 0.17, 0.30);
    vec3 abyssWater = vec3(0.003, 0.042, 0.105);

    vec3 color = mix(coastalWater, shelfWater, shelfTransition);
    color = mix(color, slopeWater, slopeTransition);
    color = mix(color, abyssWater, abyssTransition);
    color = mix(color, vec3(0.10, 0.53, 0.52), coastGlow * 0.22);

    float regional = valueNoise((radial * 7.0) + vec3(3.0,-4.0,8.0)) - 0.5;
    color += vec3(0.0, regional * 0.026, regional * 0.040) * (1.0 - abyssTransition);

    vec3 normal = radial;
    vec3 light = normalize(uLightDirection);
    vec3 viewDirection = normalize(uCameraPosition - (normal * ${waterSurfaceOffset.toFixed(6)}));
    float diffuse = 0.86 + (0.14 * max(dot(normal, light), 0.0));
    float fresnel = pow(1.0 - max(dot(normal, viewDirection), 0.0), 3.2);
    vec3 halfVector = normalize(light + viewDirection);
    float specular = pow(max(dot(normal, halfVector), 0.0), 72.0) * 0.09;
    color *= diffuse;
    color = mix(color, vec3(0.045, 0.12, 0.19), fresnel * 0.16);
    color += vec3(specular * 0.70, specular * 0.86, specular);

    float shorelineAlpha = smoothstep(2.0, 24.0, depthMeters);
    float waterAlpha = smoothstep(0.02, 0.42, openWater);
    outColor = vec4(clamp(color, 0.0, 1.0), 0.98 * shorelineAlpha * waterAlpha);
}`;