let state;

const verticalFieldOfViewRadians = Math.PI / 4.2;
const minimumCameraAltitudeMeters = 3.0;
const maximumCameraAltitudeRatio = 4.2;
const localTransitionAltitudeMeters = 20_000.0;
const localExitAltitudeMeters = 25_000.0;
const riverSurfaceOffset = 1.003;
const lakeSurfaceOffset = 1.0025;
const endpointPrecision = 100_000;
const waterFieldStudyVersion = 1;

const waterChallenges = [
    {
        eyebrow: 'FIELD TEST 1 OF 3 · READ THE TERRAIN',
        title: 'Rain has started. Where should persistent flow appear first?',
        explanation: 'Choose a hypothesis before advancing the hydrology model.',
        correct: 'drainage',
        options: [
            { id: 'ridges', label: 'Along the highest ridges', hint: 'High terrain intercepts rain first.' },
            { id: 'drainage', label: 'In connected terrain lows', hint: 'Gravity concentrates runoff into drainage paths.' },
            { id: 'uniform', label: 'Evenly across the surface', hint: 'Rainfall should wet every location equally.' }
        ],
        observation: cycle => `${cycle?.activeRiverSegmentCount ?? 0} drainage segments are now carrying persistent flow. Water concentrates along terrain-defined lows rather than remaining evenly distributed.`
    },
    {
        eyebrow: 'FIELD TEST 2 OF 3 · FOLLOW THE WATER',
        title: 'Runoff reaches a closed depression. What happens next?',
        explanation: 'Predict how a basin changes downstream flow before you observe another century.',
        correct: 'store',
        options: [
            { id: 'drain', label: 'It drains immediately', hint: 'The depression should not change downstream timing.' },
            { id: 'store', label: 'It stores water before spilling', hint: 'A closed basin must fill before water can continue downstream.' },
            { id: 'ignore', label: 'It has no effect on runoff', hint: 'Only rivers matter to the water cycle.' }
        ],
        observation: cycle => `${cycle?.activeLakeCount ?? 0} closed basins are now persistently filled. Basins temporarily store runoff, so connected downstream flow develops more slowly than precipitation itself.`
    },
    {
        eyebrow: 'FIELD TEST 3 OF 3 · BUILD A RIVER NETWORK',
        title: 'Two tributaries join. What should happen downstream?',
        explanation: 'Use what you learned about contributing area and connected runoff.',
        correct: 'grow',
        options: [
            { id: 'grow', label: 'Discharge should increase', hint: 'The downstream channel receives water from both upstream catchments.' },
            { id: 'same', label: 'Discharge should stay constant', hint: 'Channel size should not depend on contributing area.' },
            { id: 'fall', label: 'Discharge should decrease', hint: 'Merging tributaries should weaken the resulting channel.' }
        ],
        observation: cycle => {
            const segments = cycle?.activeRiverSegments ?? [];
            const strongest = segments.reduce((maximum, segment) => Math.max(maximum, segment.relativeDischarge ?? 0), 0);
            return `The largest connected channels now reach ${(strongest * 100).toFixed(0)}% of the model's reference discharge. Contributing area accumulates downstream, so merged channels become the dominant flow paths.`;
        }
    }
];

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
        riverChains: [],
        lakeCells: [],
        cycle: null,
        gameplayKey: null,
        gameplay: createGameplayState(),
        gameplayHost: null,
        gameplayButton: null,
        gameplayButtonClickHandler: null
    };

    ensureGameplayStyles();
    installInput(state);
    requestAnimationFrame(render);
}

export function setPlanetRadius(planetRadiusMeters) {
    if (state) state.planetRadiusMeters = planetRadiusMeters;
}

export function setWaterCycle(waterCycle) {
    if (!state) return;

    const previousYears = state.cycle?.simulatedYears ?? 0;
    state.cycle = waterCycle ?? null;
    state.paths = waterCycle?.activeRiverSegments ?? [];
    state.riverChains = buildRiverChains(state.paths);
    state.lakeCells = waterCycle?.activeLakeCells ?? [];

    if (state.gameplay.selected && !state.gameplay.awaitingResolution && (waterCycle?.simulatedYears ?? 0) > previousYears) {
        state.gameplay.awaitingResolution = true;
        state.gameplay.observationStartYear = previousYears;
    }

    if (state.gameplay.awaitingResolution && (waterCycle?.simulatedYears ?? 0) - state.gameplay.observationStartYear >= 100) {
        resolveCurrentChallenge(state, waterCycle);
    }
}

