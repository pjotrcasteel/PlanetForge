// Quench-crust rafts on a convecting magma ocean.
//
// Physical basis: a magma ocean's quench crust is rigid and brittle, so
// surface deformation localises at raft boundaries (lava lakes; Io's Loki
// Patera). Rafts are rigid bodies that move with the convective surface flow
// at their centre. They are born at upwellings, where the surface diverges,
// and consumed at downwellings or when their crust exceeds the foundering
// thickness. A raft's area is the spherical Voronoi cell of its centre: a
// tessellation proxy for raft shapes, not a fracture-mechanics solution.
//
// The convective flow is the gradient of a source/sink potential, so it
// genuinely diverges and converges; it is not curl noise. Rafts welded into a
// plate move rigidly: each plate rotates about its own Euler pole, the
// least-squares fit of the flow under it (plates are dragged by the melt but
// cannot deform). New crust nucleates only where plates pull apart; it is
// consumed where they converge. Every raft carries its plate's accumulated
// rotation, so surface detail is anchored to the moving crust. Displayed
// motion is rate-limited (time-lapse aliasing); crust ages are physical.

export const maximumRafts = 8192;
export const binSlots = 8;

export function mulberry32(seed) {
    let a = seed >>> 0;
    return () => {
        a = (a + 0x6D2B79F5) >>> 0;
        let t = a;
        t = Math.imul(t ^ (t >>> 15), t | 1);
        t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

export function randomDirection(random) {
    const z = random() * 2 - 1;
    const a = random() * Math.PI * 2;
    const r = Math.sqrt(1 - z * z);
    return [r * Math.cos(a), z, r * Math.sin(a)];
}

function normalizeInPlace(v, o) {
    const l = Math.hypot(v[o], v[o + 1], v[o + 2]) || 1;
    v[o] /= l; v[o + 1] /= l; v[o + 2] /= l;
}

export function createRaftSystem(seed, spacingRadians) {
    const random = mulberry32(seed ^ 0x51ED270B);
    const cells = [];
    // Equal numbers of sources (upwelling sheets) and sinks (downwellings)
    // keep the net surface divergence near zero.
    for (let i = 0; i < 36; i++) {
        cells.push({
            center: randomDirection(random),
            strength: i % 2 === 0 ? 1.0 : -1.0,
            width: 0.22 + random() * 0.22,
            drift: randomDirection(random),
        });
    }
    const system = {
        random,
        cells,
        count: 0,
        spacing: spacingRadians,
        position: new Float32Array(maximumRafts * 3),
        velocity: new Float32Array(maximumRafts * 3),
        // Accumulated rotation (quaternion x, y, z, w) of the crust carried by
        // each raft: maps material (crust-fixed) to world coordinates.
        orientation: new Float32Array(maximumRafts * 4),
        plateOmega: new Map(),
        phase: new Float32Array(maximumRafts),
        phaseRate: new Float32Array(maximumRafts),
        cluster: new Float32Array(maximumRafts),
        // Displayed plate boundary state: a welding seam freezes over and a
        // new rift opens over ~2 s instead of switching. displayId is the
        // plate drawn, linkPartner the plate it is welding to / rifting
        // from, linkFactor the boundary strength (1 = open rift).
        displayId: new Float32Array(maximumRafts),
        linkPartner: new Float32Array(maximumRafts).fill(-1),
        linkFactor: new Float32Array(maximumRafts).fill(1),
        linkRate: new Float32Array(maximumRafts),
        nextCluster: 0,
        initialSpacing: spacingRadians,
        seamShare: 1,
        removed: new Uint8Array(maximumRafts),
        binGrid: 0,
        bins: null,
        binOverflow: 0,
    };
    const target = targetCount(spacingRadians);
    rebuildBins(system);
    for (let i = 0; i < target * 3 && system.count < target; i++) trySpawn(system, randomDirection(random), 0.85, true);
    rebuildBins(system);
    assignInitialPlates(system);
    return system;
}

// Quench crust breaks into composite plates of very different sizes (lava
// lakes: a few large plates, many small ones), not a uniform honeycomb.
// Multiplicatively weighted Voronoi domains group rafts into such plates.
function assignInitialPlates(system) {
    const domains = Math.max(6, Math.round(system.count / 12));
    const seeds = [];
    for (let k = 0; k < domains; k++) {
        const w = 0.3 + 1.7 * system.random() ** 2;
        seeds.push([...randomDirection(system.random), 1 / (w * w)]);
    }
    for (let i = 0; i < system.count; i++) {
        const o = i * 3, x = system.position[o], y = system.position[o + 1], z = system.position[o + 2];
        let best = Infinity, id = 0;
        for (let k = 0; k < domains; k++) {
            const s = seeds[k];
            const d = (1 - (x * s[0] + y * s[1] + z * s[2])) * s[3];
            if (d < best) { best = d; id = k; }
        }
        system.cluster[i] = id;
        system.displayId[i] = id;
    }
    system.nextCluster = domains;
}

// Nearest raft belonging to a different plate (-1 if none nearby).
function nearestOtherPlate(system, i) {
    const g = system.binGrid;
    const o = i * 3;
    const px = system.position[o], py = system.position[o + 1], pz = system.position[o + 2];
    const [ix, iy, iz] = binKey(system, px, py, pz);
    let best = 4.0, bestIndex = -1;
    for (let dz = -1; dz <= 1; dz++) for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) {
        const x = ix + dx, y = iy + dy, z = iz + dz;
        if (x < 0 || y < 0 || z < 0 || x >= g || y >= g || z >= g) continue;
        const flat = (x + g * (y + g * z)) * binSlots;
        for (let s = 0; s < binSlots; s++) {
            const index = system.bins[flat + s];
            if (index < 0) break;
            if (system.cluster[index] === system.cluster[i]) continue;
            const q = index * 3;
            const d = Math.hypot(px - system.position[q], py - system.position[q + 1], pz - system.position[q + 2]);
            if (d < best) { best = d; bestIndex = index; }
        }
    }
    return bestIndex;
}

export function targetCount(spacingRadians) {
    return Math.min(maximumRafts, Math.round(4 * Math.PI / (0.866 * spacingRadians * spacingRadians)));
}

// Surface velocity (radians per unit display time) of the convective flow at p.
export function convectiveFlow(system, p, out) {
    out[0] = 0; out[1] = 0; out[2] = 0;
    for (const cell of system.cells) {
        const c = cell.center;
        const cosine = Math.max(-1, Math.min(1, p[0] * c[0] + p[1] * c[1] + p[2] * c[2]));
        const theta = Math.acos(cosine);
        if (theta < 1e-5 || theta > cell.width * 4.0) continue;
        // Tangent at p pointing away from the cell centre.
        let tx = p[0] * cosine - c[0], ty = p[1] * cosine - c[1], tz = p[2] * cosine - c[2];
        const tl = Math.hypot(tx, ty, tz) || 1;
        const x = theta / cell.width;
        const magnitude = cell.strength * x * Math.exp(1 - x * x);
        out[0] += tx / tl * magnitude; out[1] += ty / tl * magnitude; out[2] += tz / tl * magnitude;
    }
    return out;
}

function binKey(system, x, y, z) {
    const g = system.binGrid;
    const ix = Math.min(g - 1, Math.max(0, Math.floor((x + 1) * 0.5 * g)));
    const iy = Math.min(g - 1, Math.max(0, Math.floor((y + 1) * 0.5 * g)));
    const iz = Math.min(g - 1, Math.max(0, Math.floor((z + 1) * 0.5 * g)));
    return [ix, iy, iz];
}

export function rebuildBins(system) {
    // Bin edge >= 1.3 raft spacings, so the nearest two centres of any point
    // lie in its 3x3x3 neighbourhood.
    const grid = Math.max(4, Math.min(48, Math.floor(2.0 / (system.spacing * 1.3))));
    if (system.binGrid !== grid || !system.bins) {
        system.binGrid = grid;
        system.bins = new Float32Array(grid * grid * grid * binSlots);
    }
    system.bins.fill(-1);
    system.binOverflow = 0;
    const counts = new Uint8Array(grid * grid * grid);
    for (let i = 0; i < system.count; i++) {
        const o = i * 3;
        const [ix, iy, iz] = binKey(system, system.position[o], system.position[o + 1], system.position[o + 2]);
        const flat = ix + grid * (iy + grid * iz);
        if (counts[flat] >= binSlots) { system.binOverflow++; continue; }
        system.bins[flat * binSlots + counts[flat]] = i;
        counts[flat]++;
    }
}

function nearestDistance(system, p, skip = -1) {
    const g = system.binGrid;
    const [ix, iy, iz] = binKey(system, p[0], p[1], p[2]);
    let best = 4.0;
    let bestIndex = -1;
    for (let dz = -1; dz <= 1; dz++) for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) {
        const x = ix + dx, y = iy + dy, z = iz + dz;
        if (x < 0 || y < 0 || z < 0 || x >= g || y >= g || z >= g) continue;
        const flat = (x + g * (y + g * z)) * binSlots;
        for (let s = 0; s < binSlots; s++) {
            const index = system.bins[flat + s];
            if (index < 0) break;
            if (index === skip || system.removed[index]) continue;
            const o = index * 3;
            const d = Math.hypot(p[0] - system.position[o], p[1] - system.position[o + 1], p[2] - system.position[o + 2]);
            if (d < best) { best = d; bestIndex = index; }
        }
    }
    return { distance: best, index: bestIndex };
}

