import { getRetainedSurfaceGeometry } from './cryosphereSurfaceAdapter.js';

let state;

const verticalFieldOfViewRadians = Math.PI / 4.2;
const minimumCameraAltitudeMeters = 3.0;
const maximumCameraAltitudeRatio = 4.2;
const localTransitionAltitudeMeters = 20_000.0;
const localExitAltitudeMeters = 25_000.0;
const landReliefExaggeration = 28.0;
const visualWaterClearanceMeters = 180.0;
const terrainLatitudeBins = 60;
const terrainLongitudeBins = 120;
const riverSamplesPerSegment = 7;

export function initialize(overlayCanvasId, inputCanvasId, planetRadiusMeters) {
    const canvas = document.getElementById(overlayCanvasId);
    const inputCanvas = document.getElementById(inputCanvasId);
    const context = canvas?.getContext('2d', { alpha: true });
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
        cycle: null,
        riverSegments: [],
        lakeGroups: [],
        terrainIndex: null,
        terrainVertexCount: 0,
        metrics: createMetrics()
    };

    installInput(state);
    installVisualTestApi();
    requestAnimationFrame(render);
}

export function setPlanetRadius(planetRadiusMeters) {
    if (!state || state.planetRadiusMeters === planetRadiusMeters) return;
    state.planetRadiusMeters = planetRadiusMeters;
    state.terrainIndex = null;
    rebuildWaterGeometry(state);
}

export function setWaterCycle(waterCycle) {
    if (!state) return;
    state.cycle = waterCycle ?? null;
    rebuildWaterGeometry(state);
}

export function clearWaterCycle() {
    if (!state) return;
    state.cycle = null;
    state.riverSegments = [];
    state.lakeGroups = [];
    state.metrics = createMetrics();
}

export function dispose() {
    if (window.__planetForgeWaterTest) delete window.__planetForgeWaterTest;
    state = null;
}

function createMetrics() {
    return {
        activeRiverSegmentCount: 0,
        riverPointCount: 0,
        lakeCellCount: 0,
        lakeGroupCount: 0,
        terrainVertexCount: 0,
        terrainSampleCount: 0,
        terrainFallbackCount: 0,
        minimumRiverElevationMeters: null,
        maximumRiverElevationMeters: null,
        visiblePixels: 0,
        maximumAlpha: 0.0
    };
}

function rebuildWaterGeometry(s) {
    s.metrics = createMetrics();
    ensureTerrainIndex(s);
    const paths = s.cycle?.activeRiverSegments ?? [];
    const lakeCells = s.cycle?.activeLakeCells ?? [];
    s.metrics.activeRiverSegmentCount = paths.length;
    s.metrics.lakeCellCount = lakeCells.length;
    s.riverSegments = buildRiverSegments(s, paths);
    s.lakeGroups = buildLakeGroups(s, lakeCells, s.cycle?.lakeFillFraction ?? 0.0);
    s.metrics.lakeGroupCount = s.lakeGroups.length;
}

function ensureTerrainIndex(s) {
    const geometry = getRetainedSurfaceGeometry();
    const tiles = geometry?.surfaceTiles ?? [];
    const radiusMeters = geometry?.physicalParameters?.radiusMeters ?? s.planetRadiusMeters;
    let vertexCount = 0;
    for (const tile of tiles) vertexCount += Math.min(tile?.surfaceVertexCount ?? 0, Math.floor((tile?.positions?.length ?? 0) / 3));

    if (s.terrainIndex && s.terrainVertexCount === vertexCount && vertexCount > 0 && Math.abs(s.planetRadiusMeters - radiusMeters) < 0.5) {
        s.metrics.terrainVertexCount = vertexCount;
        return;
    }

    s.planetRadiusMeters = radiusMeters;
    s.terrainVertexCount = vertexCount;
    const bins = new Map();
    for (const tile of tiles) {
        const positions = tile?.positions ?? [];
        const count = Math.min(tile?.surfaceVertexCount ?? 0, Math.floor(positions.length / 3));
        for (let vertex = 0; vertex < count; vertex++) {
            const offset = vertex * 3;
            const x = positions[offset];
            const y = positions[offset + 1];
            const z = positions[offset + 2];
            const radius = Math.hypot(x, y, z);
            if (!Number.isFinite(radius) || radius <= 0.0) continue;
            const direction = [x / radius, y / radius, z / radius];
            const elevationMeters = (radius - 1.0) * radiusMeters;
            const { latitudeIndex, longitudeIndex } = terrainBin(direction);
            const key = terrainBinKey(latitudeIndex, longitudeIndex);
            let bucket = bins.get(key);
            if (!bucket) {
                bucket = [];
                bins.set(key, bucket);
            }
            bucket.push({ direction, elevationMeters });
        }
    }

    s.terrainIndex = bins;
    s.metrics.terrainVertexCount = vertexCount;
}