export function clearWaterCycle() {
    if (!state) return;
    state.paths = [];
    state.riverChains = [];
    state.lakeCells = [];
    state.cycle = null;
}

export function dispose() {
    if (state?.gameplayButton && state.gameplayButtonClickHandler) {
        state.gameplayButton.removeEventListener('click', state.gameplayButtonClickHandler);
    }
    state = null;
}

function createGameplayState() {
    return {
        round: 0,
        score: 0,
        selected: null,
        awaitingResolution: false,
        observationStartYear: 0,
        lastFeedback: null
    };
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
    syncWaterGameplay(state);
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
    if (s.riverChains.length === 0 && s.lakeCells.length === 0) return;

    const altitudeMeters = Math.max(0.0, (s.distance - 1.0) * s.planetRadiusMeters);
    if (altitudeMeters <= localTransitionAltitudeMeters) return;

    const aspect = canvas.width / Math.max(canvas.height, 1);
    const projection = perspective(verticalFieldOfViewRadians, aspect, 0.002, 20.0);
    const eye = orbitEye(s.yaw, s.pitch, s.distance);
    const view = lookAt(eye, [0, 0, 0], [0, 1, 0]);
    const viewProjection = multiply(projection, view);

    context.globalCompositeOperation = 'source-over';
    drawLakeCells(context, s.lakeCells, s.cycle?.lakeFillFraction ?? 0.0, eye, viewProjection, canvas.width, canvas.height);

    context.lineCap = 'round';
    context.lineJoin = 'round';
    for (const chain of s.riverChains) {
        drawRiverChain(context, chain, eye, viewProjection, canvas.width, canvas.height);
    }
}

function drawLakeCells(context, cells, fillFraction, eye, viewProjection, width, height) {
    if (!cells?.length) return;

    const fillScale = 0.58 + (0.42 * Math.sqrt(clamp(fillFraction, 0.0, 1.0)));
    for (const group of groupLakeCells(cells)) {
        const boundary = [];
        for (const cell of group) {
            const direction = unitPoint(cell.x, cell.y, cell.z);
            const tangent = createSurfaceTangent(direction);
            const bitangent = safeNormalize(cross(direction, tangent), [0, 0, 1]);
            const radiusRadians = Math.max(0.0005, (cell.angularRadiusRadians ?? 0.01) * fillScale);

            for (let sampleIndex = 0; sampleIndex < 8; sampleIndex++) {
                const angle = sampleIndex / 8 * Math.PI * 2;
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
                const surfacePoint = scale(edgeDirection, lakeSurfaceOffset);
                if (dot(surfacePoint, eye) <= 1.0) continue;
                const projected = project(viewProjection, surfacePoint, width, height);
                if (projected) boundary.push(projected);
            }
        }

        const hull = convexHull(boundary);
        if (hull.length < 3) continue;

        context.beginPath();
        context.moveTo(hull[0][0], hull[0][1]);
        for (let index = 1; index < hull.length; index++) context.lineTo(hull[index][0], hull[index][1]);
        context.closePath();
        context.fillStyle = `rgba(24, 73, 85, ${0.56 + fillFraction * 0.14})`;
        context.fill();
        context.strokeStyle = `rgba(49, 103, 113, ${0.28 + fillFraction * 0.12})`;
        context.lineWidth = 0.8;
        context.stroke();
    }
}

function groupLakeCells(cells) {
    const remaining = new Set(cells.map((_, index) => index));
    const groups = [];

    while (remaining.size > 0) {
        const start = remaining.values().next().value;
        remaining.delete(start);
        const queue = [start];
        const group = [];

        while (queue.length > 0) {
            const currentIndex = queue.pop();
            const current = cells[currentIndex];
            group.push(current);
            for (const candidateIndex of [...remaining]) {
                if (!lakeCellsTouch(current, cells[candidateIndex])) continue;
                remaining.delete(candidateIndex);
                queue.push(candidateIndex);
            }
        }

        groups.push(group);
    }

    return groups;
}

