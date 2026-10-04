let state;

const verticalFieldOfViewRadians = Math.PI / 4.2;
const minimumCameraAltitudeMeters = 3.0;
const maximumCameraAltitudeRatio = 4.2;
const localTransitionAltitudeMeters = 20_000.0;
const localExitAltitudeMeters = 25_000.0;

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
        paths: []
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
}

export function clearWaterCycle() {
    if (state) state.paths = [];
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
    if (s.paths.length === 0) return;

    const altitudeMeters = Math.max(0.0, (s.distance - 1.0) * s.planetRadiusMeters);
    if (altitudeMeters <= localTransitionAltitudeMeters) return;

    const aspect = canvas.width / Math.max(canvas.height, 1);
    const projection = perspective(verticalFieldOfViewRadians, aspect, 0.002, 20.0);
    const eye = orbitEye(s.yaw, s.pitch, s.distance);
    const view = lookAt(eye, [0, 0, 0], [0, 1, 0]);
    const viewProjection = multiply(projection, view);

    context.lineCap = 'round';
    context.lineJoin = 'round';
    for (const path of s.paths) {
        const from = [path.fromX * 1.006, path.fromY * 1.006, path.fromZ * 1.006];
        const to = [path.toX * 1.006, path.toY * 1.006, path.toZ * 1.006];
        const midpoint = normalize([(from[0] + to[0]) * 0.5, (from[1] + to[1]) * 0.5, (from[2] + to[2]) * 0.5]);
        if (dot(midpoint, eye) <= 1.0) continue;

        const projectedFrom = project(viewProjection, from, canvas.width, canvas.height);
        const projectedTo = project(viewProjection, to, canvas.width, canvas.height);
        if (!projectedFrom || !projectedTo) continue;

        const discharge = clamp(path.relativeDischarge ?? 0.2, 0.08, 1.0);
        context.strokeStyle = `rgba(68, 188, 235, ${0.48 + discharge * 0.42})`;
        context.lineWidth = 0.8 + discharge * 2.6;
        context.beginPath();
        context.moveTo(projectedFrom[0], projectedFrom[1]);
        context.lineTo(projectedTo[0], projectedTo[1]);
        context.stroke();
    }
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