function trySpawn(system, p, minimumFactor, randomPhase) {
    if (system.count >= maximumRafts) return false;
    let parent = -1;
    if (system.bins) {
        const near = nearestDistance(system, p);
        if (near.distance < system.spacing * minimumFactor) return false;
        parent = near.index;
    } else {
        for (let i = 0; i < system.count; i++) {
            const o = i * 3;
            const d = Math.hypot(p[0] - system.position[o], p[1] - system.position[o + 1], p[2] - system.position[o + 2]);
            if (d < system.spacing * minimumFactor) return false;
        }
    }
    const i = system.count++;
    system.position.set(p, i * 3);
    system.phase[i] = randomPhase ? system.random() : 0.0;
    // Crust ages to the foundering thickness over 3-6 s of display time.
    system.phaseRate[i] = 1.0 / (3.0 + system.random() * 3.0);
    // New crust accretes to the plate at the spreading seam and moves with it.
    system.cluster[i] = parent >= 0 ? system.cluster[parent] : system.nextCluster++;
    system.displayId[i] = system.cluster[i];
    system.linkPartner[i] = -1;
    system.linkFactor[i] = 1;
    system.linkRate[i] = 0;
    if (parent >= 0) {
        system.orientation.copyWithin(i * 4, parent * 4, parent * 4 + 4);
        system.displayId[i] = system.displayId[parent];
        system.linkPartner[i] = system.linkPartner[parent];
        system.linkFactor[i] = system.linkFactor[parent];
        system.linkRate[i] = system.linkRate[parent];
        // A gap inside one plate (cell coarsening) is not a rift: the plate's
        // own crust closes over it.
        if (!randomPhase && !bordersOtherPlate(system, p, system.cluster[parent])) system.phase[i] = system.phase[parent];
    } else {
        system.orientation.set([0, 0, 0, 1], i * 4);
    }
    system.removed[i] = 0;
    if (system.bins) {
        const g = system.binGrid;
        const [ix, iy, iz] = binKey(system, p[0], p[1], p[2]);
        const flat = (ix + g * (iy + g * iz)) * binSlots;
        for (let s = 0; s < binSlots; s++) {
            if (system.bins[flat + s] < 0) { system.bins[flat + s] = i; break; }
        }
    }
    return true;
}