function terrainBin(direction) {
    const latitude = Math.asin(clamp(direction[1], -1.0, 1.0));
    const longitude = Math.atan2(direction[2], direction[0]);
    const latitudeIndex = clamp(Math.floor(((latitude + Math.PI * 0.5) / Math.PI) * terrainLatitudeBins), 0, terrainLatitudeBins - 1);
    const longitudeIndex = wrapLongitudeBin(Math.floor(((longitude + Math.PI) / (Math.PI * 2.0)) * terrainLongitudeBins));
    return { latitudeIndex, longitudeIndex };
}

function terrainBinKey(latitudeIndex, longitudeIndex) {
    return `${latitudeIndex}:${wrapLongitudeBin(longitudeIndex)}`;
}

function wrapLongitudeBin(index) {
    return ((index % terrainLongitudeBins) + terrainLongitudeBins) % terrainLongitudeBins;
}

function sampleTerrainElevation(s, direction) {
    s.metrics.terrainSampleCount++;
    if (!s.terrainIndex || s.terrainIndex.size === 0) {
        s.metrics.terrainFallbackCount++;
        return 0.0;
    }

    const { latitudeIndex, longitudeIndex } = terrainBin(direction);
    const nearest = [];
    for (let latitudeOffset = -1; latitudeOffset <= 1; latitudeOffset++) {
        const candidateLatitude = latitudeIndex + latitudeOffset;
        if (candidateLatitude < 0 || candidateLatitude >= terrainLatitudeBins) continue;
        for (let longitudeOffset = -1; longitudeOffset <= 1; longitudeOffset++) {
            const bucket = s.terrainIndex.get(terrainBinKey(candidateLatitude, longitudeIndex + longitudeOffset));
            if (!bucket) continue;
            for (const sample of bucket) {
                const similarity = dot(direction, sample.direction);
                insertNearest(nearest, { similarity, elevationMeters: sample.elevationMeters }, 4);
            }
        }
    }

    if (nearest.length === 0) {
        s.metrics.terrainFallbackCount++;
        return 0.0;
    }

    let weightedElevation = 0.0;
    let weightTotal = 0.0;
    for (const sample of nearest) {
        const angularDistanceSignal = Math.max(1e-7, 1.0 - clamp(sample.similarity, -1.0, 1.0));
        const weight = 1.0 / (angularDistanceSignal * angularDistanceSignal);
        weightedElevation += sample.elevationMeters * weight;
        weightTotal += weight;
    }
    return weightedElevation / Math.max(weightTotal, 1e-9);
}

function insertNearest(items, candidate, maximumCount) {
    let index = 0;
    while (index < items.length && items[index].similarity >= candidate.similarity) index++;
    items.splice(index, 0, candidate);
    if (items.length > maximumCount) items.length = maximumCount;
}

