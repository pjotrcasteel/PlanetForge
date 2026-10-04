let state;

const verticalFieldOfViewRadians = Math.PI / 4.2;
const minimumCameraAltitudeMeters = 3.0;
const maximumCameraAltitudeRatio = 4.2;
const localTransitionAltitudeMeters = 20_000.0;
const localExitAltitudeMeters = 25_000.0;
const riverSurfaceOffset = 1.006;
const endpointPrecision = 100_000;

export function initialize(overlayCanvasId, inputCanvasId, planetRadiusMeters) {
    const canvas = document.getElementById(overlayCanvasId);
    const inputCanvas = document.getElementById(inputCanvasId);
    const context = canvas?.getContext('2d');
    if (!canvas || !inputCanvas || !context) return;

    state = {
        canvas,
        inputCanvas,
        context,
        planetRadiusMeters,
        yaw: -0.65,
        pitch: 0.24,
        distance: 3.15,
        dragging: false,
        lastX: 0,
        lastY: 0,
        paths: [],
        riverChains: []
    };

    installInput(state);
    requestAnimationFrame(render);
}

export function setPlanetRadius(planetRadiusMeters) {
    if (state) state.planetRadiusMeters = planetRadiusMeters;
}

export function setWaterCycle(waterCycle) {
    if (!state) return;
    state.paths = waterCycle?.activeRiverSegments ?? [];
    state.riverChains = buildRiverChains(state.paths);
}

export function clearWaterCycle() {
    if (!state) return;
    state.paths = [];
    state.riverChains = [];
}

export function dispose() {
    state = null;
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
    });
    canvas.addEventListener('wheel', event => {
        const zoomFactor = Math.exp(event.deltaY * 0.0015);
        const minimumAltitudeRatio = minimumCameraAltitudeMeters / Math.max(s.planetRadiusMeters, 1.0);
        const altitudeRatio = clamp(s.distance - 1.0, minimumAltitudeRatio, maximumCameraAltitudeRatio);
        s.distance = 1.0 + clamp(altitudeRatio * zoomFactor, minimumAltitudeRatio, maximumCameraAltitudeRatio);
    }, { passive: true });
}

function render() {
    if (!state) return;
    resize(state);
    draw(state);
    requestAnimationFrame(render);
}

function resize(s) {
    const ratio = Math.min(window.devicePixelRatio || 1, 1.5);
    const width = Math.floor(s.canvas.clientWidth * ratio);
    const height = Math.floor(s.canvas.clientHeight * ratio);
    if (s.canvas.width === width && s.canvas.height === height) return;
    s.canvas.width = width;
    s.canvas.height = height;
}

function draw(s) {
    const { context, canvas } = s;
    context.clearRect(0, 0, canvas.width, canvas.height);
    if (s.riverChains.length === 0) return;

    const altitudeMeters = Math.max(0.0, (s.distance - 1.0) * s.planetRadiusMeters);
    if (altitudeMeters <= localTransitionAltitudeMeters) return;

    const aspect = canvas.width / Math.max(canvas.height, 1);
    const projection = perspective(verticalFieldOfViewRadians, aspect, 0.002, 20.0);
    const eye = orbitEye(s.yaw, s.pitch, s.distance);
    const view = lookAt(eye, [0, 0, 0], [0, 1, 0]);
    const viewProjection = multiply(projection, view);

    context.lineCap = 'round';
    context.lineJoin = 'round';
    for (const chain of s.riverChains) {
        drawRiverChain(context, chain, eye, viewProjection, canvas.width, canvas.height);
    }
}