// True if a raft of another plate lies within 1.6 spacings of p.
function bordersOtherPlate(system, p, cluster) {
    const g = system.binGrid;
    const [ix, iy, iz] = binKey(system, p[0], p[1], p[2]);
    const limit = system.spacing * 1.6;
    for (let dz = -1; dz <= 1; dz++) for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) {
        const x = ix + dx, y = iy + dy, z = iz + dz;
        if (x < 0 || y < 0 || z < 0 || x >= g || y >= g || z >= g) continue;
        const flat = (x + g * (y + g * z)) * binSlots;
        for (let s = 0; s < binSlots; s++) {
            const index = system.bins[flat + s];
            if (index < 0) break;
            if (system.removed[index] || system.cluster[index] === cluster) continue;
            const o = index * 3;
            if (Math.hypot(p[0] - system.position[o], p[1] - system.position[o + 1], p[2] - system.position[o + 2]) < limit) return true;
        }
    }
    return false;
}

// Rotate unit vector v (in place at offset o) by the rotation vector w (rad).
function rotateInPlace(v, o, wx, wy, wz) {
    const angle = Math.hypot(wx, wy, wz);
    if (angle < 1e-12) return;
    const kx = wx / angle, ky = wy / angle, kz = wz / angle;
    const c = Math.cos(angle), s = Math.sin(angle);
    const x = v[o], y = v[o + 1], z = v[o + 2];
    const d = (kx * x + ky * y + kz * z) * (1 - c);
    v[o] = x * c + (ky * z - kz * y) * s + kx * d;
    v[o + 1] = y * c + (kz * x - kx * z) * s + ky * d;
    v[o + 2] = z * c + (kx * y - ky * x) * s + kz * d;
}