function buildRiverSegments(s, paths) {
    const riverPaths = buildConnectedRiverPaths(paths);
    const segments = [];
    let minimumElevation = Number.POSITIVE_INFINITY;
    let maximumElevation = Number.NEGATIVE_INFINITY;

    for (const riverPath of riverPaths) {
        const points = [];
        let reachedOcean = false;
        for (let pathIndex = 0; pathIndex < riverPath.length && !reachedOcean; pathIndex++) {
            const path = riverPath[pathIndex];
            const from = unitPoint(path.fromX, path.fromY, path.fromZ);
            const to = unitPoint(path.toX, path.toY, path.toZ);
            const bend = chooseTerrainRiverBend(s, from, to);
            for (let sampleIndex = 0; sampleIndex < riverSamplesPerSegment; sampleIndex++) {
                if (pathIndex > 0 && sampleIndex === 0) continue;
                const t = sampleIndex / (riverSamplesPerSegment - 1);
                const direction = terrainRiverDirection(from, to, bend, t);
                const terrainElevation = sampleTerrainElevation(s, direction);
                minimumElevation = Math.min(minimumElevation, terrainElevation);
                maximumElevation = Math.max(maximumElevation, terrainElevation);
                if (terrainElevation < -50.0) {
                    reachedOcean = true;
                    break;
                }
                points.push(surfacePoint(direction, terrainElevation, s.planetRadiusMeters));
            }
        }

        if (points.length < 2) continue;
        segments.push({
            points,
            discharge: Math.max(...riverPath.map(path => clamp(path.relativeDischarge ?? 0.2, 0.08, 1.0))),
            streamOrder: Math.max(...riverPath.map(path => Math.max(1, path.streamOrder ?? 1)))
        });
        s.metrics.riverPointCount += points.length;
    }

    s.metrics.minimumRiverElevationMeters = Number.isFinite(minimumElevation) ? minimumElevation : null;
    s.metrics.maximumRiverElevationMeters = Number.isFinite(maximumElevation) ? maximumElevation : null;
    return segments;
}

function chooseTerrainRiverBend(s, from, to) {
    const angle = Math.acos(clamp(dot(from, to), -1.0, 1.0));
    if (angle < 0.00001) return 0.0;

    const routeNormal = safeNormalize(cross(from, to), createSurfaceTangent(from));
    const midpoint = sphericalInterpolate(from, to, 0.5);
    const bendCandidates = [-0.18, -0.09, 0.0, 0.09, 0.18];
    let bestBend = 0.0;
    let bestScore = Number.POSITIVE_INFINITY;
    for (const bend of bendCandidates) {
        const offset = bend * angle;
        const candidate = normalize([
            midpoint[0] * Math.cos(offset) + routeNormal[0] * Math.sin(offset),
            midpoint[1] * Math.cos(offset) + routeNormal[1] * Math.sin(offset),
            midpoint[2] * Math.cos(offset) + routeNormal[2] * Math.sin(offset)
        ]);
        const elevation = sampleTerrainElevation(s, candidate);
        const score = elevation + (Math.abs(bend) * 900.0);
        if (score >= bestScore) continue;
        bestScore = score;
        bestBend = bend;
    }
    return bestBend;
}

function terrainRiverDirection(from, to, bend, t) {
    const base = sphericalInterpolate(from, to, t);
    if (bend === 0.0 || t <= 0.0 || t >= 1.0) return base;

    const angle = Math.acos(clamp(dot(from, to), -1.0, 1.0));
    const routeNormal = safeNormalize(cross(from, to), createSurfaceTangent(base));
    const offset = bend * angle * Math.sin(Math.PI * t);
    return normalize([
        base[0] * Math.cos(offset) + routeNormal[0] * Math.sin(offset),
        base[1] * Math.cos(offset) + routeNormal[1] * Math.sin(offset),
        base[2] * Math.cos(offset) + routeNormal[2] * Math.sin(offset)
    ]);
}