function lakeCellsTouch(first, second) {
    const firstDirection = unitPoint(first.x, first.y, first.z);
    const secondDirection = unitPoint(second.x, second.y, second.z);
    const separation = Math.acos(clamp(dot(firstDirection, secondDirection), -1.0, 1.0));
    const firstRadius = first.angularRadiusRadians ?? 0.01;
    const secondRadius = second.angularRadiusRadians ?? 0.01;
    return separation <= (firstRadius + secondRadius) * 1.18;
}

function convexHull(points) {
    if (points.length <= 3) return points;
    const sorted = [...points].sort((first, second) => first[0] === second[0] ? first[1] - second[1] : first[0] - second[0]);
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
    if (Math.hypot(primary[0], primary[1], primary[2]) >= 0.0001) return normalize(primary);
    return safeNormalize(cross(direction, [1, 0, 0]), [0, 0, 1]);
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
    const sourcePaths = paths
        .filter(path => incomingCount.get(endpointKey(path.fromX, path.fromY, path.fromZ)) === 0)
        .sort((first, second) => (second.relativeDischarge ?? 0) - (first.relativeDischarge ?? 0));

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
    const rawPoints = [];
    const discharges = [];
    const streamOrders = [];
    let current = start;

    while (current && !visited.has(segmentKey(current))) {
        visited.add(segmentKey(current));
        if (rawPoints.length === 0) rawPoints.push(unitPoint(current.fromX, current.fromY, current.fromZ));
        rawPoints.push(unitPoint(current.toX, current.toY, current.toZ));
        discharges.push(clamp(current.relativeDischarge ?? 0.2, 0.08, 1.0));
        streamOrders.push(current.streamOrder ?? 1);
        current = outgoing.get(endpointKey(current.toX, current.toY, current.toZ));
    }

    if (rawPoints.length < 2) return null;
    return {
        points: meanderChain(rawPoints),
        discharge: Math.max(...discharges),
        streamOrder: Math.max(...streamOrders)
    };
}

function meanderChain(points) {
    const result = [];
    for (let segmentIndex = 0; segmentIndex < points.length - 1; segmentIndex++) {
        const from = points[segmentIndex];
        const to = points[segmentIndex + 1];
        const seed = hashEndpointPair(from, to);
        const phase = ((seed % 4096) / 4096) * Math.PI * 2;
        const amplitude = 0.0022 + (((seed >>> 12) % 1000) / 1000) * 0.0028;
        const normal = safeNormalize(cross(from, to), [0, 1, 0]);
        const samples = 7;

        for (let sampleIndex = 0; sampleIndex < samples; sampleIndex++) {
            if (segmentIndex > 0 && sampleIndex === 0) continue;
            const t = sampleIndex / (samples - 1);
            const base = sphericalInterpolate(from, to, t);
            const envelope = Math.sin(Math.PI * t);
            const wave = (Math.sin((t * Math.PI * 2) + phase) * 0.62) + (Math.sin((t * Math.PI * 4) + phase * 0.57) * 0.22);
            const offset = amplitude * envelope * wave;
            result.push(scale(normalize([base[0] + normal[0] * offset, base[1] + normal[1] * offset, base[2] + normal[2] * offset]), riverSurfaceOffset));
        }
    }
    return result;
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
    const orderScale = clamp((chain.streamOrder - 1) * 0.14, 0.0, 0.48);
    const coreWidth = 0.62 + discharge * 1.65 + orderScale;

    for (const points of visibleRuns) {
        drawSmoothPolyline(context, points, `rgba(12, 35, 40, ${0.42 + discharge * 0.12})`, coreWidth + 0.9);
        drawSmoothPolyline(context, points, `rgba(35, 105, 121, ${0.58 + discharge * 0.16})`, coreWidth);
    }
}