// Pre-multiply quaternion q (at offset o) by the rotation vector w (rad).
function composeRotation(q, o, wx, wy, wz) {
    const angle = Math.hypot(wx, wy, wz);
    if (angle < 1e-12) return;
    const s = Math.sin(angle * 0.5) / angle;
    const ax = wx * s, ay = wy * s, az = wz * s, aw = Math.cos(angle * 0.5);
    const bx = q[o], by = q[o + 1], bz = q[o + 2], bw = q[o + 3];
    const x = aw * bx + ax * bw + ay * bz - az * by;
    const y = aw * by - ax * bz + ay * bw + az * bx;
    const z = aw * bz + ax * by - ay * bx + az * bw;
    const w = aw * bw - ax * bx - ay * by - az * bz;
    const l = Math.hypot(x, y, z, w) || 1;
    q[o] = x / l; q[o + 1] = y / l; q[o + 2] = z / l; q[o + 3] = w / l;
}

// Euler vector of each plate: least-squares rigid rotation of the flow under
// it, sum (I - p p^T) w = sum p x v, eased over ~1.5 s (plate inertia /
// viscous coupling) so plates do not jitter.
function updatePlateKinematics(system, flowAt, wallDt) {
    const acc = new Map();
    for (let i = 0; i < system.count; i++) {
        const o = i * 3;
        const x = system.position[o], y = system.position[o + 1], z = system.position[o + 2];
        const v = flowAt(i);
        let a = acc.get(system.cluster[i]);
        if (!a) { a = new Float64Array(9); acc.set(system.cluster[i], a); }
        // Symmetric matrix entries (xx, yy, zz, xy, xz, yz) and torque.
        a[0] += 1 - x * x; a[1] += 1 - y * y; a[2] += 1 - z * z;
        a[3] -= x * y; a[4] -= x * z; a[5] -= y * z;
        a[6] += y * v[2] - z * v[1]; a[7] += z * v[0] - x * v[2]; a[8] += x * v[1] - y * v[0];
    }
    const ease = Math.min(1, wallDt / 1.5);
    const next = new Map();
    for (const [id, a] of acc) {
        const r = 0.05;
        const m00 = a[0] + r, m11 = a[1] + r, m22 = a[2] + r, m01 = a[3], m02 = a[4], m12 = a[5];
        const c00 = m11 * m22 - m12 * m12, c01 = m02 * m12 - m01 * m22, c02 = m01 * m12 - m02 * m11;
        const c11 = m00 * m22 - m02 * m02, c12 = m01 * m02 - m00 * m12, c22 = m00 * m11 - m01 * m01;
        const det = m00 * c00 + m01 * c01 + m02 * c02 || 1e-12;
        const tx = (c00 * a[6] + c01 * a[7] + c02 * a[8]) / det;
        const ty = (c01 * a[6] + c11 * a[7] + c12 * a[8]) / det;
        const tz = (c02 * a[6] + c12 * a[7] + c22 * a[8]) / det;
        const prev = system.plateOmega.get(id) ?? [tx, ty, tz];
        next.set(id, [prev[0] + (tx - prev[0]) * ease, prev[1] + (ty - prev[1]) * ease, prev[2] + (tz - prev[2]) * ease]);
    }
    system.plateOmega = next;
}

function compact(system) {
    let write = 0;
    for (let read = 0; read < system.count; read++) {
        if (system.removed[read]) continue;
        if (write !== read) {
            system.position.copyWithin(write * 3, read * 3, read * 3 + 3);
            system.velocity.copyWithin(write * 3, read * 3, read * 3 + 3);
            system.orientation.copyWithin(write * 4, read * 4, read * 4 + 4);
            system.phase[write] = system.phase[read];
            system.phaseRate[write] = system.phaseRate[read];
            system.cluster[write] = system.cluster[read];
            system.displayId[write] = system.displayId[read];
            system.linkPartner[write] = system.linkPartner[read];
            system.linkFactor[write] = system.linkFactor[read];
            system.linkRate[write] = system.linkRate[read];
        }
        system.removed[write] = 0;
        write++;
    }
    system.count = write;
}

