let state;

const verticalFieldOfViewRadians = Math.PI / 4.2;
const minimumCameraAltitudeMeters = 3.0;
const maximumCameraAltitudeRatio = 4.2;
const localTransitionAltitudeMeters = 20_000.0;
const localExitAltitudeMeters = 25_000.0;
const waterSurfaceClearanceMeters = 18.0;
const finalSeaIceLatitudeDegrees = 68.0;

export function initialize(inputCanvasId, snapshot) {
    const inputCanvas = document.getElementById(inputCanvasId);
    const canvas = document.getElementById('ocean-depth-canvas');
    if (!inputCanvas || !canvas) return;

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
        seaLevelMeters: 0.0,
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
            seaLevelMeters: gl.getUniformLocation(program, 'uSeaLevelMeters'),
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
    state.seaLevelMeters = snapshot.seaLevelMeters ?? state.seaLevelMeters;
    state.seaIceFraction = snapshot.climateFeedback?.seaIceFraction ?? snapshot.climateFeedback?.cryosphereFraction ?? state.seaIceFraction;
    state.visible = !snapshot.localSurface;

    const surfaceTiles = snapshot.surfaceTiles ?? [];
    if (surfaceTiles.length > 0) {
        const activeKeys = new Set();
        for (const tile of surfaceTiles) {
            if (!tile?.key || !tile.surfaceVertexCount) continue;
            activeKeys.add(tile.key);
            if (!tile.positions?.length) continue;
            replaceTile(state, tile);
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
    if (window.__planetForgeOceanTest) delete window.__planetForgeOceanTest;
    for (const tile of state.tiles.values()) state.gl.deleteBuffer(tile.positionBuffer);
    state.tiles.clear();
    state.gl.deleteProgram(state.program);
    state = null;
}

function replaceTile(s, tile) {
    const existing = s.tiles.get(tile.key);
    if (existing) s.gl.deleteBuffer(existing.positionBuffer);
    const positionBuffer = s.gl.createBuffer();
    s.gl.bindBuffer(s.gl.ARRAY_BUFFER, positionBuffer);
    s.gl.bufferData(s.gl.ARRAY_BUFFER, new Float32Array(tile.positions), s.gl.STATIC_DRAW);
    s.tiles.set(tile.key, { positionBuffer, vertexCount: tile.surfaceVertexCount });
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
            if (!state) return emptyMetrics();
            draw(state);
            state.dirty = false;
            return measureDepthVariation(state);
        }
    };
}

function emptyMetrics() {
    return { sampledPixels: 0, luminanceRange: 0.0, maximumAlpha: 0.0, canvasWidth: 0, canvasHeight: 0, tileCount: 0, seaIceFraction: 0.0, glError: -1 };
}