function buildRiverChains(paths) {
    if (!paths || paths.length === 0) return [];

    const outgoing = new Map();
    const incomingCount = new Map();
    for (const path of paths) {
        const fromKey = endpointKey(path.fromX, path.fromY, path.fromZ);
        const toKey = endpointKey(path.toX, path.toY, path.toZ);
        outgoing.set(fromKey, path);
        incomingCount.set(toKey, (incomingCount.get(toKey) ?? 0) + 1);
        if (!incomingCount.has(fromKey)) incomingCount.set(fromKey, 0);
    }

    const visited = new Set();
    const chains = [];
    const sourcePaths = paths.filter(path => incomingCount.get(endpointKey(path.fromX, path.fromY, path.fromZ)) === 0);

    for (const source of sourcePaths) {
        const chain = followChain(source, outgoing, visited);
        if (chain) chains.push(chain);
    }

    for (const path of paths) {
        if (visited.has(segmentKey(path))) continue;
        const chain = followChain(path, outgoing, visited);
        if (chain) chains.push(chain);
    }

    return chains;
}

function followChain(start, outgoing, visited) {
    const points = [];
    const discharges = [];
    const streamOrders = [];
    let current = start;

    while (current && !visited.has(segmentKey(current))) {
        visited.add(segmentKey(current));
        if (points.length === 0) points.push(surfacePoint(current.fromX, current.fromY, current.fromZ));
        points.push(surfacePoint(current.toX, current.toY, current.toZ));
        discharges.push(clamp(current.relativeDischarge ?? 0.2, 0.08, 1.0));
        streamOrders.push(current.streamOrder ?? 1);
        current = outgoing.get(endpointKey(current.toX, current.toY, current.toZ));
    }

    if (points.length < 2) return null;
    return {
        points,
        discharge: Math.max(...discharges),
        streamOrder: Math.max(...streamOrders)
    };
}

function drawRiverChain(context, chain, eye, viewProjection, width, height) {
    const visibleRuns = [];
    let currentRun = [];

    for (const point of chain.points) {
        if (dot(point, eye) <= 1.0) {
            if (currentRun.length >= 2) visibleRuns.push(currentRun);
            currentRun = [];
            continue;
        }

        const projected = project(viewProjection, point, width, height);
        if (!projected) {
            if (currentRun.length >= 2) visibleRuns.push(currentRun);
            currentRun = [];
            continue;
        }

        currentRun.push(projected);
    }

    if (currentRun.length >= 2) visibleRuns.push(currentRun);
    if (visibleRuns.length === 0) return;

    const discharge = chain.discharge;
    const orderScale = clamp((chain.streamOrder - 1) * 0.12, 0.0, 0.45);
    context.strokeStyle = `rgba(68, 188, 235, ${0.46 + discharge * 0.44})`;
    context.lineWidth = 0.75 + discharge * 2.4 + orderScale;

    for (const points of visibleRuns) {
        drawSmoothPolyline(context, points);
    }
}

function drawSmoothPolyline(context, points) {
    context.beginPath();
    context.moveTo(points[0][0], points[0][1]);

    if (points.length === 2) {
        context.lineTo(points[1][0], points[1][1]);
        context.stroke();
        return;
    }

    for (let index = 1; index < points.length - 1; index++) {
        const current = points[index];
        const next = points[index + 1];
        const midpointX = (current[0] + next[0]) * 0.5;
        const midpointY = (current[1] + next[1]) * 0.5;
        context.quadraticCurveTo(current[0], current[1], midpointX, midpointY);
    }

    const last = points[points.length - 1];
    context.lineTo(last[0], last[1]);
    context.stroke();
}

function surfacePoint(x, y, z) {
    return [x * riverSurfaceOffset, y * riverSurfaceOffset, z * riverSurfaceOffset];
}

function endpointKey(x, y, z) {
    return `${Math.round(x * endpointPrecision)},${Math.round(y * endpointPrecision)},${Math.round(z * endpointPrecision)}`;
}

function segmentKey(path) {
    return `${endpointKey(path.fromX, path.fromY, path.fromZ)}>${endpointKey(path.toX, path.toY, path.toZ)}`;
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

function normalize(v) { const length = Math.hypot(v[0], v[1], v[2]) || 1; return [v[0]/length, v[1]/length, v[2]/length]; }
function cross(a,b) { return [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]; }
function dot(a,b) { return a[0]*b[0] + a[1]*b[1] + a[2]*b[2]; }
function clamp(value,min,max) { return Math.max(min, Math.min(max, value)); }
