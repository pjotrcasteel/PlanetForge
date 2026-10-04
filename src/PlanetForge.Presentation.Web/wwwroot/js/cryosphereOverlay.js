let state;

const verticalFieldOfViewRadians = Math.PI / 4.2;
const minimumCameraAltitudeMeters = 3.0;
const maximumCameraAltitudeRatio = 4.2;
const localTransitionAltitudeMeters = 20_000.0;
const localExitAltitudeMeters = 25_000.0;
const surfaceOffset = 1.0015;
const landFreezingStartKelvin = 272.0;
const landFreezingEndKelvin = 265.0;
const oceanFreezingStartKelvin = 271.5;
const oceanFreezingEndKelvin = 266.0;
const latitudeCoolingKelvin = 18.0;
const elevationLapseRateKelvinPerMeter = 0.0065;
const temperateTransitionStartKelvin = 270.0;
const temperateTransitionEndKelvin = 278.0;
const alpineSnowStartMeters = 1_900.0;
const alpineSnowFullMeters = 3_200.0;
const polarRetentionStart = 0.93;
const polarRetentionFull = 0.98;
const permanentPolarStart = 0.95;
const permanentPolarFull = 0.99;
const triangleSeamOverlapPixels = 1.15;

export function initialize(overlayCanvasId, inputCanvasId, snapshot) {
    const canvas = document.getElementById(overlayCanvasId);
    const inputCanvas = document.getElementById(inputCanvasId);
    const context = canvas?.getContext('2d');
    if (!canvas || !inputCanvas || !context) return;

    state = {
        canvas,
        inputCanvas,
        context,
        yaw: -0.65,
        pitch: 0.24,
        distance: 3.15,
        dragging: false,
        lastX: 0,
        lastY: 0,
        planetRadiusMeters: 6_371_000.0,
        seaLevelMeters: 0.0,
        surfaceTemperatureKelvin: 250.0,
        tileCache: new Map(),
        dirty: true
    };

    installInput(state);
    setPlanet(snapshot);
    requestAnimationFrame(render);
}

export function setPlanet(snapshot) {
    if (!state || !snapshot) return;

    state.planetRadiusMeters = snapshot.physicalParameters?.radiusMeters ?? state.planetRadiusMeters;
    state.seaLevelMeters = snapshot.seaLevelMeters ?? state.seaLevelMeters;
    state.surfaceTemperatureKelvin = snapshot.climate?.surfaceTemperatureKelvin ?? state.surfaceTemperatureKelvin;

    const activeKeys = new Set();
    for (const tile of snapshot.surfaceTiles ?? []) {
        if (!tile?.positions?.length || !tile.surfaceVertexCount) continue;
        activeKeys.add(tile.key);
        state.tileCache.set(tile.key, buildTriangles(tile.positions, tile.surfaceVertexCount, state.planetRadiusMeters));
    }

    for (const key of state.tileCache.keys()) {
        if (!activeKeys.has(key)) state.tileCache.delete(key);
    }

    state.dirty = true;
}

export function dispose() {
    state = null;
}

function buildTriangles(positions, surfaceVertexCount, planetRadiusMeters) {
    const triangles = [];
    const vertexLimit = Math.min(surfaceVertexCount, Math.floor(positions.length / 3));

    for (let vertex = 0; vertex + 2 < vertexLimit; vertex += 3) {
        const offset = vertex * 3;
        const a = [positions[offset], positions[offset + 1], positions[offset + 2]];
        const b = [positions[offset + 3], positions[offset + 4], positions[offset + 5]];
        const c = [positions[offset + 6], positions[offset + 7], positions[offset + 8]];
        const center = normalize([(a[0] + b[0] + c[0]) / 3.0, (a[1] + b[1] + c[1]) / 3.0, (a[2] + b[2] + c[2]) / 3.0]);
        const elevationMeters = (((length(a) + length(b) + length(c)) / 3.0) - 1.0) * planetRadiusMeters;
        triangles.push({ a, b, c, center, elevationMeters });
    }

    return triangles;
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
    const { context, canvas } = s;
    context.clearRect(0, 0, canvas.width, canvas.height);

    const altitudeMeters = Math.max(0.0, (s.distance - 1.0) * s.planetRadiusMeters);
    if (altitudeMeters <= localTransitionAltitudeMeters) return;

    const aspect = canvas.width / Math.max(canvas.height, 1);
    const projection = perspective(verticalFieldOfViewRadians, aspect, 0.002, 20.0);
    const eye = orbitEye(s.yaw, s.pitch, s.distance);
    const view = lookAt(eye, [0, 0, 0], [0, 1, 0]);
    const viewProjection = multiply(projection, view);

    context.globalCompositeOperation = 'source-over';
    context.lineJoin = 'round';
    context.lineWidth = triangleSeamOverlapPixels;

    for (const triangles of s.tileCache.values()) {
        for (const triangle of triangles) drawTriangle(context, triangle, s, eye, viewProjection, canvas.width, canvas.height);
    }
}