// Advance rafts by wall-clock dt. displaySpeed is the flow speed in rad/s of
// display time (rate-limited). Returns the number of foundering events.
export function advanceRafts(system, wallDt, displaySpeed, spacingRadians) {
    system.spacing = spacingRadians;
    const flow = [0, 0, 0];
    const p = [0, 0, 0];
    let foundered = 0;
    for (const cell of system.cells) {
        // Convective cells wander slowly (they are not fixed features).
        const c = cell.center;
        const d = cell.drift;
        c[0] += d[0] * wallDt * 0.01; c[1] += d[1] * wallDt * 0.01; c[2] += d[2] * wallDt * 0.01;
        normalizeInPlace(c, 0);
        if (system.random() < wallDt * 0.05) {
            const n = randomDirection(system.random);
            d[0] = n[0]; d[1] = n[1]; d[2] = n[2];
        }
    }
    // The flow drags each plate; plates move as rigid caps.
    const flowAt = i => {
        const o = i * 3;
        p[0] = system.position[o]; p[1] = system.position[o + 1]; p[2] = system.position[o + 2];
        return convectiveFlow(system, p, flow);
    };
    updatePlateKinematics(system, flowAt, wallDt);
    for (let i = 0; i < system.count; i++) {
        const o = i * 3;
        const w = system.plateOmega.get(system.cluster[i]) ?? [0, 0, 0];
        const wx = w[0] * displaySpeed, wy = w[1] * displaySpeed, wz = w[2] * displaySpeed;
        const x = system.position[o], y = system.position[o + 1], z = system.position[o + 2];
        system.velocity[o] = wy * z - wz * y; system.velocity[o + 1] = wz * x - wx * z; system.velocity[o + 2] = wx * y - wy * x;
        rotateInPlace(system.position, o, wx * wallDt, wy * wallDt, wz * wallDt);
        composeRotation(system.orientation, i * 4, wx * wallDt, wy * wallDt, wz * wallDt);
        // Crust thickens towards the foundering thickness and stays there;
        // it is renewed only by rifting and consumed at convergent margins.
        system.phase[i] = Math.min(1, system.phase[i] + system.phaseRate[i] * wallDt);
        if (system.linkPartner[i] >= 0) {
            system.linkFactor[i] += system.linkRate[i] * wallDt;
            if (system.linkFactor[i] <= 0 || system.linkFactor[i] >= 1) {
                system.displayId[i] = system.cluster[i];
                system.linkPartner[i] = -1;
                system.linkFactor[i] = 1;
                system.linkRate[i] = 0;
            }
        }
    }
    rebuildBins(system);
    // Crowding at downwellings: the older of two converging rafts subducts.
    for (let i = 0; i < system.count; i++) {
        if (system.removed[i]) continue;
        const o = i * 3;
        p[0] = system.position[o]; p[1] = system.position[o + 1]; p[2] = system.position[o + 2];
        const near = nearestDistance(system, p, i);
        if (near.index >= 0 && near.distance < spacingRadians * 0.5) {
            const older = system.phase[i] > system.phase[near.index] ? i : near.index;
            system.removed[older] = 1;
            foundered++;
        }
    }
    compact(system);
    // As crust strengthens towards the rheological transition, rafts weld
    // into larger plates: excess rafts merge into their neighbours.
    const excess = system.count - Math.round(targetCount(spacingRadians) * 1.1);
    if (excess > 0) {
        const merges = Math.min(excess, Math.max(4, Math.round(system.count * wallDt * 0.6)));
        for (let m = 0; m < merges; m++) system.removed[Math.floor(system.random() * system.count)] = 1;
        compact(system);
    }
    rebuildBins(system);
    // Divergence opens room: fresh crust nucleates in the largest gaps.
    const deficit = targetCount(spacingRadians) - system.count;
    const attempts = Math.max(8, Math.min(600, Math.abs(deficit) * 2 + 40));
    for (let a = 0; a < attempts && system.count < targetCount(spacingRadians) * 1.05; a++) {
        trySpawn(system, randomDirection(system.random), 0.95, false);
    }
    rebuildBins(system);
    weldPlates(system, wallDt, spacingRadians);
    return foundered;
}