function buildConnectedRiverPaths(paths) {
    if (!paths?.length) return [];

    const nodes = paths.map((path, index) => {
        const from = unitPoint(path.fromX, path.fromY, path.fromZ);
        const to = unitPoint(path.toX, path.toY, path.toZ);
        return { index, path, fromKey: riverPointKey(from), toKey: riverPointKey(to) };
    });
    const byFrom = new Map(nodes.map(node => [node.fromKey, node]));
    const targetKeys = new Set(nodes.map(node => node.toKey));
    const visited = new Set();
    const result = [];
    const starts = nodes
        .filter(node => !targetKeys.has(node.fromKey))
        .sort((first, second) => riverPriority(second.path) - riverPriority(first.path));

    for (const startNode of starts) appendConnectedRiverPath(startNode, byFrom, visited, result);
    for (const node of nodes) if (!visited.has(node.index)) appendConnectedRiverPath(node, byFrom, visited, result);
    return result;
}

function appendConnectedRiverPath(startNode, byFrom, visited, result) {
    const riverPath = [];
    let current = startNode;
    while (current && !visited.has(current.index)) {
        visited.add(current.index);
        riverPath.push(current.path);
        current = byFrom.get(current.toKey);
    }
    if (riverPath.length > 0) result.push(riverPath);
}

function riverPointKey(direction) {
    return `${direction[0].toFixed(6)}:${direction[1].toFixed(6)}:${direction[2].toFixed(6)}`;
}

function riverPriority(path) {
    return (Math.max(1, path.streamOrder ?? 1) * 10.0) + clamp(path.relativeDischarge ?? 0.2, 0.08, 1.0);
}

function buildLakeGroups(s, cells, fillFraction) {
    if (!cells?.length) return [];
    const groups = groupLakeCells(cells);
    const fillScale = 0.58 + (0.42 * Math.sqrt(clamp(fillFraction, 0.0, 1.0)));
    return groups.map(group => buildLakeGroup(s, group, fillScale)).filter(group => group.boundary.length >= 3);
}

function buildLakeGroup(s, cells, fillScale) {
    const elevations = cells.map(cell => sampleTerrainElevation(s, unitPoint(cell.x, cell.y, cell.z)));
    const surfaceElevationMeters = elevations.length > 0 ? Math.max(...elevations) : 0.0;
    const boundary = [];

    for (const cell of cells) {
        const direction = unitPoint(cell.x, cell.y, cell.z);
        const tangent = createSurfaceTangent(direction);
        const bitangent = safeNormalize(cross(direction, tangent), [0, 0, 1]);
        const radiusRadians = Math.max(0.0005, (cell.angularRadiusRadians ?? 0.01) * fillScale);
        for (let sampleIndex = 0; sampleIndex < 10; sampleIndex++) {
            const angle = sampleIndex / 10 * Math.PI * 2.0;
            const radial = [
                (tangent[0] * Math.cos(angle)) + (bitangent[0] * Math.sin(angle)),
                (tangent[1] * Math.cos(angle)) + (bitangent[1] * Math.sin(angle)),
                (tangent[2] * Math.cos(angle)) + (bitangent[2] * Math.sin(angle))
            ];
            const edgeDirection = normalize([
                direction[0] * Math.cos(radiusRadians) + radial[0] * Math.sin(radiusRadians),
                direction[1] * Math.cos(radiusRadians) + radial[1] * Math.sin(radiusRadians),
                direction[2] * Math.cos(radiusRadians) + radial[2] * Math.sin(radiusRadians)
            ]);
            boundary.push(surfacePoint(edgeDirection, surfaceElevationMeters, s.planetRadiusMeters));
        }
    }

    return { boundary, fillFraction };
}

function surfacePoint(direction, elevationMeters, planetRadiusMeters) {
    const visualRadius = 1.0 + (((elevationMeters * landReliefExaggeration) + visualWaterClearanceMeters) / Math.max(planetRadiusMeters, 1.0));
    return scale(direction, visualRadius);
}