function drawTriangle(context, triangle, s, eye, viewProjection, width, height) {
    if (!isTriangleVisible(triangle, eye)) return;

    const latitude = Math.abs(triangle.center[1]);
    const elevationCooling = Math.max(triangle.elevationMeters, 0.0) * elevationLapseRateKelvinPerMeter;
    const latitudeCooling = latitudeCoolingKelvin * Math.pow(latitude, 1.45);
    const localTemperatureKelvin = s.surfaceTemperatureKelvin - latitudeCooling - elevationCooling;
    const ocean = triangle.elevationMeters < s.seaLevelMeters;
    let dynamicIce = ocean
        ? 1.0 - smoothstep(oceanFreezingEndKelvin, oceanFreezingStartKelvin, localTemperatureKelvin)
        : 1.0 - smoothstep(landFreezingEndKelvin, landFreezingStartKelvin, localTemperatureKelvin);

    const temperateProgress = smoothstep(temperateTransitionStartKelvin, temperateTransitionEndKelvin, s.surfaceTemperatureKelvin);
    const polarRetention = smoothstep(polarRetentionStart, polarRetentionFull, latitude);
    const alpineRetention = smoothstep(alpineSnowStartMeters, alpineSnowFullMeters, triangle.elevationMeters);
    const warmRetention = ocean ? polarRetention : Math.max(polarRetention, alpineRetention);
    dynamicIce *= mix(1.0, warmRetention, temperateProgress);

    const permanentPolar = smoothstep(permanentPolarStart, permanentPolarFull, latitude);
    const coverage = Math.max(dynamicIce, permanentPolar * 0.94);
    if (coverage < 0.025) return;

    const a = project(viewProjection, scaleToSurface(triangle.a), width, height);
    const b = project(viewProjection, scaleToSurface(triangle.b), width, height);
    const c = project(viewProjection, scaleToSurface(triangle.c), width, height);
    if (!a || !b || !c) return;

    context.beginPath();
    context.moveTo(a[0], a[1]);
    context.lineTo(b[0], b[1]);
    context.lineTo(c[0], c[1]);
    context.closePath();

    const opacity = clamp(coverage * (ocean ? 0.78 : 0.86), 0.0, 0.86);
    const fill = ocean
        ? `rgba(194, 222, 229, ${opacity})`
        : `rgba(224, 235, 234, ${opacity})`;
    context.fillStyle = fill;
    context.strokeStyle = fill;
    context.fill();
    context.stroke();
}

function isTriangleVisible(triangle, eye) {
    if (dot(triangle.center, eye) <= 1.0) return false;
    return isSurfacePointVisible(triangle.a, eye) && isSurfacePointVisible(triangle.b, eye) && isSurfacePointVisible(triangle.c, eye);
}

function isSurfacePointVisible(point, eye) {
    const direction = normalize(point);
    return dot(direction, eye) > 1.0;
}

function scaleToSurface(point) {
    const magnitude = length(point);
    if (magnitude <= 0.0) return point;
    const factor = (magnitude + (surfaceOffset - 1.0)) / magnitude;
    return [point[0] * factor, point[1] * factor, point[2] * factor];
}

function project(matrix, point, width, height) {
    const x = point[0], y = point[1], z = point[2];
    const clipX = matrix[0] * x + matrix[4] * y + matrix[8] * z + matrix[12];
    const clipY = matrix[1] * x + matrix[5] * y + matrix[9] * z + matrix[13];
    const clipW = matrix[3] * x + matrix[7] * y + matrix[11] * z + matrix[15];
    if (clipW <= 0.0) return null;
    const ndcX = clipX / clipW;
    const ndcY = clipY / clipW;
    return [(ndcX * 0.5 + 0.5) * width, (1.0 - (ndcY * 0.5 + 0.5)) * height];
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

function smoothstep(edge0, edge1, value) {
    const t = clamp((value - edge0) / (edge1 - edge0), 0.0, 1.0);
    return t * t * (3.0 - (2.0 * t));
}

function mix(first, second, amount) { return first + ((second - first) * amount); }
function length(v) { return Math.hypot(v[0], v[1], v[2]); }
function normalize(v) { const magnitude = length(v) || 1.0; return [v[0]/magnitude, v[1]/magnitude, v[2]/magnitude]; }
function cross(a,b) { return [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]; }
function dot(a,b) { return a[0]*b[0] + a[1]*b[1] + a[2]*b[2]; }
function clamp(value,min,max) { return Math.max(min, Math.min(max, value)); }