function measureDepthVariation(s) {
    const { gl, canvas } = s;
    const metrics = {
        sampledPixels: 0,
        luminanceRange: 0.0,
        maximumAlpha: 0.0,
        canvasWidth: canvas.width,
        canvasHeight: canvas.height,
        tileCount: s.tiles.size,
        seaIceFraction: s.seaIceFraction,
        glError: gl.getError()
    };
    if (canvas.width === 0 || canvas.height === 0) return metrics;

    const pixels = new Uint8Array(canvas.width * canvas.height * 4);
    gl.readPixels(0, 0, canvas.width, canvas.height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
    let minimum = 1.0;
    let maximum = 0.0;

    for (let y = 0; y < canvas.height; y += 4) {
        for (let x = 0; x < canvas.width; x += 4) {
            const offset = ((y * canvas.width) + x) * 4;
            const alpha = pixels[offset + 3] / 255.0;
            metrics.maximumAlpha = Math.max(metrics.maximumAlpha, alpha);
            if (alpha < 0.95) continue;
            const red = pixels[offset] / 255.0;
            const green = pixels[offset + 1] / 255.0;
            const blue = pixels[offset + 2] / 255.0;
            const luminance = (red * 0.2126) + (green * 0.7152) + (blue * 0.0722);
            minimum = Math.min(minimum, luminance);
            maximum = Math.max(maximum, luminance);
            metrics.sampledPixels++;
        }
    }

    metrics.luminanceRange = metrics.sampledPixels > 0 ? maximum - minimum : 0.0;
    metrics.glError = gl.getError();
    return metrics;
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
    if (!s.visible || (s.distance - 1.0) * s.planetRadiusMeters <= localTransitionAltitudeMeters) return;

    const eye = orbitEye(s.yaw, s.pitch, s.distance);
    const projection = perspective(verticalFieldOfViewRadians, canvas.width / Math.max(canvas.height, 1), 0.002, 20.0);
    const viewProjection = multiply(projection, lookAt(eye, [0, 0, 0], [0, 1, 0]));

    gl.enable(gl.DEPTH_TEST);
    gl.depthMask(true);
    gl.enable(gl.CULL_FACE);
    gl.frontFace(gl.CCW);
    gl.cullFace(gl.BACK);
    gl.disable(gl.BLEND);
    gl.useProgram(program);
    gl.uniformMatrix4fv(s.uniforms.viewProjection, false, viewProjection);
    gl.uniform3f(s.uniforms.lightDirection, 0.72, 0.42, 0.55);
    gl.uniform3f(s.uniforms.cameraPosition, eye[0], eye[1], eye[2]);
    gl.uniform1f(s.uniforms.planetRadiusMeters, s.planetRadiusMeters);
    gl.uniform1f(s.uniforms.seaLevelMeters, s.seaLevelMeters);
    gl.uniform1f(s.uniforms.seaIceFraction, s.seaIceFraction);

    for (const tile of s.tiles.values()) {
        gl.bindBuffer(gl.ARRAY_BUFFER, tile.positionBuffer);
        gl.enableVertexAttribArray(s.positionAttribute);
        gl.vertexAttribPointer(s.positionAttribute, 3, gl.FLOAT, false, 0, 0);
        gl.drawArrays(gl.TRIANGLES, 0, tile.vertexCount);
    }
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
uniform mat4 uViewProjection;
uniform float uPlanetRadiusMeters;
uniform float uSeaLevelMeters;
out vec3 vDirection;
out float vElevationMeters;
void main() {
    float physicalRadius = length(aPosition);
    vec3 radial = normalize(aPosition);
    vDirection = radial;
    vElevationMeters = (physicalRadius - 1.0) * uPlanetRadiusMeters;
    float waterSurfaceRadius = 1.0 + ((uSeaLevelMeters + 18.0) / uPlanetRadiusMeters);
    gl_Position = uViewProjection * vec4(radial * waterSurfaceRadius, 1.0);
}`;

const fragmentShaderSource = `#version 300 es
precision highp float;
in vec3 vDirection;
in float vElevationMeters;
uniform vec3 uLightDirection;
uniform vec3 uCameraPosition;
uniform float uPlanetRadiusMeters;
uniform float uSeaLevelMeters;
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
    float physicalDepth = uSeaLevelMeters - vElevationMeters;
    if (physicalDepth <= 0.0) discard;

    vec3 radial = normalize(vDirection);
    float waterSurfaceRadius = 1.0 + ((uSeaLevelMeters + 18.0) / uPlanetRadiusMeters);
    vec3 viewDirection = normalize(uCameraPosition - (radial * waterSurfaceRadius));
    if (dot(radial, viewDirection) <= 0.0) discard;

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
    if (openWater <= 0.08) discard;

    float shelf = 1.0 - smoothstep(180.0, 1250.0, physicalDepth);
    float coast = 1.0 - smoothstep(0.0, 520.0, physicalDepth);
    float deepening = smoothstep(900.0, 2600.0, physicalDepth);

    vec3 deepOcean = vec3(0.008, 0.078, 0.135);
    vec3 midOcean = vec3(0.012, 0.125, 0.190);
    vec3 shelfOcean = vec3(0.030, 0.235, 0.285);
    vec3 coastalOcean = vec3(0.060, 0.315, 0.330);

    vec3 color = mix(midOcean, deepOcean, deepening);
    color = mix(color, shelfOcean, shelf * 0.78);
    color = mix(color, coastalOcean, coast * 0.58);

    float fineVariation = fbm((radial * 18.0) + vec3(3.0,-4.0,8.0)) - 0.5;
    float microVariation = valueNoise((radial * 44.0) + vec3(-2.0,9.0,5.0)) - 0.5;
    color += vec3(0.0, fineVariation * 0.009, fineVariation * 0.014);
    color += vec3(0.0, microVariation * 0.004, microVariation * 0.006);

    float waterMaturity = smoothstep(0.12, 0.78, openWater);
    vec3 newlyOpenedWater = vec3(0.105, 0.190, 0.220);
    color = mix(newlyOpenedWater, color, waterMaturity);

    vec3 normal = radial;
    vec3 light = normalize(uLightDirection);
    float diffuse = 0.92 + (0.08 * max(dot(normal, light), 0.0));
    float fresnel = pow(1.0 - max(dot(normal, viewDirection), 0.0), 3.6);
    vec3 halfVector = normalize(light + viewDirection);
    float specular = pow(max(dot(normal, halfVector), 0.0), 104.0) * 0.045;
    color *= diffuse;
    color = mix(color, vec3(0.025, 0.085, 0.135), fresnel * 0.08);
    color += vec3(specular * 0.60, specular * 0.76, specular);

    outColor = vec4(clamp(color, 0.0, 1.0), 1.0);
}`;