function groupLakeCells(cells) {
    const directions = cells.map(cell => unitPoint(cell.x, cell.y, cell.z));
    const maximumRadius = cells.reduce((maximum, cell) => Math.max(maximum, cell.angularRadiusRadians ?? 0.01), 0.01);
    const binSize = Math.max(maximumRadius * 2.5, Math.PI / 180.0);
    const latitudeBinCount = Math.max(4, Math.ceil(Math.PI / binSize));
    const longitudeBinCount = Math.max(8, latitudeBinCount * 2);
    const buckets = new Map();

    for (let index = 0; index < cells.length; index++) {
        const key = lakeBinKey(directions[index], latitudeBinCount, longitudeBinCount);
        let bucket = buckets.get(key);
        if (!bucket) {
            bucket = [];
            buckets.set(key, bucket);
        }
        bucket.push(index);
    }

    const visited = new Uint8Array(cells.length);
    const groups = [];
    for (let start = 0; start < cells.length; start++) {
        if (visited[start]) continue;
        visited[start] = 1;
        const queue = [start];
        const group = [];
        while (queue.length > 0) {
            const currentIndex = queue.pop();
            group.push(cells[currentIndex]);
            const currentDirection = directions[currentIndex];
            const { latitudeIndex, longitudeIndex } = lakeBin(currentDirection, latitudeBinCount, longitudeBinCount);
            for (let latitudeOffset = -1; latitudeOffset <= 1; latitudeOffset++) {
                const candidateLatitude = latitudeIndex + latitudeOffset;
                if (candidateLatitude < 0 || candidateLatitude >= latitudeBinCount) continue;
                for (let longitudeOffset = -1; longitudeOffset <= 1; longitudeOffset++) {
                    const candidateLongitude = wrapBin(longitudeIndex + longitudeOffset, longitudeBinCount);
                    const candidates = buckets.get(`${candidateLatitude}:${candidateLongitude}`) ?? [];
                    for (const candidateIndex of candidates) {
                        if (visited[candidateIndex]) continue;
                        if (!lakeCellsTouch(cells[currentIndex], cells[candidateIndex], currentDirection, directions[candidateIndex])) continue;
                        visited[candidateIndex] = 1;
                        queue.push(candidateIndex);
                    }
                }
            }
        }
        groups.push(group);
    }
    return groups;
}

function lakeBin(direction, latitudeBinCount, longitudeBinCount) {
    const latitude = Math.asin(clamp(direction[1], -1.0, 1.0));
    const longitude = Math.atan2(direction[2], direction[0]);
    const latitudeIndex = clamp(Math.floor(((latitude + Math.PI * 0.5) / Math.PI) * latitudeBinCount), 0, latitudeBinCount - 1);
    const longitudeIndex = wrapBin(Math.floor(((longitude + Math.PI) / (Math.PI * 2.0)) * longitudeBinCount), longitudeBinCount);
    return { latitudeIndex, longitudeIndex };
}

function lakeBinKey(direction, latitudeBinCount, longitudeBinCount) {
    const { latitudeIndex, longitudeIndex } = lakeBin(direction, latitudeBinCount, longitudeBinCount);
    return `${latitudeIndex}:${longitudeIndex}`;
}

function wrapBin(index, count) {
    return ((index % count) + count) % count;
}

function lakeCellsTouch(first, second, firstDirection = null, secondDirection = null) {
    const firstPoint = firstDirection ?? unitPoint(first.x, first.y, first.z);
    const secondPoint = secondDirection ?? unitPoint(second.x, second.y, second.z);
    const separation = Math.acos(clamp(dot(firstPoint, secondPoint), -1.0, 1.0));
    const firstRadius = first.angularRadiusRadians ?? 0.01;
    const secondRadius = second.angularRadiusRadians ?? 0.01;
    return separation <= (firstRadius + secondRadius) * 1.18;
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
    });
    canvas.addEventListener('wheel', event => {
        const zoomFactor = Math.exp(event.deltaY * 0.0015);
        const minimumAltitudeRatio = minimumCameraAltitudeMeters / Math.max(s.planetRadiusMeters, 1.0);
        const altitudeRatio = clamp(s.distance - 1.0, minimumAltitudeRatio, maximumCameraAltitudeRatio);
        s.distance = 1.0 + clamp(altitudeRatio * zoomFactor, minimumAltitudeRatio, maximumCameraAltitudeRatio);
    }, { passive: true });
}