// As the crust strengthens towards the rheological transition, neighbouring
// plates weld: the mean plate grows from ~10 to ~60 rafts.
function riftLargestPlate(system, sizes, meanPlate) {
    let biggest = -1, size = 0;
    for (const [id, n] of sizes) if (n > size) { size = n; biggest = id; }
    if (biggest < 0 || size < Math.max(12, meanPlate * 2.2)) return;
    const members = [];
    for (let i = 0; i < system.count; i++) if (system.cluster[i] === biggest) members.push(i);
    let cx = 0, cy = 0, cz = 0;
    for (const i of members) { cx += system.position[i * 3]; cy += system.position[i * 3 + 1]; cz += system.position[i * 3 + 2]; }
    const n = randomDirection(system.random);
    const bend = randomDirection(system.random);
    const id = system.nextCluster++;
    let moved = 0;
    for (const i of members) {
        const x = system.position[i * 3] - cx / members.length, y = system.position[i * 3 + 1] - cy / members.length, z = system.position[i * 3 + 2] - cz / members.length;
        const curve = 0.6 * (bend[0] * x + bend[1] * y + bend[2] * z) ** 2;
        if (n[0] * x + n[1] * y + n[2] * z + curve > 0) { system.cluster[i] = id; moved++; }
    }
    if (moved === 0 || moved === members.length) {
        for (const i of members) system.cluster[i] = biggest;
        return;
    }
    // The new rift opens gradually.
    for (const i of members) {
        if (system.cluster[i] !== id) continue;
        system.displayId[i] = id;
        system.linkPartner[i] = system.displayId[members.find(k => system.cluster[k] === biggest)] ?? biggest;
        system.linkFactor[i] = 0.02;
        system.linkRate[i] = 0.6;
    }
    sizes.set(id, moved);
    sizes.set(biggest, size - moved);
}

// Plate `from` welds onto `to`: kinematically one plate at once, while the
// glowing seam between them freezes over (~2 s of display time).
function weldInto(system, from, to, toDisplay) {
    for (let k = 0; k < system.count; k++) {
        if (system.cluster[k] !== from) continue;
        system.cluster[k] = to;
        if (system.linkPartner[k] < 0) {
            system.linkPartner[k] = toDisplay;
            system.linkFactor[k] = 1;
            system.linkRate[k] = -0.5;
        }
    }
}

function weldPlates(system, wallDt, spacingRadians) {
    const strength = Math.min(1, Math.max(0, (spacingRadians / system.initialSpacing - 1) / 2.2));
    const meanPlate = 10 + 50 * strength;
    const sizes = new Map();
    for (let i = 0; i < system.count; i++) sizes.set(system.cluster[i], (sizes.get(system.cluster[i]) ?? 0) + 1);
    let plateCount = sizes.size;
    const target = Math.max(4, system.count / meanPlate);
    const welds = Math.min(Math.max(0, Math.ceil(plateCount - target)), Math.max(1, Math.round(plateCount * wallDt * 0.2)));
    // Plates far above the mean size rift apart along a curved break, which
    // keeps a spread of plate sizes instead of one welded shell.
    if (system.random() < wallDt * 0.8) riftLargestPlate(system, sizes, meanPlate);
    plateCount = sizes.size;
    for (let w = 0; w < welds && system.count > 1; w++) {
        // Small plates weld first (they have the most boundary per area).
        let i = Math.floor(system.random() * system.count);
        for (let t = 0; t < 12; t++) {
            const k = Math.floor(system.random() * system.count);
            if (sizes.get(system.cluster[k]) < sizes.get(system.cluster[i])) i = k;
        }
        const j = nearestOtherPlate(system, i);
        if (j < 0) continue;
        const from = system.cluster[i], to = system.cluster[j];
        weldInto(system, from, to, system.displayId[j]);
        sizes.set(to, sizes.get(to) + sizes.get(from));
        sizes.delete(from);
        plateCount--;
    }
    // Single-raft fragments cannot survive inside a welded plate.
    for (let i = 0; i < system.count; i++) {
        if (sizes.get(system.cluster[i]) > 2) continue;
        const j = nearestOtherPlate(system, i);
        if (j < 0) continue;
        const from = system.cluster[i], to = system.cluster[j];
        weldInto(system, from, to, system.displayId[j]);
        sizes.set(to, sizes.get(to) + sizes.get(from));
        sizes.delete(from);
    }
    // Share of raft boundaries that are open plate boundaries: all new crust
    // forms there, so fewer open seams must spread faster.
    let open = 0, sampled = 0;
    for (let s = 0; s < 192 && system.count > 1; s++) {
        const i = Math.floor(system.random() * system.count);
        const o = i * 3;
        const near = nearestDistance(system, [system.position[o], system.position[o + 1], system.position[o + 2]], i);
        if (near.index < 0) continue;
        sampled++;
        if (system.cluster[near.index] !== system.cluster[i]) open++;
    }
    if (sampled > 0) system.seamShare += (open / sampled - system.seamShare) * Math.min(1, wallDt * 2);
}
