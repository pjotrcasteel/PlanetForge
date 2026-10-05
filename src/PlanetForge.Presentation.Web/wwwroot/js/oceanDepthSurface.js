let state;

const verticalFieldOfViewRadians = Math.PI / 4.2;
const minimumCameraAltitudeMeters = 3.0;
const maximumCameraAltitudeRatio = 4.2;
const localTransitionAltitudeMeters = 20_000.0;
const localExitAltitudeMeters = 25_000.0;
const waterSurfaceOffset = 1.00018;

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
            planetRadiusMeters: gl.getUniformLocation(program, 'uPlanetRadiusMeters')
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
    state.visible = !snapshot.localSurface;

    const surfaceTiles = snapshot.surfaceTiles ?? [];
    if (surfaceTiles.length === 0) {
        state.dirty = true;
        return;
    }

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

    state.dirty = true;
}

export function dispose() {
    if (!state) return;
    if (window.__planetForgeOceanTest) delete window.__planetForgeOceanTest;
    for (const tile of state.tiles.values()) deleteTileBuffers(state.gl, tile);
    state.tiles.clear();
    state.gl.deleteProgram(state.program);
    state.canvas.remove();
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
            if (!state) return { sampledPixels: 0, luminanceRange: 0.0 };
            draw(state);
            state.dirty = false;
            return measureDepthVariation(state);
        }
    };
}

function measureDepthVariation(s) {
    const { gl, canvas } = s;
    const width = canvas.width;
    const height = canvas.height;
    if (width === 0 || height === 0) return { sampledPixels: 0, luminanceRange: 0.0 };

    const pixels = new Uint8Array(width * height * 4);
    gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
    let minimum = 1.0;
    let maximum = 0.0;
    let sampledPixels = 0;
    const step = 4;

    for (let y = 0; y < height; y += step) {
        for (let x = 0; x < width; x += step) {
            const offset = ((y * width) + x) * 4;
            if (pixels[offset + 3] < 180) continue;
            const red = pixels[offset] / 255.0;
            const green = pixels[offset + 1] / 255.0;
            const blue = pixels[offset + 2] / 255.0;
            const luminance = (red * 0.2126) + (green * 0.7152) + (blue * 0.0722);
            minimum = Math.min(minimum, luminance);
            maximum = Math.max(maximum, luminance);
            sampledPixels++;
        }
    }

    return { sampledPixels, luminanceRange: sampledPixels > 0 ? maximum - minimum : 0.0 };
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
    gl.uniform3f(s.uniforms.lightDirection, 0.72, 0.42, 0.55);
    gl.uniform3f(s.uniforms.cameraPosition, eye[0], eye[1], eye[2]);
    gl.uniform1f(s.uniforms.planetRadiusMeters, s.planetRadiusMeters);

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

void main() {
    if (vElevationMeters >= 25.0) discard;

    float depthMeters = max(-vElevationMeters, 0.0);
    float shore = 1.0 - smoothstep(0.0, 180.0, depthMeters);
    float shelf = 1.0 - smoothstep(180.0, 900.0, depthMeters);
    float upperSlope = 1.0 - smoothstep(900.0, 2300.0, depthMeters);
    float abyss = smoothstep(3000.0, 5600.0, depthMeters);

    vec3 coastalWater = vec3(0.075, 0.39, 0.43);
    vec3 shelfWater = vec3(0.035, 0.27, 0.34);
    vec3 slopeWater = vec3(0.022, 0.18, 0.27);
    vec3 deepWater = vec3(0.012, 0.085, 0.15);

    vec3 color = mix(slopeWater, shelfWater, upperSlope);
    color = mix(color, coastalWater, max(shore, shelf * 0.58));
    color = mix(color, deepWater, abyss);

    float basinNoise = (valueNoise((vDirection * 3.2) + vec3(4.0, -7.0, 2.0)) - 0.5) * 0.035;
    float shelfNoise = (valueNoise((vDirection * 12.0) + vec3(-3.0, 5.0, 8.0)) - 0.5) * 0.020 * (1.0 - abyss);
    color += vec3(basinNoise * 0.28, basinNoise * 0.65, basinNoise) + vec3(0.0, shelfNoise * 0.55, shelfNoise);

    vec3 normal = normalize(vDirection);
    vec3 light = normalize(uLightDirection);
    vec3 viewDirection = normalize(uCameraPosition - (normal * ${waterSurfaceOffset.toFixed(6)}));
    float diffuse = 0.76 + (0.24 * max(dot(normal, light), 0.0));
    float fresnel = pow(1.0 - max(dot(normal, viewDirection), 0.0), 3.0);
    vec3 halfVector = normalize(light + viewDirection);
    float specular = pow(max(dot(normal, halfVector), 0.0), 54.0) * 0.16;

    color *= diffuse;
    color = mix(color, vec3(0.08, 0.20, 0.27), fresnel * 0.25);
    color += vec3(specular * 0.72, specular * 0.86, specular);

    float coastAlpha = smoothstep(-15.0, 55.0, depthMeters);
    outColor = vec4(clamp(color, 0.0, 1.0), 0.94 * coastAlpha);
}`;