function installVisualTestApi() {
    if (!new URLSearchParams(window.location.search).has('visualTest')) return;
    window.__planetForgeWaterTest = {
        measure() {
            if (!state) return createMetrics();
            draw(state);
            return measureVisibleWater(state);
        }
    };
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
    if (s.riverSegments.length === 0 && s.lakeGroups.length === 0) return;
    if ((s.distance - 1.0) * s.planetRadiusMeters <= localTransitionAltitudeMeters) return;

    const aspect = canvas.width / Math.max(canvas.height, 1);
    const projection = perspective(verticalFieldOfViewRadians, aspect, 0.002, 20.0);
    const eye = orbitEye(s.yaw, s.pitch, s.distance);
    const viewProjection = multiply(projection, lookAt(eye, [0, 0, 0], [0, 1, 0]));

    context.globalCompositeOperation = 'source-over';
    context.lineCap = 'round';
    context.lineJoin = 'round';
    for (const lake of s.lakeGroups) drawLake(context, lake, eye, viewProjection, canvas.width, canvas.height);
    for (const river of s.riverSegments) drawRiver(context, river, eye, viewProjection, canvas.width, canvas.height);
}

function drawLake(context, lake, eye, viewProjection, width, height) {
    const projected = lake.boundary
        .filter(point => isFrontFacing(point, eye))
        .map(point => project(viewProjection, point, width, height))
        .filter(Boolean);
    const hull = convexHull(projected);
    if (hull.length < 3) return;

    context.beginPath();
    context.moveTo(hull[0][0], hull[0][1]);
    for (let index = 1; index < hull.length; index++) context.lineTo(hull[index][0], hull[index][1]);
    context.closePath();
    context.fillStyle = `rgba(18, 82, 101, ${0.64 + lake.fillFraction * 0.16})`;
    context.fill();
    context.strokeStyle = `rgba(64, 137, 151, ${0.38 + lake.fillFraction * 0.18})`;
    context.lineWidth = 0.85;
    context.stroke();
}