function drawSmoothPolyline(context, points, strokeStyle, lineWidth) {
    context.strokeStyle = strokeStyle;
    context.lineWidth = lineWidth;
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

function syncWaterGameplay(s) {
    const dock = document.querySelector('.water-gameplay-dock');
    if (!dock) {
        detachGameplayButton(s);
        s.gameplayHost = null;
        return;
    }

    loadGameplayForCurrentWorld(s);
    let host = dock.querySelector('.water-field-study');
    if (!host) {
        host = document.createElement('section');
        host.className = 'water-field-study';
        const status = dock.querySelector('.water-status-line');
        if (status) dock.insertBefore(host, status);
        else dock.insertBefore(host, dock.querySelector('.water-cycle-button'));
    }
    s.gameplayHost = host;

    const button = dock.querySelector('.water-cycle-button');
    attachGameplayButton(s, button);
    renderGameplayPanel(s, dock, host, button);
    syncTimelapseOverlay(s);
}

function loadGameplayForCurrentWorld(s) {
    const key = getGameplayStorageKey();
    if (s.gameplayKey === key) return;

    s.gameplayKey = key;
    s.gameplay = createGameplayState();
    try {
        const saved = localStorage.getItem(key);
        if (!saved) return;
        const parsed = JSON.parse(saved);
        s.gameplay.round = clamp(Math.trunc(parsed.round ?? 0), 0, waterChallenges.length);
        s.gameplay.score = clamp(Math.trunc(parsed.score ?? 0), 0, waterChallenges.length);
        s.gameplay.lastFeedback = parsed.lastFeedback ?? null;
    } catch {
        s.gameplay = createGameplayState();
    }
}

function getGameplayStorageKey() {
    const status = document.querySelector('.run-topbar-status span')?.textContent ?? '';
    const seed = status.match(/Seed\s+(-?\d+)/)?.[1] ?? 'unknown';
    return `planetforge-water-field-study-v${waterFieldStudyVersion}:${seed}`;
}

function saveGameplay(s) {
    if (!s.gameplayKey) return;
    const persisted = {
        round: s.gameplay.round,
        score: s.gameplay.score,
        lastFeedback: s.gameplay.lastFeedback
    };
    try {
        localStorage.setItem(s.gameplayKey, JSON.stringify(persisted));
    } catch {
    }
}

function renderGameplayPanel(s, dock, host, button) {
    const copy = dock.querySelector('.water-gameplay-copy');
    const heading = copy?.querySelector('h2');
    const paragraph = copy?.querySelector('p');
    const gameplay = s.gameplay;
    const feedbackKey = gameplay.lastFeedback ? `${gameplay.lastFeedback.correct}:${gameplay.lastFeedback.headline}:${gameplay.lastFeedback.observation}` : 'none';
    const renderKey = `${gameplay.round}|${gameplay.score}|${gameplay.selected ?? 'none'}|${gameplay.awaitingResolution}|${feedbackKey}`;

    if (gameplay.round >= waterChallenges.length) {
        if (heading) heading.textContent = 'Field study complete: you can now read this planet’s water cycle.';
        if (paragraph) paragraph.textContent = 'You tested runoff, basin storage and river-network growth against the generated world instead of simply advancing time.';
        if (host.dataset.renderKey !== renderKey) {
            host.dataset.renderKey = renderKey;
            host.innerHTML = `
                <div class="water-field-complete">
                    <span class="water-field-kicker">HYDROLOGY FIELD STUDY COMPLETE</span>
                    <strong>${gameplay.score} / ${waterChallenges.length} predictions confirmed</strong>
                    <p>The important result is not the score: you now have evidence for why terrain lows collect runoff, why basins delay connected flow, and why downstream discharge grows with contributing area.</p>
                </div>`;
        }
        updateGameplayButton(button, gameplay);
        return;
    }

    const challenge = waterChallenges[gameplay.round];
    if (heading) heading.textContent = `Investigation ${gameplay.round + 1} of ${waterChallenges.length}: make a prediction.`;
    if (paragraph) paragraph.textContent = 'Read the generated planet, commit a hypothesis, then let a century of hydrology test it.';

    if (host.dataset.renderKey === renderKey) {
        updateGameplayButton(button, gameplay);
        return;
    }
    host.dataset.renderKey = renderKey;

    const feedback = gameplay.lastFeedback
        ? `<div class="water-field-feedback ${gameplay.lastFeedback.correct ? 'confirmed' : 'revised'}"><strong>${escapeHtml(gameplay.lastFeedback.headline)}</strong><p>${escapeHtml(gameplay.lastFeedback.observation)}</p></div>`
        : '';
    const options = challenge.options.map(option => `
        <button type="button" class="water-prediction-option ${gameplay.selected === option.id ? 'selected' : ''}" data-prediction="${option.id}" ${gameplay.awaitingResolution ? 'disabled' : ''}>
            <strong>${escapeHtml(option.label)}</strong>
            <small>${escapeHtml(option.hint)}</small>
        </button>`).join('');

    host.innerHTML = `
        ${feedback}
        <div class="water-field-header">
            <span>${challenge.eyebrow}</span>
            <em>SCORE ${gameplay.score}/${waterChallenges.length}</em>
        </div>
        <h3>${escapeHtml(challenge.title)}</h3>
        <p>${escapeHtml(challenge.explanation)}</p>
        <div class="water-prediction-grid">${options}</div>`;

    for (const optionButton of host.querySelectorAll('.water-prediction-option')) {
        optionButton.addEventListener('click', () => {
            if (gameplay.awaitingResolution) return;
            gameplay.selected = optionButton.dataset.prediction;
            gameplay.lastFeedback = null;
            renderGameplayPanel(s, dock, host, button);
        });
    }

    updateGameplayButton(button, gameplay);
}

function updateGameplayButton(button, gameplay) {
    if (!button) return;
    const complete = gameplay.round >= waterChallenges.length;
    button.style.display = complete ? 'none' : '';
    button.disabled = complete || gameplay.selected === null || gameplay.awaitingResolution;
    if (complete) return;
    button.textContent = gameplay.awaitingResolution ? 'OBSERVING THE PLANET…' : gameplay.selected ? 'COMMIT PREDICTION & OBSERVE' : 'CHOOSE A PREDICTION FIRST';
}

function attachGameplayButton(s, button) {
    if (!button || s.gameplayButton === button) return;
    detachGameplayButton(s);
    s.gameplayButton = button;
    s.gameplayButtonClickHandler = () => {
        if (!s.gameplay.selected || s.gameplay.awaitingResolution || s.gameplay.round >= waterChallenges.length) return;
        s.gameplay.awaitingResolution = true;
        s.gameplay.observationStartYear = s.cycle?.simulatedYears ?? 0;
    };
    button.addEventListener('click', s.gameplayButtonClickHandler);
}

function detachGameplayButton(s) {
    if (s.gameplayButton && s.gameplayButtonClickHandler) s.gameplayButton.removeEventListener('click', s.gameplayButtonClickHandler);
    s.gameplayButton = null;
    s.gameplayButtonClickHandler = null;
}

function resolveCurrentChallenge(s, cycle) {
    const gameplay = s.gameplay;
    const challenge = waterChallenges[gameplay.round];
    if (!challenge || !gameplay.selected) return;

    const correct = gameplay.selected === challenge.correct;
    if (correct) gameplay.score++;
    gameplay.lastFeedback = {
        correct,
        headline: correct ? 'Prediction confirmed.' : 'Prediction revised by the evidence.',
        observation: challenge.observation(cycle)
    };
    gameplay.round++;
    gameplay.selected = null;
    gameplay.awaitingResolution = false;
    gameplay.observationStartYear = cycle?.simulatedYears ?? 0;
    saveGameplay(s);
}

function syncTimelapseOverlay(s) {
    const overlay = document.querySelector('.water-timelapse-overlay');
    if (!overlay || s.gameplay.round >= waterChallenges.length) return;
    const challenge = waterChallenges[s.gameplay.round];
    const label = overlay.querySelector('span');
    const strong = overlay.querySelector('strong');
    const small = overlay.querySelector('small');
    if (label) label.textContent = 'TESTING YOUR HYDROLOGY PREDICTION';
    if (strong) strong.textContent = `FIELD TEST ${s.gameplay.round + 1} / ${waterChallenges.length}`;
    if (small) small.textContent = challenge.eyebrow.replace(/^FIELD TEST \d OF \d · /, '');
}

function ensureGameplayStyles() {
    if (document.getElementById('planetforge-water-field-study-styles')) return;
    const style = document.createElement('style');
    style.id = 'planetforge-water-field-study-styles';
    style.textContent = `
        .water-field-study { margin: 18px 0; padding: 18px; border: 1px solid rgba(82,174,209,.42); border-radius: 18px; background: linear-gradient(145deg, rgba(9,30,37,.94), rgba(7,19,23,.92)); box-shadow: inset 0 0 0 1px rgba(89,199,239,.04); }
        .water-field-header { display:flex; justify-content:space-between; gap:12px; align-items:center; margin-bottom:8px; }
        .water-field-header span, .water-field-kicker { color:#71c8e8; font:800 .68rem ui-monospace, SFMono-Regular, Menlo, monospace; letter-spacing:.12em; }
        .water-field-header em { color:#8ca8b2; font:700 .65rem ui-monospace, SFMono-Regular, Menlo, monospace; font-style:normal; }
        .water-field-study h3 { margin:6px 0 7px; color:#edf8fb; font-size:1.08rem; line-height:1.25; }
        .water-field-study > p { margin:0 0 14px; color:#91a9b1; font-size:.84rem; line-height:1.45; }
        .water-prediction-grid { display:grid; grid-template-columns:repeat(3,minmax(0,1fr)); gap:9px; }
        .water-prediction-option { min-height:112px; padding:13px; text-align:left; border:1px solid #26444f; border-radius:14px; background:rgba(10,28,34,.8); color:#dbe9ed; transition:border-color .15s ease, background .15s ease, transform .15s ease; }
        .water-prediction-option:not(:disabled):active { transform:scale(.985); }
        .water-prediction-option.selected { border-color:#59c7ef; background:linear-gradient(145deg, rgba(24,74,91,.94), rgba(9,39,49,.92)); box-shadow:inset 0 0 0 1px rgba(89,199,239,.18); }
        .water-prediction-option strong { display:block; margin-bottom:7px; color:inherit; font-size:.83rem; line-height:1.25; }
        .water-prediction-option small { color:#77939c; font-size:.72rem; line-height:1.35; }
        .water-field-feedback { margin-bottom:15px; padding:13px 14px; border-radius:13px; border-left:3px solid #59c7ef; background:rgba(13,46,57,.72); }
        .water-field-feedback.revised { border-left-color:#e8b15d; background:rgba(67,48,20,.45); }
        .water-field-feedback strong { display:block; margin-bottom:4px; color:#eaf7fa; }
        .water-field-feedback p { margin:0; color:#9fb5bc; font-size:.78rem; line-height:1.4; }
        .water-field-complete { display:flex; flex-direction:column; gap:7px; }
        .water-field-complete strong { color:#dff6fd; font-size:1.15rem; }
        .water-field-complete p { margin:0; color:#91a9b1; line-height:1.45; }
        @media (max-width:760px) {
            .water-field-study { margin:14px 0; padding:14px; }
            .water-prediction-grid { grid-template-columns:1fr; }
            .water-prediction-option { min-height:0; padding:12px; }
        }
    `;
    document.head.appendChild(style);
}

function unitPoint(x, y, z) {
    return normalize([x, y, z]);
}

function endpointKey(x, y, z) {
    return `${Math.round(x * endpointPrecision)},${Math.round(y * endpointPrecision)},${Math.round(z * endpointPrecision)}`;
}

function segmentKey(path) {
    return `${endpointKey(path.fromX, path.fromY, path.fromZ)}>${endpointKey(path.toX, path.toY, path.toZ)}`;
}

function hashEndpointPair(from, to) {
    let hash = 2166136261;
    for (const value of [...from, ...to]) {
        const quantized = Math.round(value * endpointPrecision);
        hash ^= quantized;
        hash = Math.imul(hash, 16777619);
    }
    return hash >>> 0;
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

function normalize(v) {
    const length = Math.hypot(v[0], v[1], v[2]) || 1;
    return [v[0]/length, v[1]/length, v[2]/length];
}

function safeNormalize(v, fallback) {
    const length = Math.hypot(v[0], v[1], v[2]);
    return length < 0.000001 ? fallback.slice() : [v[0]/length, v[1]/length, v[2]/length];
}

function scale(v, amount) { return [v[0] * amount, v[1] * amount, v[2] * amount]; }
function cross(a,b) { return [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]; }
function dot(a,b) { return a[0]*b[0] + a[1]*b[1] + a[2]*b[2]; }
function clamp(value,min,max) { return Math.max(min, Math.min(max, value)); }
function escapeHtml(value) { const div = document.createElement('div'); div.textContent = value ?? ''; return div.innerHTML; }