function drawRiver(context, river, eye, viewProjection, width, height) {
    const visibleRuns = [];
    let currentRun = [];
    for (const point of river.points) {
        if (!isFrontFacing(point, eye)) {
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

    const orderScale = clamp((river.streamOrder - 1) * 0.16, 0.0, 0.58);
    const coreWidth = 0.55 + (river.discharge * 1.55) + orderScale;
    for (const points of visibleRuns) {
        drawSmoothPolyline(context, points, `rgba(7, 31, 39, ${0.34 + river.discharge * 0.12})`, coreWidth + 0.85);
        drawSmoothPolyline(context, points, `rgba(27, 91, 108, ${0.46 + river.discharge * 0.16})`, coreWidth);
    }
}

function drawSmoothPolyline(context, points, strokeStyle, lineWidth) {
    context.strokeStyle = strokeStyle;
    context.lineWidth = lineWidth;
    context.beginPath();
    context.moveTo(points[0][0], points[0][1]);
    if (points.length === 2) {
        context.lineTo(points[1][0], points[1][1]);
    } else {
        for (let index = 1; index < points.length - 1; index++) {
            const current = points[index];
            const next = points[index + 1];
            context.quadraticCurveTo(current[0], current[1], (current[0] + next[0]) * 0.5, (current[1] + next[1]) * 0.5);
        }
        const last = points[points.length - 1];
        context.lineTo(last[0], last[1]);
    }
    context.stroke();
}

function measureVisibleWater(s) {
    const metrics = { ...s.metrics };
    if (s.canvas.width === 0 || s.canvas.height === 0) return metrics;
    const pixels = s.context.getImageData(0, 0, s.canvas.width, s.canvas.height).data;
    let visiblePixels = 0;
    let maximumAlpha = 0.0;
    for (let offset = 3; offset < pixels.length; offset += 4) {
        const alpha = pixels[offset] / 255.0;
        maximumAlpha = Math.max(maximumAlpha, alpha);
        if (alpha > 0.05) visiblePixels++;
    }
    metrics.visiblePixels = visiblePixels;
    metrics.maximumAlpha = maximumAlpha;
    return metrics;
}

function convexHull(points) {
    if (points.length <= 3) return points;
    const sorted = [...points].sort((a, b) => a[0] === b[0] ? a[1] - b[1] : a[0] - b[0]);
    const lower = [];
    for (const point of sorted) {
        while (lower.length >= 2 && cross2d(lower[lower.length - 2], lower[lower.length - 1], point) <= 0) lower.pop();
        lower.push(point);
    }
    const upper = [];
    for (let index = sorted.length - 1; index >= 0; index--) {
        const point = sorted[index];
        while (upper.length >= 2 && cross2d(upper[upper.length - 2], upper[upper.length - 1], point) <= 0) upper.pop();
        upper.push(point);
    }
    lower.pop();
    upper.pop();
    return lower.concat(upper);
}

function cross2d(origin, first, second) {
    return ((first[0] - origin[0]) * (second[1] - origin[1])) - ((first[1] - origin[1]) * (second[0] - origin[0]));
}

function createSurfaceTangent(direction) {
    const primary = cross(direction, [0, 1, 0]);
    if (Math.hypot(...primary) >= 0.0001) return normalize(primary);
    return safeNormalize(cross(direction, [1, 0, 0]), [0, 0, 1]);
}

function sphericalInterpolate(from, to, t) {
    const cosine = clamp(dot(from, to), -1.0, 1.0);
    const angle = Math.acos(cosine);
    if (angle < 0.00001) return from.slice();
    const sine = Math.sin(angle);
    const fromWeight = Math.sin((1.0 - t) * angle) / sine;
    const toWeight = Math.sin(t * angle) / sine;
    return normalize([
        from[0] * fromWeight + to[0] * toWeight,
        from[1] * fromWeight + to[1] * toWeight,
        from[2] * fromWeight + to[2] * toWeight
    ]);
}

function isFrontFacing(point, eye) {
    return dot(point, eye) > dot(point, point);
}

function project(matrix, point, width, height) {
    const x = point[0], y = point[1], z = point[2];
    const clipX = matrix[0]*x + matrix[4]*y + matrix[8]*z + matrix[12];
    const clipY = matrix[1]*x + matrix[5]*y + matrix[9]*z + matrix[13];
    const clipZ = matrix[2]*x + matrix[6]*y + matrix[10]*z + matrix[14];
    const clipW = matrix[3]*x + matrix[7]*y + matrix[11]*z + matrix[15];
    if (clipW <= 0.0 || clipZ < -clipW || clipZ > clipW) return null;
    return [((clipX / clipW) * 0.5 + 0.5) * width, (0.5 - (clipY / clipW) * 0.5) * height];
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

function unitPoint(x, y, z) { return normalize([x, y, z]); }
function normalize(v) { const magnitude = Math.hypot(v[0],v[1],v[2]) || 1.0; return [v[0]/magnitude,v[1]/magnitude,v[2]/magnitude]; }
function safeNormalize(v, fallback) { const magnitude=Math.hypot(v[0],v[1],v[2]); return magnitude < 1e-8 ? fallback.slice() : [v[0]/magnitude,v[1]/magnitude,v[2]/magnitude]; }
function cross(a,b) { return [a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]]; }
function dot(a,b) { return a[0]*b[0]+a[1]*b[1]+a[2]*b[2]; }
function scale(v,s) { return [v[0]*s,v[1]*s,v[2]*s]; }
function clamp(value,min,max) { return Math.max(min,Math.min(max,value)); }