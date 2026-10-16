// Planet evolution: magma ocean -> quench-crust rafts -> stagnant lid with
// heat-pipe volcanism -> plate tectonics. One continuous simulation whose
// regime follows from the 0-D thermal state (thermalHistory.js). The surface
// fields live on the GPU (WebGL2, RGBA32F equirectangular 1024x512).
//
// Scientific anchors (see docs/handover/CURRENT.md for the full list):
//  - Stefan quench-crust growth h = 2 lambda sqrt(kappa t), radiative balance.
//  - Solomatov (2000) magma-ocean convection velocity; Einstein-Roscoe viscosity.
//  - Moore & Webb (2013) heat-pipe lid; Sizova et al. (2010) subduction onset.
//  - Parsons & Sclater (1977) age-depth; Airy isostasy for continental crust.
//  - Arc volcanism 100-200 km behind trenches; fixed-mantle hotspots.
// Display liberties are listed in DEMO_LIBERTIES and surfaced in the HUD.

import {
    PARAMETERS, SECONDS_PER_YEAR, createThermalHistory, advanceThermalHistory, physicalRate,
    crystalFraction, crustThicknessMeters, crustSurfaceKelvin, formatDuration, rheologicalTransitionKelvin,
} from './thermalHistory.js';
import { createRaftSystem, advanceRafts, mulberry32, randomDirection, maximumRafts, binSlots } from './crustRafts.js';
import { emissionLinearRadiance, thermalMaterialShader } from '../thermalRadiance.js';
import {
    EARTH_LIBERTIES, loadEarthAssets, createEarthHistory, prepareEarthHistory, disposeEarthHistory, startEarthHistory,
    stepEarthHistory, deriveEarthHistory, earthHistoryStatus, earthLatestStartMyr,
} from './earthHistory.js';

export const DEMO_LIBERTIES = Object.freeze([
    'Raft and plate motion is shown rate-limited; physical speeds set crust ages and seam widths.',
    'Raft shapes use a Voronoi tessellation of rigid raft centres (proxy for fracture mechanics) whose fractures are roughened with fractal noise; contraction cracks inside rafts are procedural sub-grid detail. Rafts weld into irregular composite plates whose sizes are stochastic.',
    'Plates move as rigid spherical caps whose Euler poles are least-squares fits to the convective flow beneath them; welding seams freeze over and new rifts open over ~2 s of display time.',
    'Seams open at the modelled crust-renewal velocity (raft spacing / crust lifetime), scaled by local divergence.',
    'Glowing seams are drawn ~500x wider than their physical (metre-scale) width so they resolve from orbit; their temperature-vs-age profile is physical.',
    'Time runs logarithmically (one decade per 3.5 s) until the selected cap.',
    'Eruption glow persists ~0.6 s of display time so individual eruptions are visible in time-lapse.',
    'Magma-ocean impacts are paced in display time (one every 5-12 s) and aimed at the visible hemisphere; their crater size distribution (N(>R) ~ R^-2) is physical.',
    'Quench-crust slabs are drawn paler than basaltic glass (closer to the lunar anorthosite flotation look) so their relief reads; the incandescent surface is limb-darkened by an assumed silicate-vapour atmosphere.',
    'The star field and Milky Way background are decorative.',
    'Relief is vertically exaggerated 8x; camera auto-exposure adapts to the scene brightness.',
    'Relief and albedo detail below the ~40 km simulation grid is procedural, scaled by the simulated terrain.',
    'Lid-era felsic (TTG) crust production rate is a hypothesis.',
    'Steam rain-out takes ~1 Myr (geologically instantaneous); playback slows to 0.25 Myr/s while it happens so it can be watched.',
    "Oceans: Earth's present water inventory (~2.6 km global layer) rains out of the steam atmosphere after the crust solidifies; sea level fills the simulated basins.",
    'Clouds are an instantaneous weather snapshot (circulation belts, mid-latitude cyclones); at this time-lapse real clouds would average into haze.',
    'Climate (ice caps, snow line, vegetation belts) is a latitude/altitude parameterisation, not a coupled climate model. Land plants appear ~470 Ma.',
    'Global temperature follows an approximate palaeotemperature curve (hot post-steam Hadean, Huronian and Cryogenian snowballs, Phanerozoic ice ages and hothouses after Scotese et al. 2021) with ~2x polar amplification.',
]);

const SEAM_WIDENING = 500;
const GRID_WIDTH = 1024;
const GRID_HEIGHT = 512;
const AGE_TABLE_SIZE = 256;
const AGE_LOG_MIN = -2.0;   // 10 ms
const AGE_LOG_MAX = 12.0;   // ~32 kyr
const MAX_PLATES = 24;
const MAX_PLUMES = 32;
const MAX_VENTS = 24;
const MAX_IMPACTS = 16;
const MYR = 1e6 * SECONDS_PER_YEAR;
const RELIEF_EXAGGERATION = 8.0;
// Sunlight at 1 AU in the thermal LUT's units (2200 K luminance = 1):
// L_sun * Omega_sun / pi for a 5772 K photosphere; reflected = albedo x this.
const SOLAR_REFLECTANCE_SCALE = 0.0277;
const OCEAN_LAYER_KM = 2.6;          // 1.335e9 km^3 over 5.1e8 km^2
const STEAM_RAINOUT_MYR = 0.6;       // condensation e-folding once the surface is solid
const PRESENT_MYR = 4567;
const MAX_MAGMA_IMPACTS = 6;
const LAND_PLANTS_MYR = PRESENT_MYR - 470;  // embryophytes, Ordovician
const ATMOSPHERE_SHELL = 0.022;
const RAINOUT_PLAYBACK_MYR = 0.1;    // Myr per wall second during rain-out

// ---------------------------------------------------------------- shaders --

const fullscreenVertex = `#version 300 es
void main() {
    vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}`;

// Background sky: a procedural star field (power-law magnitudes, stellar
// colours from ~3000 K to ~12000 K) and a faint Milky Way band with dust
// lanes. Fixed to the inertial frame, so it turns as the camera orbits.
const starFragment = `#version 300 es
precision highp float;
uniform mat4 uInverseViewProjection;
uniform vec3 uCamera;
uniform vec2 uViewport;
out vec4 outColor;
float h13(vec3 p) { p = fract(p * 0.1031); p += dot(p, p.zyx + 31.32); return fract((p.x + p.y) * p.z); }
float vn(vec3 p) {
    vec3 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(h13(i), h13(i + vec3(1, 0, 0)), f.x), mix(h13(i + vec3(0, 1, 0)), h13(i + vec3(1, 1, 0)), f.x), f.y),
               mix(mix(h13(i + vec3(0, 0, 1)), h13(i + vec3(1, 0, 1)), f.x), mix(h13(i + vec3(0, 1, 1)), h13(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}
float fbm(vec3 p) { float s = 0.0, a = 0.5; for (int i = 0; i < 5; i++) { s += a * vn(p); p = p * 2.03 + 7.1; a *= 0.5; } return s; }
vec3 starColour(float t) {
    return mix(mix(vec3(1.0, 0.62, 0.36), vec3(1.0, 0.93, 0.84), smoothstep(0.0, 0.4, t)), vec3(0.72, 0.82, 1.0), smoothstep(0.55, 1.0, t));
}
vec3 starLayer(vec3 d, float scale, float density, float px, float seed) {
    vec3 cell = floor(d * scale);
    if (h13(cell + seed) > density) return vec3(0.0);
    vec3 sp = normalize((cell + 0.2 + 0.6 * vec3(h13(cell + seed + 1.7), h13(cell + seed + 5.1), h13(cell + seed + 9.3))) / scale);
    float ang = length(sp - d);
    float mag = pow(h13(cell + seed + 3.3), 7.0);
    float core = exp(-ang * ang / (px * px * 0.5));
    return starColour(h13(cell + seed + 7.7)) * core * (0.02 + 1.4 * mag);
}
void main() {
    vec2 ndc = gl_FragCoord.xy / uViewport * 2.0 - 1.0;
    vec4 far = uInverseViewProjection * vec4(ndc, 1.0, 1.0);
    vec3 d = normalize(far.xyz / far.w - uCamera);
    float px = 1.2 * length(fwidth(d));
    vec3 c = starLayer(d, 40.0, 0.12, px, 0.0) + starLayer(d, 110.0, 0.05, px, 11.0) * 0.6 + starLayer(d, 260.0, 0.02, px, 23.0) * 0.4;
    // Milky Way: unresolved starlight along the galactic plane, cut by dust.
    vec3 gn = normalize(vec3(0.35, 0.8, -0.48));
    float b = dot(d, gn);
    float band = exp(-b * b / 0.018) * (0.4 + 0.8 * fbm(d * 4.0));
    float dust = smoothstep(0.45, 0.75, fbm(d * 7.0 + 3.0)) * exp(-b * b / 0.004);
    c += vec3(0.010, 0.0095, 0.0085) * band * (1.0 - 0.8 * dust);
    c += starLayer(d, 400.0, 0.12 * exp(-b * b / 0.02), px, 41.0) * 0.3;
    outColor = vec4(pow(c / (1.0 + c), vec3(1.0 / 2.2)), 1.0);
}`;

const sphereCommon = `
const float PI = 3.14159265358979;
vec3 dirFromUv(vec2 uv) {
    float lon = (uv.x - 0.5) * 2.0 * PI;
    float lat = (uv.y - 0.5) * PI;
    return vec3(cos(lat) * cos(lon), sin(lat), cos(lat) * sin(lon));
}
vec2 uvFromDir(vec3 d) {
    return vec2(atan(d.z, d.x) / (2.0 * PI) + 0.5, asin(clamp(d.y, -1.0, 1.0)) / PI + 0.5);
}
ivec2 texelOf(vec3 d, ivec2 size) {
    ivec2 t = ivec2(floor(uvFromDir(d) * vec2(size)));
    t.x = (t.x % size.x + size.x) % size.x;
    t.y = clamp(t.y, 0, size.y - 1);
    return t;
}
ivec2 wrapTexel(ivec2 t, ivec2 size) {
    t.x = (t.x % size.x + size.x) % size.x;
    t.y = clamp(t.y, 0, size.y - 1);
    return t;
}
float hash12(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}
float hash13(vec3 p) {
    p = fract(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return fract((p.x + p.y) * p.z);
}
float noise3(vec3 p) {
    vec3 i = floor(p); vec3 f = fract(p); f = f * f * (3.0 - 2.0 * f);
    float n000 = hash13(i), n100 = hash13(i + vec3(1,0,0)), n010 = hash13(i + vec3(0,1,0)), n110 = hash13(i + vec3(1,1,0));
    float n001 = hash13(i + vec3(0,0,1)), n101 = hash13(i + vec3(1,0,1)), n011 = hash13(i + vec3(0,1,1)), n111 = hash13(i + vec3(1,1,1));
    return mix(mix(mix(n000, n100, f.x), mix(n010, n110, f.x), f.y), mix(mix(n001, n101, f.x), mix(n011, n111, f.x), f.y), f.z);
}
`;

// Initial lid: one immobile plate, crust formed at lid onset.
const initLidFragment = `#version 300 es
precision highp float;
uniform float uTimeMyr;
layout(location = 0) out vec4 out0;
layout(location = 1) out vec4 out1;
void main() {
    out0 = vec4(0.0, uTimeMyr, 0.0, 0.0);
    out1 = vec4(-1e6, 0.0, 0.0, 0.0);
}`;

// Plate partition at mobile-lid onset (and plate rifting events).
const partitionFragment = `#version 300 es
precision highp float;
precision highp int;
uniform highp sampler2D uS0;
uniform highp sampler2D uS1;
uniform int uMode;            // 0 = full partition, 1 = split plate uSplitPlate
uniform int uSeedCount;
uniform vec3 uSeeds[${MAX_PLATES}];
uniform int uSplitPlate;
uniform int uNewPlate;
uniform vec3 uSplitNormal;
uniform float uWarp;
layout(location = 0) out vec4 out0;
layout(location = 1) out vec4 out1;
${sphereCommon}
void main() {
    ivec2 size = textureSize(uS0, 0);
    ivec2 me = ivec2(gl_FragCoord.xy);
    vec3 p = dirFromUv((vec2(me) + 0.5) / vec2(size));
    vec4 s0 = texelFetch(uS0, me, 0);
    vec4 s1 = texelFetch(uS1, me, 0);
    // Weak zones are irregular: warp the partition with low-frequency noise.
    vec3 w = p + uWarp * (vec3(noise3(p * 3.1 + 1.7), noise3(p * 3.1 + 9.2), noise3(p * 3.1 + 4.4)) - 0.5)
        + 0.35 * uWarp * (vec3(noise3(p * 9.0 + 2.1), noise3(p * 9.0 + 7.7), noise3(p * 9.0 + 5.3)) - 0.5);
    if (uMode == 0) {
        float best = -2.0; int id = 0;
        for (int k = 0; k < ${MAX_PLATES}; k++) {
            if (k >= uSeedCount) break;
            float c = dot(normalize(w), uSeeds[k]);
            if (c > best) { best = c; id = k; }
        }
        s0.r = float(id);
    } else if (int(floor(s0.r + 0.5)) == uSplitPlate && dot(normalize(w), uSplitNormal) > 0.0) {
        s0.r = float(uNewPlate);
    }
    out0 = s0;
    out1 = s1;
}`;

// Rigid plate motion: gather each texel from every plate's back-rotated
// position. Zero candidates = divergence (new crust); two or more =
// convergence (the less buoyant plate subducts or continents collide).
const composeFragment = `#version 300 es
precision highp float;
precision highp int;
uniform highp sampler2D uS0;
uniform highp sampler2D uS1;
uniform int uPlateCount;
uniform mat3 uBack[${MAX_PLATES}];
uniform float uRank[${MAX_PLATES}];
uniform float uTimeMyr;
layout(location = 0) out vec4 out0;
layout(location = 1) out vec4 out1;
${sphereCommon}
vec4 catmullRom(float t) {
    float t2 = t * t, t3 = t2 * t;
    return vec4(-0.5 * t3 + t2 - 0.5 * t, 1.5 * t3 - 2.5 * t2 + 1.0, -1.5 * t3 + 2.0 * t2 + 0.5 * t, 0.5 * t3 - 0.5 * t2);
}
void main() {
    ivec2 size = textureSize(uS0, 0);
    ivec2 me = ivec2(gl_FragCoord.xy);
    vec3 p = dirFromUv((vec2(me) + 0.5) / vec2(size));
    int count = 0;
    float bestScore = -1e30; float secondScore = -1e30;
    vec4 b0 = vec4(0.0), b1 = vec4(0.0), l0 = vec4(0.0);
    int bestId = -1;
    for (int k = 0; k < ${MAX_PLATES}; k++) {
        if (k >= uPlateCount) break;
        // Membership from bilinear coverage of the plate mask: boundaries
        // move with sub-texel precision instead of being re-quantised to the
        // grid at every bake (which grows sawtooth edges).
        vec2 q = uvFromDir(uBack[k] * p) * vec2(size) - 0.5;
        ivec2 base = ivec2(floor(q));
        vec2 f = q - vec2(base);
        vec4 sum0 = vec4(0.0), sum1 = vec4(0.0);
        vec4 lo = vec4(1e9), hi = vec4(-1e9);
        float weight = 0.0;
        for (int j = 0; j < 4; j++) {
            ivec2 o = ivec2(j & 1, j >> 1);
            ivec2 tt = wrapTexel(base + o, size);
            vec4 n0 = texelFetch(uS0, tt, 0);
            if (abs(n0.r - float(k)) > 0.5) continue;
            float w = (o.x == 1 ? f.x : 1.0 - f.x) * (o.y == 1 ? f.y : 1.0 - f.y) + 1e-5;
            sum0 += w * n0;
            sum1 += w * texelFetch(uS1, tt, 0);
            lo = min(lo, n0); hi = max(hi, n0);
            weight += w;
        }
        if (weight < 0.5) continue;
        vec4 a0 = vec4(float(k), sum0.gba / weight);
        vec4 a1 = sum1 / weight;
        // Interior: Catmull-Rom reconstruction (clamped, no overshoot) so
        // thousands of re-bakes do not blur the crust like bilinear would.
        vec4 wx = catmullRom(f.x), wy = catmullRom(f.y);
        vec3 cubic = vec3(0.0);
        bool interior = true;
        for (int jy = 0; jy < 4 && interior; jy++) for (int jx = 0; jx < 4; jx++) {
            vec4 n0 = texelFetch(uS0, wrapTexel(base + ivec2(jx - 1, jy - 1), size), 0);
            if (abs(n0.r - float(k)) > 0.5) { interior = false; break; }
            cubic += wx[jx] * wy[jy] * n0.gba;
        }
        if (interior) a0.gba = clamp(cubic, lo.gba, hi.gba);
        count++;
        // Continental crust never subducts. Oceanic subduction polarity is a
        // plate property (slab density at the margin), so a convergent
        // boundary keeps one consistent trench.
        float score = a0.b > 12.0 ? 1e5 + a0.b : uRank[k];
        if (score > bestScore) {
            secondScore = bestScore; l0 = b0;
            bestScore = score; b0 = a0; b1 = a1; bestId = k;
        } else if (score > secondScore) {
            secondScore = score; l0 = a0;
        }
    }
    if (count == 0) {
        out0 = vec4(-1.0, uTimeMyr, 0.0, 0.0);
        out1 = vec4(uTimeMyr, 0.0, 0.0, 0.0);
        return;
    }
    out0 = b0;
    out1 = vec4(b1.r, b1.g, 0.0, b1.a);
    if (count >= 2) {
        if (b0.b > 12.0 && l0.b > 12.0) {
            // Continental collision: crust volume is conserved by stacking.
            // Collisional thickening saturates (~80 km): beyond that the
            // orogen spreads laterally (high diffusivity above 50 km).
            out0.b = max(b0.b, min(b0.b + l0.b, 80.0));
            out0.a = max(b0.a, l0.a);
            out1.a = 1.0;
        } else {
            out1.b = float(bestId + 1); // trench; overriding plate id + 1
        }
    }
}`;

// Geological processes applied with the accumulated interval dt.
const processFragment = `#version 300 es
precision highp float;
precision highp int;
uniform highp sampler2D uC0;
uniform highp sampler2D uC1;
uniform float uTimeMyr;
uniform float uDtMyr;
uniform int uRegime;          // 1 = stagnant lid, 2 = plates
uniform float uRadiusKm;
uniform int uPlumeCount;
uniform vec4 uPlumes[${MAX_PLUMES}];      // xyz direction, w angular radius
uniform float uPlumeRates[${MAX_PLUMES}]; // edifice km/Myr at centre
uniform int uVentCount;
uniform vec4 uVents[${MAX_VENTS}];
uniform float uResurfaceKmPerMyr;
uniform int uImpactCount;
uniform vec4 uImpacts[${MAX_IMPACTS}];
uniform float uRecycleMyr;
uniform float uFelsicRate;
uniform float uArcRate;
uniform float uSeedPhase;
layout(location = 0) out vec4 out0;
layout(location = 1) out vec4 out1;
${sphereCommon}
void main() {
    ivec2 size = textureSize(uC0, 0);
    ivec2 me = ivec2(gl_FragCoord.xy);
    vec2 uv = (vec2(me) + 0.5) / vec2(size);
    vec3 p = dirFromUv(uv);
    float lat = (uv.y - 0.5) * PI;
    float coslat = max(cos(lat), 0.02);
    vec4 c0 = texelFetch(uC0, me, 0);
    vec4 c1 = texelFetch(uC1, me, 0);
    float id = c0.r;
    bool fresh = id < -0.5;
    if (fresh) {
        // Fill divergent gaps with the nearest plate's new oceanic crust.
        float bestD = 1e9;
        for (int dy = -4; dy <= 4; dy++) for (int dx = -4; dx <= 4; dx++) {
            vec4 n = texelFetch(uC0, wrapTexel(me + ivec2(dx, dy), size), 0);
            if (n.r < -0.5) continue;
            float d = float(dx * dx) * coslat * coslat + float(dy * dy);
            if (d < bestD) { bestD = d; id = n.r; }
        }
        c0 = vec4(id, uTimeMyr - 0.5 * uDtMyr, 0.0, 0.0);
        c1 = vec4(uTimeMyr - 2.5, 0.0, 0.0, 0.0);
    } else {
        // Plates are rigid: one-texel slivers left by resampling a rotated
        // boundary are aliasing, so they join the surrounding plate.
        float ids[8]; vec4 n0s[8];
        int k8 = 0;
        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) {
            if (dx == 0 && dy == 0) continue;
            n0s[k8] = texelFetch(uC0, wrapTexel(me + ivec2(dx, dy), size), 0);
            ids[k8] = n0s[k8].r; k8++;
        }
        int same = 0;
        for (int i = 0; i < 8; i++) if (abs(ids[i] - id) < 0.5) same++;
        if (same <= 2) {
            for (int i = 0; i < 8; i++) {
                if (ids[i] < -0.5) continue;
                int votes = 0;
                for (int j = 0; j < 8; j++) if (abs(ids[j] - ids[i]) < 0.5) votes++;
                if (votes >= 5) {
                    vec4 a0 = vec4(0.0), a1 = vec4(0.0);
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) {
                        ivec2 t = wrapTexel(me + ivec2(dx, dy), size);
                        vec4 m0 = texelFetch(uC0, t, 0);
                        if (abs(m0.r - ids[i]) > 0.5) continue;
                        a0 += m0; a1 += texelFetch(uC1, t, 0);
                    }
                    c0 = a0 / float(votes); c0.r = ids[i];
                    c1 = a1 / float(votes);
                    id = ids[i];
                    break;
                }
            }
        }
    }
    if (uRegime == 2 && !fresh && c0.b > 12.0) {
        // Continents are strong: plate boundaries localise in the weaker
        // oceanic lithosphere, so a continent belongs to a single plate
        // (and colliding continents suture). Majority vote over 5x5.
        float bestId = id; int bestVotes = 0; int ownVotes = 0;
        for (int j = 0; j < 25; j++) {
            vec4 a = texelFetch(uC0, wrapTexel(me + ivec2(j % 5 - 2, j / 5 - 2), size), 0);
            if (a.b <= 12.0 || a.r < -0.5) continue;
            if (abs(a.r - id) < 0.5) { ownVotes++; continue; }
            int votes = 0;
            for (int i = 0; i < 25; i++) {
                vec4 b = texelFetch(uC0, wrapTexel(me + ivec2(i % 5 - 2, i / 5 - 2), size), 0);
                if (b.b > 12.0 && abs(b.r - a.r) < 0.5) votes++;
            }
            if (votes > bestVotes) { bestVotes = votes; bestId = a.r; }
        }
        if (bestVotes > ownVotes + 2) { id = bestId; c0.r = bestId; }
    }
    float hc = c0.b;
    float edifice = c0.a;
    float lastEruption = c1.r;
    float mafic = c1.g;
    float strain = c1.a;

    // Lateral relaxation: lower-crustal flow and gravitational collapse
    // (dry erosion is negligible). Explicit diffusion, clamped for stability.
    float dyKm = PI * uRadiusKm / float(size.y);
    float dxKm = 2.0 * PI * uRadiusKm * coslat / float(size.x);
    // Cratons are stiff; only thickened orogens (>55 km) collapse quickly.
    float kCrust = 40.0 + 700.0 * smoothstep(50.0, 80.0, hc);
    float kEdifice = 30.0;
    vec4 e0 = texelFetch(uC0, wrapTexel(me + ivec2(1, 0), size), 0);
    vec4 w0 = texelFetch(uC0, wrapTexel(me + ivec2(-1, 0), size), 0);
    vec4 n0 = texelFetch(uC0, wrapTexel(me + ivec2(0, 1), size), 0);
    vec4 s0 = texelFetch(uC0, wrapTexel(me + ivec2(0, -1), size), 0);
    float ax = min(kCrust * uDtMyr / (dxKm * dxKm), 0.2);
    float ay = min(kCrust * uDtMyr / (dyKm * dyKm), 0.2);
    // Crust flows within its own plate only (no leak across boundaries).
    float he = abs(e0.r - id) < 0.5 ? e0.b : hc, hw = abs(w0.r - id) < 0.5 ? w0.b : hc;
    float hn = abs(n0.r - id) < 0.5 ? n0.b : hc, hs = abs(s0.r - id) < 0.5 ? s0.b : hc;
    if (!fresh) hc += ax * (he + hw - 2.0 * hc) + ay * (hn + hs - 2.0 * hc);
    float bx = min(kEdifice * uDtMyr / (dxKm * dxKm), 0.2);
    float by = min(kEdifice * uDtMyr / (dyKm * dyKm), 0.2);
    if (!fresh) edifice += bx * (e0.a + w0.a - 2.0 * edifice) + by * (n0.a + s0.a - 2.0 * edifice);

    // Heat-pipe resurfacing thickens the mafic lid; its base delaminates.
    mafic += uResurfaceKmPerMyr * uDtMyr;
    mafic -= mafic * min(uDtMyr / uRecycleMyr, 1.0);

    // Mantle plumes (fixed in the mantle frame): shield volcanoes and
    // volcanic provinces. On moving plates they leave hotspot tracks.
    for (int i = 0; i < ${MAX_PLUMES}; i++) {
        if (i >= uPlumeCount) break;
        float angle = acos(clamp(dot(p, uPlumes[i].xyz), -1.0, 1.0));
        // Volcanic provinces are lobate: rift-zone arms and flow fields,
        // not axisymmetric cones.
        vec3 seed = uPlumes[i].xyz * 17.0;
        float r = uPlumes[i].w * (0.45 + 1.1 * (noise3(p * 7.0 + seed) * 0.65 + noise3(p * 19.0 + seed) * 0.35));
        if (angle >= r) continue;
        float f = 1.0 - angle / r;
        float shield = f * f * (3.0 - 2.0 * f);
        edifice = min(edifice + uPlumeRates[i] * uDtMyr * shield, 6.0);
        mafic += uPlumeRates[i] * 0.6 * uDtMyr * f;
        if (hash12(vec2(me) + uSeedPhase * 911.0 + uTimeMyr) < f * f * 0.5) lastEruption = uTimeMyr;
    }
    // Distributed fissure eruptions (sampled visible subset of the heat pipe).
    for (int i = 0; i < ${MAX_VENTS}; i++) {
        if (i >= uVentCount) break;
        float angle = acos(clamp(dot(p, uVents[i].xyz), -1.0, 1.0));
        if (angle >= uVents[i].w) continue;
        float f = 1.0 - angle / uVents[i].w;
        float lobes = noise3(p * 60.0 + uVents[i].xyz * 17.0);
        if (lobes < f * 1.3) {
            lastEruption = uTimeMyr;
            edifice = min(edifice + 0.04 * f, 9.0);
        }
    }

    // Impact craters (Hadean bombardment). Complex-crater scaling (Pike
    // 1980, gravity-scaled to Earth): depth ~0.7 D^0.3 km, rim ~0.03 D^0.9.
    for (int i = 0; i < ${MAX_IMPACTS}; i++) {
        if (i >= uImpactCount) break;
        float r = uImpacts[i].w;
        float angle = acos(clamp(dot(p, uImpacts[i].xyz), -1.0, 1.0));
        // Slumped, eroded rims: the outline is irregular, not a perfect circle.
        float x = angle / (r * (0.82 + 0.36 * noise3(p * 9.0 + uImpacts[i].xyz * 5.0)));
        if (x >= 3.0) continue;
        float diameterKm = 2.0 * r * uRadiusKm;
        float depth = 0.7 * pow(diameterKm, 0.3);
        float rim = min(0.03 * pow(diameterKm, 0.9), 0.6 * depth);
        float wobble = 0.85 + 0.3 * noise3(p * 40.0 + uImpacts[i].xyz * 13.0);
        if (x < 1.0) {
            float bowl = -depth * (1.0 - x * x) + rim * pow(x, 6.0);
            edifice = mix(edifice, bowl * wobble, 0.9);
            // Impact melt sheet in large craters stays incandescent.
            if (diameterKm > 150.0 && x < 0.6) lastEruption = uTimeMyr;
        } else {
            edifice += rim * pow(x, -3.0) * wobble * smoothstep(3.0, 2.0, x);
        }
    }
    // Lava floods pond in topographic lows and bury old craters.
    if (edifice < 0.0) edifice = min(edifice + uResurfaceKmPerMyr * 1.5 * uDtMyr, 0.0);

    if (uRegime == 1) {
        // Thick mafic piles partially remelt to felsic (TTG-like) crust.
        // Lid thickness varies laterally with plume/drip history; TTG melts
        // form where the thick mafic root reaches garnet stability.
        float thick = mafic * (0.7 + 0.7 * (noise3(p * 3.0 + uSeedPhase * 37.0) * 0.7 + noise3(p * 7.0 + 5.0) * 0.3));
        hc += uFelsicRate * max(thick - 12.0, 0.0) * uDtMyr * (1.0 - smoothstep(30.0, 45.0, hc));
    } else if (!fresh) {
        // Arc magmatism 80-160 km behind the trench on the overriding plate,
        // with back-arc extension and magmatism widening the belt to ~300 km.
        float arc = 0.0;
        for (int k = 0; k < 8; k++) {
            float a = float(k) * PI * 0.25;
            for (int r = 2; r <= 8; r += 2) {
                vec2 off = vec2(cos(a) / coslat, sin(a)) * float(r);
                vec4 m = texelFetch(uC1, wrapTexel(me + ivec2(round(off)), size), 0);
                if (m.b > 0.5 && abs(m.b - 1.0 - id) < 0.5) arc = max(arc, r <= 4 ? 1.0 : (r == 6 ? 0.55 : 0.3));
            }
        }
        if (arc > 0.0) {
            // Arc crust growth stalls once the arc crust is thick (~45 km),
            // when the arc root founders back into the mantle.
            hc += uArcRate * arc * uDtMyr * (1.0 - smoothstep(30.0, 50.0, hc));
            edifice = min(edifice + 0.25 * arc * uDtMyr, 3.0);
            if (hash12(vec2(me) * 1.37 + uTimeMyr) < 0.35 * arc) lastEruption = uTimeMyr;
        }
    }
    strain = max(strain * exp(-uDtMyr / 60.0), c1.a);
    hc = clamp(hc, 0.0, 85.0);
    out0 = vec4(id, c0.g, hc, max(edifice, -8.0));
    out1 = vec4(lastEruption, max(mafic, 0.0), 0.0, strain);
}`;

// Isostatic elevation and display fields.
const derivedFragment = `#version 300 es
precision highp float;
precision highp int;
uniform highp sampler2D uC0;
uniform highp sampler2D uC1;
uniform float uTimeMyr;
uniform float uGlowWindowMyr;
layout(location = 0) out vec4 outDerived;
${sphereCommon}
float elevationAt(ivec2 t, ivec2 size) {
    vec4 c0 = texelFetch(uC0, t, 0);
    vec4 c1 = texelFetch(uC1, t, 0);
    float age = max(uTimeMyr - c0.g, 0.0);
    // Parsons & Sclater (1977) half-space subsidence, flattening ~90 Myr.
    float oceanic = -2.6 - 0.35 * sqrt(min(age, 90.0));
    // Airy isostasy: (rho_m - rho_c)/rho_m = 0.152 km per km of felsic crust.
    float continental = -3.9 + max(c0.b - 7.0, 0.0) * 0.152;
    float felsic = smoothstep(6.0, 20.0, c0.b);
    float mafic = 0.09 * min(c1.g, 30.0) * (1.0 - felsic);
    return mix(oceanic, continental, felsic) + mafic + c0.a;
}
void main() {
    ivec2 size = textureSize(uC0, 0);
    ivec2 me = ivec2(gl_FragCoord.xy);
    vec4 c0 = texelFetch(uC0, me, 0);
    vec4 c1 = texelFetch(uC1, me, 0);
    float elevation = elevationAt(me, size);
    float trench = 0.0;
    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) {
        trench += step(0.5, texelFetch(uC1, wrapTexel(me + ivec2(dx, dy), size), 0).b);
    }
    elevation -= 2.8 * trench / 9.0;
    float glow = exp(-max(uTimeMyr - c1.r, 0.0) / max(uGlowWindowMyr, 1e-9));
    float felsic = smoothstep(6.0, 20.0, c0.b);
    float freshness = exp(-max(uTimeMyr - c1.r, 0.0) / 40.0);
    outDerived = vec4(elevation, glow, felsic + 0.25 * clamp(c1.a, 0.0, 1.0), freshness);
}`;

const globeVertex = `#version 300 es
precision highp float;
in vec3 aPosition;
uniform mat4 uViewProjection;
uniform sampler2D uDerived;
uniform float uReliefScale;
uniform float uLithosphereWeight;
uniform float uDatumKm;
uniform float uSeaLevel;
uniform float uShell;
out vec3 vDirection;
out vec3 vWorld;
${sphereCommon}
void main() {
    vec3 radial = normalize(aPosition);
    float elevation = max(textureLod(uDerived, uvFromDir(radial), 0.0).r, uSeaLevel) - uDatumKm;
    float radius = uShell > 0.0 ? 1.0 + uShell : 1.0 + elevation * uReliefScale * uLithosphereWeight;
    vec3 world = radial * radius;
    vDirection = radial;
    vWorld = world;
    gl_Position = uViewProjection * vec4(world, 1.0);
}`;

const globeFragment = `#version 300 es
precision highp float;
precision highp int;
in vec3 vDirection;
in vec3 vWorld;
uniform vec3 uLight;
uniform vec3 uCamera;
uniform float uExposure;
uniform float uMagmaWeight;
uniform float uLithosphereWeight;
uniform float uRadiusMeters;
uniform float uMeltKelvin;
uniform float uRenewalVelocity;
uniform float uDisplaySpeed;
uniform float uSolar;
uniform int uRaftCount;
uniform int uBinGrid;
uniform highp sampler2D uRaftPosition;
uniform highp sampler2D uRaftVelocity;
uniform highp sampler2D uRaftFrame;
uniform highp sampler2D uRaftLink;
uniform float uMaterialScale;
uniform highp sampler2D uBins;
uniform highp sampler2D uAgeTable;
uniform sampler2D uDerived;
uniform float uReliefScale;
uniform float uDatumKm;
uniform float uVolcanicGlowKelvin;
uniform float uSeaLevel;
uniform float uShoreKm;    // sub-grid shoreline noise amplitude scale
uniform float uWater;      // 0..1 of the present ocean inventory condensed
uniform float uSteam;      // steam/rain-out cloud deck
uniform float uLife;       // land vegetation
uniform float uClimateK;   // global-mean temperature anomaly vs present (K)
uniform float uOxic;       // atmospheric oxygenation (Great Oxidation Event)
uniform float uCloudPhase;
uniform float uShell;
uniform float uClouds;
uniform float uSeamShare;
uniform float uRaftSpacing;
uniform int uMagmaImpactCount;
uniform vec4 uMagmaImpacts[${MAX_MAGMA_IMPACTS}];
uniform vec4 uMagmaImpactInfo[${MAX_MAGMA_IMPACTS}];
out vec4 outColor;
${thermalMaterialShader}
${sphereCommon}
const float AGE_LOG_MIN = ${AGE_LOG_MIN.toFixed(1)};
const float AGE_LOG_MAX = ${AGE_LOG_MAX.toFixed(1)};
const int AGE_SIZE = ${AGE_TABLE_SIZE};

vec4 raftData(sampler2D tex, int i) { return texelFetch(tex, ivec2(i % 1024, i / 1024), 0); }
// World -> material (crust-fixed) frame of raft i: inverse of its rotation.
vec3 toMaterial(int i, vec3 v) {
    vec4 q = raftData(uRaftFrame, i);
    vec3 t = 2.0 * cross(-q.xyz, v);
    return v + q.w * t + cross(-q.xyz, t);
}
vec3 toWorld(int i, vec3 v) {
    vec4 q = raftData(uRaftFrame, i);
    vec3 t = 2.0 * cross(q.xyz, v);
    return v + q.w * t + cross(q.xyz, t);
}

// Age table: rgb = cumulative radiance integral J(age) (radiance x s), a = Ts.
vec4 ageLookup(float age) {
    float x = (log(max(age, 1e-6)) / log(10.0) - AGE_LOG_MIN) / (AGE_LOG_MAX - AGE_LOG_MIN) * float(AGE_SIZE - 1);
    x = clamp(x, 0.0, float(AGE_SIZE - 1));
    int i = int(floor(x));
    int j = min(i + 1, AGE_SIZE - 1);
    return mix(texelFetch(uAgeTable, ivec2(i, 0), 0), texelFetch(uAgeTable, ivec2(j, 0), 0), x - float(i));
}
vec3 radianceAtAge(float age) { return thermalRadiance(ageLookup(age).a).rgb; }
vec3 cumulative(float age) {
    float minimum = pow(10.0, AGE_LOG_MIN);
    if (age <= minimum) return thermalRadiance(uMeltKelvin).rgb * age;
    return ageLookup(age).rgb;
}
// Integral of radiance over [0, X] metres from a seam opening at speed s
// into a raft whose crust is at most A seconds old.
vec3 seamIntegral(float X, float A, float s) {
    if (X <= 0.0) return vec3(0.0);
    vec3 rA = radianceAtAge(A);
    if (s <= 1e-6) return rA * X;
    float xA = A * s * 0.5;
    if (X <= xA) return 0.5 * s * cumulative(2.0 * X / s);
    return 0.5 * s * cumulative(A) + rA * (X - xA);
}

const int NC = 5;
void nearestRafts(vec3 p, out int ids[NC]) {
    float ds[NC];
    for (int i = 0; i < NC; i++) { ids[i] = -1; ds[i] = 1e9; }
    int g = uBinGrid;
    ivec3 cell = clamp(ivec3(floor((p + 1.0) * 0.5 * float(g))), ivec3(0), ivec3(g - 1));
    for (int dz = -1; dz <= 1; dz++) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) {
        ivec3 c = cell + ivec3(dx, dy, dz);
        if (any(lessThan(c, ivec3(0))) || any(greaterThanEqual(c, ivec3(g)))) continue;
        int binBase = (c.x + g * (c.y + g * c.z)) * ${binSlots};
        for (int s = 0; s < ${binSlots}; s++) {
            int k = binBase + s;
            float index = texelFetch(uBins, ivec2(k % 1024, k / 1024), 0).r;
            if (index < 0.0) break;
            int i = int(index);
            vec3 q = raftData(uRaftPosition, i).xyz;
            float d = dot(p - q, p - q);
            if (d >= ds[NC - 1]) continue;
            int j = NC - 1;
            while (j > 0 && ds[j - 1] > d) { ds[j] = ds[j - 1]; ids[j] = ids[j - 1]; j--; }
            ds[j] = d; ids[j] = i;
        }
    }
}

// Signed angular distance from the fracture between rafts a and b (positive
// on a's side). The straight Voronoi bisector is roughened by fractal noise
// anchored to the pair, so fractures are irregular and travel with the
// rafts; the perturbation flips sign with the pair order so both sides agree.
float seamCoord(vec3 p, vec3 a, vec3 b) {
    float d = asin(clamp(dot(p, normalize(a - b)), -1.0, 1.0));
    vec3 q = (p - normalize(a + b)) / uRaftSpacing;
    float w = (noise3(q * 1.7 + 3.1) - 0.5) * 0.5 + (noise3(q * 4.3 + 7.7) - 0.5) * 0.25
        + (noise3(q * 11.0 + 1.3) - 0.5) * 0.12 + (noise3(q * 27.0 + 5.9) - 0.5) * 0.05;
    float s = (a.x + 0.37 * a.y > b.x + 0.37 * b.y) ? 1.0 : -1.0;
    return d + s * w * 0.42 * uRaftSpacing;
}

// Thin contraction cracks inside a raft (sub-grid, ridged noise lines).
float crackLines(vec3 q, float footprintUnits) {
    float r = abs(noise3(q * 5.0) - 0.5) + 0.6 * abs(noise3(q * 11.0 + 4.0) - 0.5);
    float width = 0.035;
    float sharp = 1.0 - smoothstep(0.0, width + footprintUnits, r);
    // Narrower than a pixel: area-average instead of aliasing.
    return sharp * clamp(width / max(footprintUnits, 1e-4), 0.0, 1.0);
}

struct Crust { vec3 emission; vec3 normal; float albedo; float gloss; };

// Mottling (x) and broad albedo provinces (y) of the crust at material point q.
vec2 crustTone(vec3 q) {
    return vec2(noise3(q * 1.3 + 2.0) * 0.6 + noise3(q * 4.1) * 0.4, noise3(q * 0.25 + 6.0));
}

Crust magmaSurface(vec3 p, float footprint) {
    Crust c;
    c.normal = p; c.albedo = 0.05; c.gloss = 0.0;
    int ids[NC];
    nearestRafts(p, ids);
    if (ids[0] < 0) { c.emission = thermalRadiance(uMeltKelvin).rgb; return c; }
    // Owner under the roughened fractures (usually the nearest centre).
    int o = ids[0];
    vec3 po = raftData(uRaftPosition, o).xyz;
    for (int pass = 0; pass < 2; pass++) {
        for (int k = 0; k < NC; k++) {
            if (ids[k] < 0 || ids[k] == o) continue;
            vec3 pk = raftData(uRaftPosition, ids[k]).xyz;
            if (seamCoord(p, po, pk) < 0.0) { o = ids[k]; po = pk; }
        }
    }
    // Plates (welded raft groups) are bounded by a smooth implicit curve: a
    // Gaussian membership field over the nearest rafts, warped by fractal
    // noise. Following raft-cell edges would give stair-stepped rifts.
    // The warp is evaluated in each plate's material frame, so plate
    // outlines are carried by the plates instead of sliding through a
    // world-fixed noise field.
    float sig2 = 0.3 * uRaftSpacing * uRaftSpacing;
    // Per-plate membership weight and gradient; the rift is the zero set of
    // the difference between the two dominant plates (owner-independent).
    float plateId[NC]; float plateW[NC]; vec3 plateG[NC]; int plateRaft[NC]; float plateD[NC]; vec3 plateWarp[NC];
    int plates = 0;
    for (int k = 0; k < NC; k++) {
        int j = ids[k];
        if (j < 0) continue;
        vec3 x = raftData(uRaftPosition, j).xyz;
        float id = raftData(uRaftVelocity, j).w;
        int m = plates;
        for (int i = 0; i < NC; i++) if (i < plates && abs(plateId[i] - id) < 0.5) m = i;
        if (m == plates) {
            vec3 qn = toMaterial(j, p) / uMaterialScale;
            vec3 wm = (vec3(noise3(qn * 0.8 + 1.3), noise3(qn * 0.8 + 7.1), noise3(qn * 0.8 + 3.7)) - 0.5) * 0.9
                + (vec3(noise3(qn * 2.6 + 5.3), noise3(qn * 2.6 + 2.9), noise3(qn * 2.6 + 8.2)) - 0.5) * 0.3;
            plateId[m] = id; plateW[m] = 0.0; plateG[m] = vec3(0.0); plateRaft[m] = j; plateD[m] = 1e9;
            plateWarp[m] = uRaftSpacing * toWorld(j, wm);
            plates++;
        }
        vec3 r = p + plateWarp[m] - x;
        float wgt = exp(-dot(r, r) / sig2);
        plateW[m] += wgt;
        plateG[m] -= wgt * 2.0 / sig2 * r;
        if (dot(r, r) < plateD[m]) { plateD[m] = dot(r, r); plateRaft[m] = j; }
    }
    int first = 0, second = -1;
    for (int i = 1; i < NC; i++) if (i < plates && plateW[i] > plateW[first]) first = i;
    for (int i = 0; i < NC; i++) if (i < plates && i != first && (second < 0 || plateW[i] > plateW[second])) second = i;
    int opp = -1;
    float rift = 1e3;
    vec3 inward = vec3(0.0);
    // A plate entering or leaving the nearest-raft set must not switch the
    // boundary on or off: fade boundary effects by its relative weight.
    float presence = 0.0;
    if (plateId[first] != raftData(uRaftVelocity, o).w) o = plateRaft[first];
    if (second >= 0) {
        opp = plateRaft[second];
        vec3 grad = plateG[first] - plateG[second];
        grad -= p * dot(grad, p);
        // Floor the gradient at its typical value near a boundary so weak
        // gradients (junctions, sparse kernels) cannot smear the rift.
        float gradLength = max(length(grad), 2.0 * (plateW[first] + plateW[second]) / uRaftSpacing);
        rift = (plateW[first] - plateW[second]) / gradLength;
        presence = smoothstep(0.01, 0.06, plateW[second] / max(plateW[first], 1e-9));
        // Seams freezing over (welding) or newly opening (rifting) fade.
        vec4 la = raftData(uRaftLink, plateRaft[first]), lb = raftData(uRaftLink, plateRaft[second]);
        if (abs(la.x - plateId[second]) < 0.5) presence *= la.y;
        if (abs(lb.x - plateId[first]) < 0.5) presence *= lb.y;
        inward = grad / gradLength;
    }
    vec4 pa = raftData(uRaftPosition, o);
    vec4 va = raftData(uRaftVelocity, o);
    float w = max(footprint, 1.0);
    vec3 rA = radianceAtAge(pa.w);
    // Welded neighbours share their crust history: blend interior radiance
    // across sutures so plates have no steps.
    vec3 interior = rA;
    float interiorWeight = 1.0;
    float groove = 0.0;
    // Low-frequency tone of the crust, blended across welded sutures between
    // fragments that carry different rotations (no polygon mosaic).
    vec3 qOwner = toMaterial(o, p) / uMaterialScale;
    vec2 tone = crustTone(qOwner);
    float toneWeight = 1.0;
    for (int k = 0; k < NC; k++) {
        int j = ids[k];
        if (j < 0 || j == o) continue;
        vec4 pb = raftData(uRaftPosition, j);
        if (abs(va.w - raftData(uRaftVelocity, j).w) >= 0.5) continue;
        float dAng = max(seamCoord(p, pa.xyz, pb.xyz), 0.0);
        groove = max(groove, 1.0 - smoothstep(0.0, 1.2 * w, uRadiusMeters * dAng));
        float wj = exp(-dAng / (0.3 * uRaftSpacing));
        interior += radianceAtAge(pb.w) * wj;
        interiorWeight += wj;
        float wt = exp(-dAng / (0.12 * uRaftSpacing));
        if (wt > 0.03) { tone += crustTone(toMaterial(j, p) / uMaterialScale) * wt; toneWeight += wt; }
    }
    vec3 emission = vec3(0.0);
    vec3 tilt = vec3(0.0);
    float reach = (1.0 - smoothstep(0.12, 0.35, rift / uRaftSpacing)) * presence;
    if (opp >= 0 && reach > 0.0) {
        vec4 pb = raftData(uRaftPosition, opp);
        vec4 vb = raftData(uRaftVelocity, opp);
        float d = uRadiusMeters * rift;
        // Rifts open at the crust renewal velocity (all crust is created at
        // rifts within one lifetime), modulated by the local divergence of
        // the two rigid plates (Euler vectors) across the boundary normal.
        float divergence = dot(cross(vb.xyz - va.xyz, p), -inward) / max(uDisplaySpeed, 1e-9);
        float separation = uRenewalVelocity / max(uSeamShare, 0.25) * clamp(0.35 + 9.0 * divergence, 0.08, 3.0);
        vec3 own = seamIntegral(d + 0.5 * w, pa.w, separation) - seamIntegral(max(d - 0.5 * w, 0.0), pa.w, separation);
        vec3 other = d < 0.5 * w ? seamIntegral(0.5 * w - d, pb.w, separation) : vec3(0.0);
        emission = max((own + other) / w - rA, vec3(0.0)) * reach;
        // Glow scattered by the vapour over the rift (soft halo).
        vec3 core = max(seamIntegral(w, pa.w, separation) / w - rA, vec3(0.0));
        emission += core * 0.08 * exp(-rift / (0.1 * uRaftSpacing)) * reach;
        // Slabs are bevelled where they break: their edges slope into the rift.
        tilt -= inward * 0.4 * exp(-rift / (0.1 * uRaftSpacing)) * reach;
    }
    emission += interior / interiorWeight;
    // Plate-scale tilt (rafts ride a sloshing, convecting surface) and ropy
    // wrinkles in the raft's own frame.
    // Relief, mottling and cracks are fixed to the crust (material frame),
    // so they ride with the slabs; welding never re-seeds them.
    vec3 q = qOwner;
    tone /= toneWeight;
    float footprintUnits = footprint / (uRaftSpacing * uRadiusMeters);
    float detailFade = smoothstep(0.08, 0.02, footprintUnits);
    float eps = 0.02;
    float h0 = noise3(q * 9.0) + 0.5 * noise3(q * 23.0);
    vec3 g = vec3(noise3((q + vec3(eps, 0, 0)) * 9.0) + 0.5 * noise3((q + vec3(eps, 0, 0)) * 23.0) - h0,
                  noise3((q + vec3(0, eps, 0)) * 9.0) + 0.5 * noise3((q + vec3(0, eps, 0)) * 23.0) - h0,
                  noise3((q + vec3(0, 0, eps)) * 9.0) + 0.5 * noise3((q + vec3(0, 0, eps)) * 23.0) - h0) / eps;
    g = toWorld(o, g);
    g -= p * dot(g, p);
    tilt -= g * 0.012 * (0.4 + 0.6 * detailFade);
    float tl = length(tilt);
    if (tl > 0.5) tilt *= 0.5 / tl;
    c.normal = normalize(p + tilt - p * dot(tilt, p));
    // Contraction cracks glow only where they are hotter than the skin.
    float crack = crackLines(q, footprintUnits);
    emission += max(radianceAtAge(pa.w * 0.15) - rA, vec3(0.0)) * crack * 0.35;
    // Quenched basaltic glass: blackest at fresh rift edges; older slabs are
    // devitrified and vesicular, mottled dark grey. Welded sutures are faint.
    float mott = tone.x;
    float aged = 1.0 - presence * exp(-rift / (0.2 * uRaftSpacing));
    c.albedo = mix(0.035, 0.05 + 0.02 * tone.y + 0.05 * mott, aged) * (1.0 - 0.12 * groove) * mix(0.9, 1.1, h0 / 1.5);
    c.gloss = 0.2 * (1.0 - 0.6 * aged);
    c.emission = emission;
    return c;
}


float elevationAt(vec2 uv) { return texture(uDerived, uv).r; }
// Octaves fade out below ~3 pixels (wavelength vs footprint) to avoid aliasing.
float detail(vec3 q, float footprint) {
    float w0 = smoothstep(2.0, 4.0, 39.8e3 / footprint);
    float w1 = smoothstep(2.0, 4.0, 18.7e3 / footprint);
    float w2 = smoothstep(2.0, 4.0, 8.8e3 / footprint);
    return w0 * noise3(q * 160.0) + 0.5 * w1 * noise3(q * 340.0 + 3.1) + 0.25 * w2 * noise3(q * 720.0 + 7.3);
}

vec3 rotateY(vec3 v, float a) { float c = cos(a), s = sin(a); return vec3(c * v.x + s * v.z, v.y, -s * v.x + c * v.z); }
float gauss(float x, float w) { return exp(-(x * x) / (w * w)); }
vec3 rotateAxis(vec3 v, vec3 k, float a) {
    float c = cos(a), s = sin(a);
    return v * c + cross(k, v) * s + k * dot(k, v) * (1.0 - c);
}
float fbm5(vec3 q) {
    float s = 0.0, a = 0.5;
    for (int i = 0; i < 5; i++) { s += a * noise3(q); q = q * 2.07 + vec3(1.7, 9.2, 3.1); a *= 0.5; }
    return s / 0.969;
}
// Convection cells of the exposed melt: returns (F1, F2-F1) of a jittered
// 3-D cell lattice; lanes (small F2-F1) are cool downwellings.
vec2 convectionCells(vec3 x) {
    vec3 i = floor(x), f = fract(x);
    float f1 = 9.0, f2 = 9.0;
    for (int z = -1; z <= 1; z++) for (int y = -1; y <= 1; y++) for (int xx = -1; xx <= 1; xx++) {
        vec3 o = vec3(xx, y, z);
        vec3 c = i + o;
        vec3 j = vec3(hash13(c), hash13(c + 17.3), hash13(c + 41.9));
        float d = length(o + j - f);
        if (d < f1) { f2 = f1; f1 = d; } else if (d < f2) { f2 = d; }
    }
    return vec2(f1, f2 - f1);
}
// Cloud optical depth proxy (0..1): zonal belts of the general circulation
// (ITCZ, dry subtropical highs, mid-latitude storm tracks), mid-latitude
// cyclones that wind cloud into comma/spiral bands (counter-clockwise in
// the north, clockwise in the south), and convective/stratocumulus texture.
float gCloudDetail = 0.0;
float cloudAt(vec3 p) {
    float lat = asin(clamp(p.y, -1.0, 1.0)) * 57.2958;
    float a = abs(lat);
    // Zonal transport: trades westward, westerlies eastward (bounded shear).
    float wind = -0.6 * gauss(a, 25.0) + 0.9 * gauss(a - 47.0, 15.0);
    vec3 q = rotateY(p, 0.25 * wind + uCloudPhase * 0.04);
    vec3 q0 = q;
    // Extratropical cyclones: born, wound up and decaying along the tracks.
    for (int i = 0; i < 10; i++) {
        float fi = float(i);
        float life = uCloudPhase * 0.035 + fi * 0.137;
        float cycle = floor(life);
        float age = fract(life);
        float hemi = i < 5 ? 1.0 : -1.0;
        float clat = radians(hemi * (38.0 + 22.0 * fract(sin((fi + cycle * 7.3) * 12.9898) * 43758.5)));
        float clon = fract(sin((fi * 3.1 + cycle * 1.7) * 78.233) * 12345.6) * 6.2832 + age * 0.9;
        vec3 centre = vec3(cos(clat) * cos(clon), sin(clat), cos(clat) * sin(clon));
        float d = acos(clamp(dot(q, centre), -1.0, 1.0));
        float strength = sin(age * 3.14159) * 1.0;
        q = rotateAxis(q, centre, hemi * strength * exp(-d * d / 0.05));
    }
    vec3 drift = vec3(uCloudPhase * 0.05, 0.0, -uCloudPhase * 0.03);
    vec3 warp = vec3(noise3(q * 2.5 + 11.0 + drift), noise3(q * 2.5 + 27.0 - drift), noise3(q * 2.5 + 41.0 + drift)) - 0.5;
    // Anisotropic sampling: weather systems are stretched along the flow.
    vec3 s = q + warp * 0.35;
    s.y *= 1.3;
    float n = fbm5(s * 4.0 + drift);
    // ITCZ: clusters of deep convection rather than a continuous band.
    float clusters = smoothstep(0.45, 0.7, noise3(s * 8.0 + drift * 2.0));
    // Earth's mean cloud fraction is ~0.67 (ISCCP/MODIS).
    float cover = 0.5 +  0.2 * gauss(lat - 5.0, 7.0) * (0.3 + clusters) - 0.24 * gauss(a - 23.0, 9.0)
        + 0.22 * gauss(a - 55.0, 12.0) + 0.1 * gauss(a - 80.0, 10.0);
    // Cloud edges are eroded by small-scale turbulence (cumulus fringes).
    // Convective cells are re-formed continuously rather than advected
    // through the whole cyclone wind-up, so they are only partly swirled
    // (full winding shears them into hairline streaks).
    vec3 sd = normalize(mix(q0, q, 0.3)) + warp * 0.35;
    float cells = noise3(sd * 38.0 + drift * 3.0) * 0.6 + noise3(sd * 85.0) * 0.4;
    float convective = clamp(gauss(lat - 5.0, 12.0) + 0.7 * gauss(a - 23.0, 10.0), 0.0, 1.0);
    float m = n + (cells - 0.5) * mix(0.1, 0.2, convective);
    // Close up, resolve cumulus-scale turbulence and crisper edges.
    m += gCloudDetail * ((noise3(sd * 190.0 + drift * 4.0) - 0.5) * 0.12 + (noise3(sd * 430.0) - 0.5) * 0.07);
    float soft = mix(1.0, 0.45, gCloudDetail);
    // Thin fringes are translucent; only the cores are optically thick.
    float edge = smoothstep(1.0 - cover - 0.05 * soft, 1.0 - cover + 0.12 * soft, m);
    return edge * (0.5 + 0.5 * smoothstep(1.0 - cover + 0.04, 1.0 - cover + 0.3, m));
}

// Steam deck: a Venus-like global cloud deck, sheared into zonal bands by
// fast zonal winds, that breaks up in patches as the steam rains out.
float steamDeck(vec3 p) {
    float lat = asin(clamp(p.y, -1.0, 1.0));
    vec3 q = rotateY(p, uCloudPhase * 0.05 * (1.0 + 0.6 * cos(2.0 * lat)) + 0.4 * sin(5.0 * lat));
    vec3 w = vec3(noise3(q * 3.0 + 5.0), noise3(q * 3.0 + 9.0), noise3(q * 3.0 + 13.0)) - 0.5;
    vec3 s = q + w * 0.25;
    float bands = 0.5 + 0.5 * sin(lat * 11.0 + 2.5 * fbm5(s * 2.0));
    float n = mix(fbm5(s * vec3(2.5, 7.0, 2.5)), bands, 0.3) + 0.08 * (fbm5(s * 9.0 + 3.0) - 0.5);
    return smoothstep(n - 0.1, n + 0.1, uSteam * 1.15);
}

// Deck tone: zonal bands and convective cells modulate the cloud-top
// brightness (water clouds are white; the warm, H2O/CO2-rich upper deck is
// slightly cream-tinted).
float steamTone(vec3 p) {
    float lat = asin(clamp(p.y, -1.0, 1.0));
    vec3 q = rotateY(p, uCloudPhase * 0.05 * (1.0 + 0.6 * cos(2.0 * lat)));
    float bands = sin(lat * 9.0 + 3.0 * fbm5(q * vec3(1.5, 4.0, 1.5)));
    float cells = noise3(q * 38.0) * 0.6 + noise3(q * 95.0) * 0.4;
    return 0.88 + 0.1 * bands + 0.18 * (cells - 0.5);
}

vec4 atmosphereShell() {
    vec3 o = uCamera;
    vec3 d = normalize(vWorld - uCamera);
    float ra = 1.0 + uShell;
    float b = dot(o, d);
    float disc = b * b - (dot(o, o) - ra * ra);
    if (disc <= 0.0) return vec4(0.0);
    float t0 = -b - sqrt(disc), t1 = -b + sqrt(disc);
    float pd = b * b - (dot(o, o) - 1.0);
    if (pd > 0.0) t1 = min(t1, -b - sqrt(pd));
    float path = max(t1 - max(t0, 0.0), 0.0) / (2.0 * sqrt(ra * ra - 1.0));
    vec3 m = normalize(o + d * 0.5 * (max(t0, 0.0) + t1));
    float sun = dot(m, normalize(uLight));
    float day = smoothstep(-0.25, 0.35, sun);
    vec3 rayleigh = vec3(0.18, 0.38, 1.0);
    vec3 dusk = vec3(1.0, 0.45, 0.2) * gauss(sun - 0.02, 0.18);
    vec3 col = (rayleigh * day + dusk * 0.6) * pow(clamp(path, 0.0, 1.0), 1.4) * 0.55 * (0.4 + 0.6 * max(uWater, uLithosphereWeight * 0.5));
    // Magma-ocean era: a thick silicate-vapour/steam atmosphere lit from
    // below by the incandescent surface glows on the night side too.
    col += thermalRadiance(uMeltKelvin).rgb * 0.12 * uMagmaWeight * pow(clamp(path, 0.0, 1.0), 1.2);
    return vec4(col, 0.0);
}

void main() {
    if (uShell > 0.0) { outColor = atmosphereShell(); return; }
    vec3 p = normalize(vDirection);
    vec3 view = normalize(uCamera - vWorld);
    vec3 light = normalize(uLight);
    float footprint = length(fwidth(vWorld)) * uRadiusMeters;
    gCloudDetail = smoothstep(8000.0, 2500.0, footprint);
    vec3 emitted = vec3(0.0);
    vec3 reflected = vec3(0.0);

    if (uMagmaWeight > 0.001) {
        Crust crust = magmaSurface(p, footprint);
        // Melt exposed at the surface organises into convection cells:
        // hot upwelling cores, ~100 K cooler downwelling lanes where a
        // transient glassy skin forms and sinks. Radiance ~ T^4 plus Wien shift.
        vec3 drift = vec3(uCloudPhase * 0.02, -uCloudPhase * 0.013, uCloudPhase * 0.017);
        vec3 warp = vec3(fbm5(p * 3.0 + drift), fbm5(p * 3.0 + drift + 7.1), fbm5(p * 3.0 + drift + 13.7)) - 0.5;
        vec2 big = convectionCells(p * 4.5 + warp * 1.1 + drift * 0.5);
        vec2 small = convectionCells(p * 17.0 + warp * 2.5 - drift);
        // Lanes are narrow, broken and filamentary (turbulent, not a net).
        float lane = (1.0 - smoothstep(0.0, 0.13, big.y)) * smoothstep(0.3, 0.6, fbm5(p * 7.0 + drift * 1.5));
        float laneSmall = 1.0 - smoothstep(0.0, 0.08, small.y);
        float core = 1.0 - smoothstep(0.0, 0.55, big.x);
        // Hot upwelling plumes and broad cooler regions, cut by sinking lanes.
        float convect = 0.8 * core - 0.75 * lane - 0.25 * laneSmall + 1.3 * (fbm5(p * 4.0 - drift) - 0.5)
            + 0.6 * smoothstep(0.62, 0.8, fbm5(p * 9.0 + drift * 2.0));
        // Convection shows only where melt is exposed; it fades as crust forms.
        vec3 meltRadiance = thermalRadiance(uMeltKelvin).rgb;
        float exposed = clamp(dot(crust.emission, vec3(0.2, 0.7, 0.1)) / max(dot(meltRadiance, vec3(0.2, 0.7, 0.1)), 1e-6), 0.0, 1.0);
        float k = exp(1.0 * convect * exposed);
        vec3 magma = crust.emission * pow(vec3(k), vec3(0.6, 1.0, 1.8)) * 0.9;
        // Limb darkening: the hot silicate-vapour atmosphere absorbs along
        // slant paths (redder and dimmer towards the limb).
        float muView = max(dot(p, view), 0.05);
        magma *= exp(-vec3(0.12, 0.2, 0.35) * (1.0 / muView - 1.0));
        // Skins in the lanes: a dark rim crusts over the coolest melt.
        float skin = smoothstep(0.55, 0.95, lane * (0.6 + 0.8 * fbm5(p * 30.0 + drift)));
        magma *= 1.0 - 0.6 * skin * exposed * uMagmaWeight;
        vec3 crustNormal = crust.normal;
        float crustAlbedo = crust.albedo;
        // Asteroid impacts into the magma ocean (Hadean bombardment).
        for (int i = 0; i < ${MAX_MAGMA_IMPACTS}; i++) {
            if (i >= uMagmaImpactCount) break;
            vec3 centre = uMagmaImpacts[i].xyz;
            float radius = uMagmaImpacts[i].w;
            float age = uMagmaImpactInfo[i].x, seed = uMagmaImpactInfo[i].y;
            float ang = acos(clamp(dot(p, centre), -1.0, 1.0));
            if (ang > radius * 3.0) continue;
            vec3 ax = normalize(cross(centre, abs(centre.y) < 0.9 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0)));
            vec3 ay = cross(centre, ax);
            float az = atan(dot(p, ay), dot(p, ax));
            float x = ang / (radius * (0.85 + 0.3 * noise3(vec3(cos(az), sin(az), seed) * 2.5)));
            vec3 hot = thermalRadiance(uMeltKelvin + 350.0).rgb;
            // Fireball: vapour plume of the impactor and target.
            float flash = exp(-age / 0.25) * exp(-x * x * 1.5);
            // Transient cavity: the crust is destroyed and superheated melt
            // is exposed; it re-crusts from the rim inwards.
            float recrust = smoothstep(2.0, 14.0, age + 8.0 * x * x);
            float pool = (1.0 - smoothstep(0.85, 1.05, x)) * (1.0 - recrust);
            // Expanding ejecta curtain and melt spray rays.
            float rr = 1.0 + 1.2 * (1.0 - exp(-age / 1.5));
            float ring = exp(-pow((x - rr) / 0.08, 2.0)) * exp(-age / 2.5) * smoothstep(0.0, 0.2, age);
            // Fine melt spatter beyond the rim (no coherent rays on a liquid target).
            float spatter = smoothstep(0.72, 0.9, noise3(p / radius * 9.0 + seed)) * smoothstep(1.0, 1.15, x) * exp(-(x - 1.0) * 2.5) * exp(-age / 4.0);
            float cracks = smoothstep(0.9, 1.0, 1.0 - abs(noise3(p / radius * 3.0 + seed) - 0.5) * 2.0) * (1.0 - smoothstep(0.9, 1.1, x)) * recrust * exp(-age / 20.0);
            magma = mix(magma, hot * 0.7, pool) + hot * (0.45 * ring + 0.35 * spatter + 0.25 * cracks) + thermalRadiance(4500.0).rgb * 0.9 * flash;
            // Raised, slumping rim and a dark fresh-glass floor once crusted.
            float rim = exp(-pow((x - 1.0) / 0.2, 2.0)) * recrust * exp(-age / 25.0);
            vec3 outward = normalize(p - centre * dot(p, centre) + 1e-6);
            crustNormal = normalize(crustNormal + outward * rim * 0.5 * sign(x - 1.0 + 1e-3));
            crustAlbedo = mix(crustAlbedo, 0.03, (1.0 - smoothstep(0.8, 1.0, x)) * recrust * exp(-age / 25.0));
        }
        float nl = max(dot(crustNormal, light), 0.0);
        vec3 halfway = normalize(light + view);
        // Rough, devitrified glass: a broad, weak sheen rather than a hotspot.
        float glossy = crust.gloss * pow(max(dot(crustNormal, halfway), 0.0), 14.0) * 0.25;
        // The degassed steam deck veils the cooling melt: only a dull,
        // scattered red glow leaks through its thick patches.
        float veil = uSteam > 0.001 && uClouds > 0.5 ? steamDeck(p) : 0.0;
        float sunUp = smoothstep(-0.05, 0.25, dot(p, light)) * max(dot(p, light), 0.04);
        emitted += magma * uMagmaWeight * (1.0 - 0.85 * veil);
        reflected += mix((vec3(crustAlbedo) * nl + vec3(glossy) * nl), vec3(0.74, 0.71, 0.66) * sunUp, veil) * uSolar * uMagmaWeight;
    }
    if (uLithosphereWeight > 0.001) {
        vec2 uv = uvFromDir(p);
        vec4 derived = texture(uDerived, uv);
        ivec2 size = textureSize(uDerived, 0);
        float du = 1.0 / float(size.x), dv = 1.0 / float(size.y);
        float lat = (uv.y - 0.5) * PI, lon = (uv.x - 0.5) * 2.0 * PI;
        float coslat = max(cos(lat), 0.05);
        float radiusKm = uRadiusMeters / 1000.0;
        float dEast = (elevationAt(uv + vec2(du, 0.0)) - elevationAt(uv - vec2(du, 0.0))) / (2.0 * du * 2.0 * PI * radiusKm * coslat);
        float dNorth = (elevationAt(uv + vec2(0.0, dv)) - elevationAt(uv - vec2(0.0, dv))) / (2.0 * dv * PI * radiusKm);
        vec3 east = vec3(-sin(lon), 0.0, cos(lon));
        vec3 north = vec3(-sin(lat) * cos(lon), cos(lat), -sin(lat) * sin(lon));
        float exaggeration = ${RELIEF_EXAGGERATION.toFixed(1)};
        float elevation = derived.r;
        float felsic = clamp(derived.b, 0.0, 1.0);
        float fresh = derived.a;
        // Sub-grid relief (< 40 km grid): procedural, amplitude set by the
        // resolved terrain (steep orogens rough, lava plains smooth).
        float slope = length(vec2(dEast, dNorth)) * exaggeration;
        float rough = 0.15 + 0.5 * felsic + min(2.0 * slope, 0.5) + 0.4 * smoothstep(1.0, 5.0, elevation);
        float detailFade = 1.0;
        float h0 = detail(p, footprint);
        float eps = 0.0012;
        vec3 detailGrad = vec3(detail(p + east * eps, footprint) - h0, detail(p + north * eps, footprint) - h0, 0.0) / eps;
        vec3 n = normalize(p - exaggeration * (dEast * east + dNorth * north)
            - rough * detailFade * 0.0022 * (detailGrad.x * east + detailGrad.y * north));
        // Airless silicate albedos: fresh basalt ~0.05, aged basalt ~0.09,
        // felsic crust ~0.20 (lighter, warmer grey).
        vec3 basalt = mix(vec3(0.085, 0.082, 0.080), vec3(0.045, 0.043, 0.042), fresh);
        vec3 granite = vec3(0.24, 0.205, 0.17);
        vec3 albedo = mix(basalt, granite, felsic);
        // Flow-field and regolith mottling.
        float flows = noise3(p * 70.0) * 0.6 + noise3(p * 190.0) * 0.4;
        albedo *= mix(0.8, 1.2, flows);
        albedo *= mix(0.9, 1.15, smoothstep(1.0, 6.0, elevation));
        vec3 halfway = normalize(light + view);
        float spec = pow(max(dot(n, halfway), 0.0), 40.0) * 0.04 * fresh;
        // Sub-grid coastline: bays, headlands and islands below the grid.
        float shoreDetail = (noise3(p * 55.0) - 0.5) * 0.9 + (noise3(p * 140.0) - 0.5) * 0.45 + (noise3(p * 330.0) - 0.5) * 0.2;
        // Lid era (flat crust, no Earth reconstruction): the first water pools
        // in broad, long-wavelength lows rather than speckled puddles.
        float broad = smoothstep(0.3, 1.0, uShoreKm);
        shoreDetail = mix(shoreDetail, (fbm5(p * 3.5 + 7.0) - 0.5) * 2.4 + 0.3 * shoreDetail, broad);
        // Coastlines on flat platforms would trace the bilinear grid cells;
        // a ~1-cell domain warp of the shoreline lookup breaks the stair-steps.
        vec2 shoreWarp = (vec2(noise3(p * 90.0 + 2.0), noise3(p * 90.0 + 6.0)) - 0.5) * 1.6 + (vec2(noise3(p * 260.0 + 4.0), noise3(p * 260.0 + 9.0)) - 0.5) * 0.6;
        float shoreElevation = texture(uDerived, uv + shoreWarp * vec2(du / coslat, dv)).r;
        float depth = uSeaLevel - shoreElevation - uShoreKm * shoreDetail * smoothstep(1.5 * uShoreKm, 0.0, abs(shoreElevation - uSeaLevel));
        float height = max(elevation - uSeaLevel, 0.0);
        float alat = abs(lat);
        // Surface temperature: equator-pole contrast and 6.5 K/km lapse rate.
        // Model freeboard exceeds Earth's ~0.8 km mean; saturate the climate altitude.
        float climateHeight = 3.0 * height / (height + 3.0);
        // Palaeoclimate anomaly with polar amplification (~2x at the poles).
        float tempK = 301.0 - 50.0 * pow(sin(alat), 2.0) - 6.5 * climateHeight + 5.0 * (noise3(p * 7.0) - 0.5)
            + uClimateK * (0.7 + 1.1 * pow(sin(alat), 2.0));
        vec3 surface;
        float glowVisible = 1.0;
        if (uWater > 0.001 && depth > 0.0) {
            // Ocean: Fresnel reflection, sun glint, and water-leaving radiance
            // that shows the seabed only in shallow shelf seas.
            float mu = max(dot(p, view), 0.0);
            float fresnel = 0.02 + 0.98 * pow(1.0 - mu, 5.0);
            // Sun glint: Cox & Munk (1954) wave-slope statistics, slope
            // variance 0.003 + 0.00512 W with patchy surface wind W (m/s).
            float wind = 1.0 + 6.0 * fbm5(p * 6.0 + vec3(uCloudPhase * 0.01, 0.0, 0.0))
                + 3.0 * (noise3(p * 45.0) - 0.5) + 1.5 * (noise3(p * 160.0) - 0.5);
            wind = max(wind, 0.5);
            float sigma2 = 0.003 + 0.00512 * wind;
            float cosB = max(dot(p, halfway), 1e-3);
            float tan2 = (1.0 - cosB * cosB) / (cosB * cosB);
            float fh = 0.02 + 0.98 * pow(1.0 - max(dot(view, halfway), 0.0), 5.0);
            float nlw = max(dot(p, light), 0.0);
            float glint = nlw > 0.0 ? fh * exp(-tan2 / sigma2) / (4.0 * sigma2 * pow(cosB, 4.0) * max(mu, 0.05)) : 0.0;
            // Water-leaving radiance: deep oligotrophic blue, greener over
            // productive/turbid water, turquoise over shallow shelves.
            float bloom = smoothstep(0.45, 0.8, fbm5(p * 11.0 + 4.0)) * uLife;
            vec3 deep = mix(vec3(0.0025, 0.008, 0.022), vec3(0.004, 0.011, 0.016), bloom);
            deep *= mix(0.85, 1.15, fbm5(p * 4.0 + 2.0));
            vec3 shelf = mix(vec3(0.02, 0.075, 0.08), albedo * 0.6, 0.3);
            vec3 water = mix(shelf, deep, smoothstep(0.02, 0.35, depth));
            // Sea ice: a ragged pack edge with a marginal zone of broken
            // floes, pressure-ridged pack and dark open leads.
            float iceField = tempK + 7.0 * (fbm5(p * 12.0 + 1.0) - 0.5) + 3.0 * (noise3(p * 55.0) - 0.5);
            float seaIce = smoothstep(267.5, 265.5, iceField);
            seaIce *= mix(1.0, smoothstep(0.42, 0.55, noise3(p * 170.0) * 0.7 + noise3(p * 420.0) * 0.3), smoothstep(262.0, 266.5, iceField));
            float lead = smoothstep(0.012, 0.0, abs(noise3(p * vec3(30.0, 90.0, 30.0) + 5.0) - 0.5)) * smoothstep(0.55, 0.75, noise3(p * 16.0 + 8.0)) * smoothstep(250.0, 262.0, iceField);
            vec3 ice = mix(vec3(0.5, 0.56, 0.62), vec3(0.78, 0.81, 0.85), smoothstep(0.3, 0.7, fbm5(p * 26.0 + 2.0)));
            ice = mix(ice, vec3(0.12, 0.16, 0.2), lead * 0.7);
            surface = mix(water * (1.0 - fresnel) * nlw + vec3(glint) * 0.9 + vec3(0.003, 0.006, 0.012) * fresnel * nlw,
                ice * nlw, seaIce);
            glowVisible = 0.0;
        } else {
            // Orbital hillshade: ranges read from space by their sunlit and
            // shadowed flanks (terminator-side relief), so land gets extra
            // vertical exaggeration over the shared normal.
            vec3 nLand = normalize(n - 1.5 * exaggeration * (dEast * east + dNorth * north) * uWater);
            float nl = max(dot(nLand, light), 0.0);
            if (uWater > 0.001) {
                // Chemical weathering under an atmosphere: oxidised soils and
                // sediments; deserts under the subtropical highs.
                float moisture = 0.9 * gauss(degrees(lat), 12.0) + 0.75 * gauss(degrees(alat) - 52.0, 14.0) + 0.15
                    - 0.6 * gauss(degrees(alat) - 24.0, 8.0) + 0.3 * (noise3(p * 6.0 + 3.0) - 0.5);
                moisture = clamp(moisture - 0.05 * climateHeight, 0.0, 1.0);
                // Geological provinces (pre-vegetation surface): grey shield
                // gneiss, oxidised red beds, dark mafic traps; deserts under
                // the subtropical highs are bright quartz sand seas.
                float prov = fbm5(p * 5.0 + 11.0);
                float prov2 = fbm5(p * 14.0 + 3.0);
                // Before the Great Oxidation (~2.4 Ga) weathering is anoxic:
                // grey-green, unoxidised regolith; afterwards ferric red beds.
                vec3 rock = mix(mix(mix(vec3(0.07, 0.078, 0.06), vec3(0.21, 0.185, 0.16), smoothstep(0.38, 0.62, fbm5(p * 9.0 + 5.0))), vec3(0.17, 0.165, 0.15), uOxic), vec3(0.24, 0.15, 0.09) * mix(0.75, 1.0, uOxic), smoothstep(0.45, 0.75, prov) * mix(0.2, 0.6, uOxic));
                rock = mix(rock, vec3(0.09, 0.085, 0.08), smoothstep(0.62, 0.78, 1.0 - prov) * (1.0 - felsic) * 0.8);
                // Greenstone belts: dark, chlorite-green metavolcanic strips.
                float belts = noise3(p * vec3(26.0, 14.0, 26.0) + 9.0);
                rock = mix(rock, vec3(0.10, 0.11, 0.095), smoothstep(0.6, 0.75, belts) * 0.35);
                // Without plants, gravel and bedrock dominate; aeolian sand seas
                // are rarer before land plants.
                float arid = smoothstep(0.35, 0.08, moisture) * mix(0.55, 1.0, uLife) * smoothstep(0.35, 0.6, prov2 + 0.25 * uLife);
                vec3 sand = mix(vec3(0.42, 0.33, 0.21), vec3(0.50, 0.40, 0.27), prov2);
                // Anoxic Archean sands: dark lithic/mafic grit, not quartz-white.
                sand = mix(vec3(0.15, 0.14, 0.115), sand, uOxic);
                // Rocky hamada plateaus and dark volcanic massifs between the ergs.
                sand = mix(sand, vec3(0.26, 0.20, 0.14), smoothstep(0.55, 0.72, prov) * 0.8);
                sand = mix(sand, vec3(0.13, 0.11, 0.10), smoothstep(0.7, 0.85, prov2) * smoothstep(0.5, 2.0, elevation));
                vec3 soil = mix(rock, sand, arid * smoothstep(0.25, 0.0, slope));
                soil *= mix(0.82, 1.15, noise3(p * 38.0) * 0.6 + noise3(p * 110.0) * 0.4);
                soil = mix(soil, soil * vec3(0.6, 0.56, 0.5), smoothstep(0.3, 0.8, moisture));
                // Steep slopes expose fresh, greyer rock.
                soil = mix(soil, vec3(0.19, 0.18, 0.17), smoothstep(0.15, 0.6, slope));
                // Orogens: thin soils and bare, darker crystalline rock with height.
                float upland = smoothstep(1.2, 3.5, height);
                soil = mix(soil, vec3(0.15, 0.14, 0.13), upland * 0.6);
                albedo = mix(albedo, mix(soil, albedo * 1.3, 0.25 * (1.0 - felsic)), uWater * 0.9);
                // Biomes: steppe/savanna (olive) to closed forest (very dark
                // green, broadleaf ~0.03-0.05 visible albedo); boreal is darker still.
                float veg = uLife * smoothstep(0.12, 0.45, moisture) * smoothstep(259.0, 275.0, tempK) * (1.0 - 0.7 * upland);
                vec3 grass = vec3(0.14, 0.13, 0.06);
                vec3 forest = mix(vec3(0.03, 0.06, 0.022), vec3(0.025, 0.04, 0.025), smoothstep(285.0, 270.0, tempK));
                vec3 plants = mix(grass, forest, smoothstep(0.35, 0.7, moisture + 0.15 * (prov2 - 0.5)));
                albedo = mix(albedo, plants, veg);
                float snow = uWater * smoothstep(258.0, 250.0, tempK + 4.0 * slope);
                albedo = mix(albedo, vec3(0.8, 0.82, 0.85), snow);
            }
            surface = (albedo + vec3(spec)) * nl + albedo * vec3(0.03, 0.045, 0.07) * max(dot(n, p), 0.0) * smoothstep(-0.1, 0.3, dot(p, light)) * uWater;
        }
        // Clouds and their shadows.
        float cloud = 0.0, shade = 0.0;
        float selfShade = 1.0;
        if ((uWater > 0.001 || uSteam > 0.001) && uClouds > 0.5) {
            float weather = min(uWater * 1.5, 1.0);
            vec3 ps = normalize(p + light * 0.012);
            float base = cloudAt(p) * weather;
            float toward = cloudAt(ps) * weather;
            cloud = max(base, steamDeck(p));
            shade = max(toward, steamDeck(ps));
            // Thick cloud is brighter; its sun-facing flanks brighter still.
            selfShade = (0.78 + 0.22 * base) * clamp(1.0 + 0.25 * (base - toward), 0.9, 1.08);
        }
        float nlg = max(dot(p, light), 0.0);
        float deckTone = uSteam > 0.001 ? mix(1.0, steamTone(p), smoothstep(0.0, 0.3, uSteam)) : 1.0;
        vec3 cloudColor = mix(vec3(0.80, 0.81, 0.83), vec3(0.76, 0.72, 0.64), uSteam) * selfShade * deckTone;
        vec3 lit = surface * (1.0 - 0.4 * shade) * (1.0 - cloud) + cloudColor * cloud * smoothstep(-0.05, 0.25, dot(p, light)) * max(nlg, 0.04);
        // Rayleigh airlight over the disc (blue Earth, brighter at the limb).
        float mu = max(dot(p, view), 0.0);
        // Direct sunlight reddens near the terminator (Rayleigh + aerosol
        // extinction along the slant path, Chapman-like airmass).
        // Normalised to the overhead sun: diffuse skylight returns most of the
        // scattered blue, so midday illumination stays white.
        float airmass = 1.0 / (max(nlg, 0.0) + 0.15 * exp(-8.0 * max(nlg, 0.0)) + 0.02);
        vec3 transmit = exp(-vec3(0.06, 0.13, 0.30) * max(min(airmass, 40.0) - 1.0, 0.0) * 0.6 * max(uWater, 0.3 * uLithosphereWeight));
        lit *= transmit;
        vec3 airlight = vec3(0.005, 0.013, 0.038) * (1.0 + 2.5 * pow(1.0 - mu, 2.0)) * nlg * max(uWater, 0.3 * uLithosphereWeight);
        reflected += (lit + airlight) * uSolar * uLithosphereWeight;
        // Fresh lava: coverage-weighted incandescence (display persistence);
        // submarine eruptions are quenched and steam-covered.
        emitted += thermalRadiance(uVolcanicGlowKelvin).rgb * 0.9 * derived.g * 0.35 * uLithosphereWeight * glowVisible * (1.0 - cloud);
    }
    vec3 linear = (emitted + reflected) * uExposure;
    linear /= 1.0 + max(linear.r, max(linear.g, linear.b));
    outColor = vec4(displaySrgb(linear), 1.0);
}`;

// ---------------------------------------------------------------- helpers --

function createFloatTexture(gl, width, height, internalFormat, format, type, filter, data = null) {
    const texture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texImage2D(gl.TEXTURE_2D, 0, internalFormat, width, height, 0, format, type, data);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, filter);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, filter);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    return texture;
}

function createTarget(gl, textures) {
    const framebuffer = gl.createFramebuffer();
    gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer);
    textures.forEach((texture, i) => gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0 + i, gl.TEXTURE_2D, texture, 0));
    const status = gl.checkFramebufferStatus(gl.FRAMEBUFFER);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    if (status !== gl.FRAMEBUFFER_COMPLETE) throw new Error(`Evolution framebuffer incomplete (${status}).`);
    return { framebuffer, count: textures.length };
}

function uniforms(gl, program, names) {
    const map = {};
    for (const name of names) map[name] = gl.getUniformLocation(program, name);
    return map;
}

function rotationMatrix(axis, angle) {
    const [x, y, z] = axis;
    const c = Math.cos(angle), s = Math.sin(angle), t = 1 - c;
    // Row-major rotation R; returned column-major for GLSL.
    const r = [
        t * x * x + c, t * x * y - s * z, t * x * z + s * y,
        t * x * y + s * z, t * y * y + c, t * y * z - s * x,
        t * x * z - s * y, t * y * z + s * x, t * z * z + c,
    ];
    return r;
}

function applyRotation(r, v) {
    return [r[0] * v[0] + r[1] * v[1] + r[2] * v[2], r[3] * v[0] + r[4] * v[1] + r[5] * v[2], r[6] * v[0] + r[7] * v[1] + r[8] * v[2]];
}
function cross3(a, b) { return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]; }
function add3(a, b) { return [a[0] + b[0], a[1] + b[1], a[2] + b[2]]; }
function scale3(a, s) { return [a[0] * s, a[1] * s, a[2] * s]; }
function normalize3(a) { const l = Math.hypot(a[0], a[1], a[2]) || 1; return [a[0] / l, a[1] / l, a[2] / l]; }

function transposeInto(target, offset, r) {
    // GLSL mat3 is column-major. Supplying R row-major as columns yields R^T,
    // which is the inverse (back) rotation we want.
    for (let i = 0; i < 9; i++) target[offset + i] = r[i];
}

// ------------------------------------------------------------------ setup --

export function createEvolution(gl, seed, createProgram, options = {}) {
    if (!gl.getExtension('EXT_color_buffer_float')) throw new Error('Planet evolution requires EXT_color_buffer_float.');
    const random = mulberry32((seed ^ 0x9E3779B9) >>> 0);
    const programs = {
        initLid: createProgram(gl, fullscreenVertex, initLidFragment),
        partition: createProgram(gl, fullscreenVertex, partitionFragment),
        compose: createProgram(gl, fullscreenVertex, composeFragment),
        process: createProgram(gl, fullscreenVertex, processFragment),
        derived: createProgram(gl, fullscreenVertex, derivedFragment),
        globe: createProgram(gl, globeVertex, globeFragment),
        stars: createProgram(gl, fullscreenVertex, starFragment),
    };
    const make32 = () => createFloatTexture(gl, GRID_WIDTH, GRID_HEIGHT, gl.RGBA32F, gl.RGBA, gl.FLOAT, gl.NEAREST);
    const state0 = [make32(), make32()];
    const state1 = [make32(), make32()];
    const compose0 = make32();
    const compose1 = make32();
    const derived = createFloatTexture(gl, GRID_WIDTH, GRID_HEIGHT, gl.RGBA16F, gl.RGBA, gl.HALF_FLOAT, gl.LINEAR);
    const targets = {
        state: [createTarget(gl, [state0[0], state1[0]]), createTarget(gl, [state0[1], state1[1]])],
        compose: createTarget(gl, [compose0, compose1]),
        derived: createTarget(gl, [derived]),
    };
    const raftRows = maximumRafts / 1024;
    const raftPosition = createFloatTexture(gl, 1024, raftRows, gl.RGBA32F, gl.RGBA, gl.FLOAT, gl.NEAREST);
    const raftVelocity = createFloatTexture(gl, 1024, raftRows, gl.RGBA32F, gl.RGBA, gl.FLOAT, gl.NEAREST);
    const raftFrame = createFloatTexture(gl, 1024, raftRows, gl.RGBA32F, gl.RGBA, gl.FLOAT, gl.NEAREST);
    const raftLink = createFloatTexture(gl, 1024, raftRows, gl.RGBA32F, gl.RGBA, gl.FLOAT, gl.NEAREST);
    const binRows = Math.ceil(48 * 48 * 48 * binSlots / 1024);
    const bins = createFloatTexture(gl, 1024, binRows, gl.R32F, gl.RED, gl.FLOAT, gl.NEAREST);
    const ageTable = createFloatTexture(gl, AGE_TABLE_SIZE, 1, gl.RGBA32F, gl.RGBA, gl.FLOAT, gl.NEAREST);
    const vao = gl.createVertexArray();

    const evo = {
        gl, seed, random, programs, vao,
        textures: { state0, state1, compose0, compose1, derived, raftPosition, raftVelocity, raftFrame, raftLink, bins, ageTable },
        targets, current: 0,
        uniforms: {
            initLid: uniforms(gl, programs.initLid, ['uTimeMyr']),
            partition: uniforms(gl, programs.partition, ['uS0', 'uS1', 'uMode', 'uSeedCount', 'uSeeds', 'uSplitPlate', 'uNewPlate', 'uSplitNormal', 'uWarp']),
            compose: uniforms(gl, programs.compose, ['uS0', 'uS1', 'uPlateCount', 'uBack', 'uRank', 'uTimeMyr']),
            process: uniforms(gl, programs.process, ['uC0', 'uC1', 'uTimeMyr', 'uDtMyr', 'uRegime', 'uRadiusKm', 'uPlumeCount', 'uPlumes',
                'uPlumeRates', 'uVentCount', 'uVents', 'uResurfaceKmPerMyr', 'uImpactCount', 'uImpacts', 'uRecycleMyr', 'uFelsicRate', 'uArcRate', 'uSeedPhase']),
            derived: uniforms(gl, programs.derived, ['uC0', 'uC1', 'uTimeMyr', 'uGlowWindowMyr']),
            stars: uniforms(gl, programs.stars, ['uInverseViewProjection', 'uCamera', 'uViewport']),
            globe: uniforms(gl, programs.globe, ['uViewProjection', 'uDerived', 'uReliefScale', 'uLithosphereWeight', 'uDatumKm', 'uLight',
                'uCamera', 'uExposure', 'uMagmaWeight', 'uRadiusMeters', 'uMeltKelvin', 'uRenewalVelocity', 'uDisplaySpeed', 'uSolar', 'uRaftCount', 'uBinGrid',
                'uRaftPosition', 'uRaftVelocity', 'uRaftFrame', 'uRaftLink', 'uMaterialScale', 'uBins', 'uAgeTable', 'uThermalRadianceTexture', 'uVolcanicGlowKelvin',
                'uSeaLevel', 'uShoreKm', 'uWater', 'uSteam', 'uLife', 'uClimateK', 'uOxic', 'uCloudPhase', 'uShell', 'uClouds', 'uSeamShare', 'uRaftSpacing',
                'uMagmaImpactCount', 'uMagmaImpacts', 'uMagmaImpactInfo']),
        },
        globePosition: gl.getAttribLocation(programs.globe, 'aPosition'),
        maximumYearsPerSecond: options.maximumYearsPerSecond ?? 20e6,
        playing: options.playing ?? true,
        thermal: createThermalHistory(),
        rafts: null,
        raftUpload: new Float32Array(maximumRafts * 4),
        raftVelocityUpload: new Float32Array(maximumRafts * 4),
        ageTableData: new Float32Array(AGE_TABLE_SIZE * 4),
        ageTableKelvin: -1,
        ageTableLifetime: -1,
        magmaWeight: 1.0,
        lithosphereWeight: 0.0,
        lithosphereReady: false,
        plates: { count: 1, poles: [[0, 1, 0]], rates: [0], angles: [0], back: new Float32Array(MAX_PLATES * 9), rank: new Float32Array(MAX_PLATES), centers: [] },
        lastBakeMyr: 0,
        nextReorganizationMyr: Infinity,
        plumes: [],
        vents: [],
        impacts: [],
        magmaImpacts: [],
        nextMagmaImpact: 2.5,
        wallTime: 0,
        impactDebt: 0,
        impactCount: 0,
        exposure: 1.0,
        foundered: 0,
        frame: 0,
        lastStatus: null,
        datumKm: -3.4,
        water: 0,
        steam: 0,
        life: 0,
        seaLevel: -100,
        seaLevelTarget: -100,
        seaTimer: 0,
        cloudPhase: 0,
        showClouds: true,
        lidOnsetSeconds: null,
    };
    identityBack(evo.plates.back, 0);
    if (options.earthHistory !== false && typeof DecompressionStream !== 'undefined') {
        // Reconstructed Earth history (1.8 Ga -> today); without it the
        // emergent plate model simply continues.
        evo.earthLoad = loadEarthAssets(options.earthDataUrl)
            .then(assets => {
                if (evo.disposed) return;
                evo.earth = createEarthHistory(gl, assets, createProgram, fullscreenVertex, sphereCommon, GRID_WIDTH, GRID_HEIGHT);
                return prepareEarthHistory(evo.earth);
            })
            .catch(error => { evo.earthError = String(error); console.warn('Earth history unavailable:', error); });
    }
    evo.rafts = createRaftSystem(seed, raftSpacing(evo));
    updateAgeTable(evo, true);
    uploadRafts(evo);
    return evo;
}

function identityBack(target, plate) {
    target.set([1, 0, 0, 0, 1, 0, 0, 0, 1], plate * 9);
}

function raftSpacing(evo) {
    // Rafts weld into larger plates as the crystal fraction approaches the
    // rheological transition (display scale: 280 km -> 900 km).
    const phi = crystalFraction(evo.thermal.potentialKelvin) / PARAMETERS.rheologicalTransition;
    return (280e3 + 620e3 * Math.min(1, Math.max(0, phi)) ** 1.5) / PARAMETERS.radiusMeters;
}

export function disposeEvolution(evo) {
    if (!evo) return;
    const gl = evo.gl;
    evo.disposed = true;
    disposeEarthHistory(evo.earth);
    for (const program of Object.values(evo.programs)) gl.deleteProgram(program);
    const t = evo.textures;
    for (const texture of [...t.state0, ...t.state1, t.compose0, t.compose1, t.derived, t.raftPosition, t.raftVelocity, t.raftFrame, t.raftLink, t.bins, t.ageTable]) {
        gl.deleteTexture(texture);
    }
    for (const target of [...evo.targets.state, evo.targets.compose, evo.targets.derived]) gl.deleteFramebuffer(target.framebuffer);
    gl.deleteVertexArray(evo.vao);
}

// ------------------------------------------------------- magma-ocean data --

function updateAgeTable(evo, force = false) {
    const melt = evo.thermal.potentialKelvin;
    if (!force && Math.abs(melt - evo.ageTableKelvin) < 2.0) return;
    const data = evo.ageTableData;
    let previousAge = 0;
    let previousRadiance = emissionLinearRadiance(melt);
    const cumulative = [0, 0, 0];
    for (let i = 0; i < AGE_TABLE_SIZE; i++) {
        const age = Math.pow(10, AGE_LOG_MIN + (AGE_LOG_MAX - AGE_LOG_MIN) * i / (AGE_TABLE_SIZE - 1));
        const ts = crustSurfaceKelvin(crustThicknessMeters(age), melt);
        const radiance = emissionLinearRadiance(ts);
        for (let c = 0; c < 3; c++) {
            cumulative[c] += 0.5 * (radiance[c] + previousRadiance[c]) * (age - previousAge);
            data[i * 4 + c] = cumulative[c];
        }
        data[i * 4 + 3] = ts;
        previousAge = age;
        previousRadiance = radiance;
    }
    const gl = evo.gl;
    gl.bindTexture(gl.TEXTURE_2D, evo.textures.ageTable);
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, AGE_TABLE_SIZE, 1, gl.RGBA, gl.FLOAT, data);
    evo.ageTableKelvin = melt;
}

function uploadRafts(evo) {
    const gl = evo.gl;
    const rafts = evo.rafts;
    const magma = evo.thermal.magma;
    const lifetime = magma ? magma.lifetimeSeconds : 1e9;
    const elapsed = Math.max(evo.thermal.timeSeconds, 1e-3);
    const position = evo.raftUpload;
    const velocity = evo.raftVelocityUpload;
    const link = evo.raftLinkUpload ??= new Float32Array(maximumRafts * 4);
    // Rafts welded into one plate share most of their crust history.
    const plateAge = new Map();
    for (let i = 0; i < rafts.count; i++) {
        const entry = plateAge.get(rafts.cluster[i]) ?? [0, 0];
        entry[0] += rafts.phase[i]; entry[1]++;
        plateAge.set(rafts.cluster[i], entry);
    }
    for (let i = 0; i < rafts.count; i++) {
        const entry = plateAge.get(rafts.cluster[i]);
        const phase = entry[0] / entry[1];
        position[i * 4] = rafts.position[i * 3];
        position[i * 4 + 1] = rafts.position[i * 3 + 1];
        position[i * 4 + 2] = rafts.position[i * 3 + 2];
        // Raft interiors carry crust aged across the renewal cycle; new melt
        // is exposed at the opening seams (seamIntegral), so even a newborn
        // raft's interior is drawn from mid-life crust: a whole Voronoi cell
        // never lights up at once.
        position[i * 4 + 3] = Math.min((0.6 + 0.4 * phase) * lifetime, elapsed);
        // The plate's Euler vector (rad/s of display time): the shader
        // evaluates plate velocities at each pixel, w x p.
        const omega = rafts.plateOmega.get(rafts.cluster[i]) ?? [0, 0, 0];
        const displaySpeed = evo.displaySpeed ?? 0.02;
        velocity[i * 4] = omega[0] * displaySpeed;
        velocity[i * 4 + 1] = omega[1] * displaySpeed;
        velocity[i * 4 + 2] = omega[2] * displaySpeed;
        velocity[i * 4 + 3] = rafts.displayId[i];
        link[i * 4] = rafts.linkPartner[i];
        link[i * 4 + 1] = rafts.linkFactor[i];
    }
    const rows = Math.max(1, Math.ceil(rafts.count / 1024));
    gl.bindTexture(gl.TEXTURE_2D, evo.textures.raftPosition);
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, 1024, rows, gl.RGBA, gl.FLOAT, position, 0);
    gl.bindTexture(gl.TEXTURE_2D, evo.textures.raftVelocity);
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, 1024, rows, gl.RGBA, gl.FLOAT, velocity, 0);
    gl.bindTexture(gl.TEXTURE_2D, evo.textures.raftLink);
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, 1024, rows, gl.RGBA, gl.FLOAT, link, 0);
    const frameUpload = evo.raftFrameUpload ??= new Float32Array(maximumRafts * 4);
    frameUpload.set(rafts.orientation.subarray(0, rows * 1024 * 4));
    gl.bindTexture(gl.TEXTURE_2D, evo.textures.raftFrame);
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, 1024, rows, gl.RGBA, gl.FLOAT, frameUpload, 0);
    const binCount = rafts.bins.length;
    const binRows = Math.ceil(binCount / 1024);
    const padded = binRows * 1024 === binCount ? rafts.bins : (() => {
        const buffer = new Float32Array(binRows * 1024).fill(-1);
        buffer.set(rafts.bins);
        return buffer;
    })();
    gl.bindTexture(gl.TEXTURE_2D, evo.textures.bins);
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, 1024, binRows, gl.RED, gl.FLOAT, padded, 0);
}

// -------------------------------------------------------------- GPU passes --

function bindTexture(gl, unit, texture, location) {
    gl.activeTexture(gl.TEXTURE0 + unit);
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.uniform1i(location, unit);
}

function runPass(evo, target, program, setup) {
    const gl = evo.gl;
    gl.bindFramebuffer(gl.FRAMEBUFFER, target.framebuffer);
    gl.drawBuffers(Array.from({ length: target.count }, (_, i) => gl.COLOR_ATTACHMENT0 + i));
    gl.viewport(0, 0, GRID_WIDTH, GRID_HEIGHT);
    gl.disable(gl.DEPTH_TEST);
    gl.disable(gl.BLEND);
    gl.disable(gl.CULL_FACE);
    gl.useProgram(program);
    setup();
    gl.bindVertexArray(evo.vao);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    gl.bindVertexArray(null);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
}

function timeMyr(evo) { return evo.thermal.timeSeconds / MYR; }

// Global-mean surface temperature anomaly vs present (K) against age (Ma).
// Hot post-steam Hadean ocean cooling as CO2 is drawn down (Sleep et al.
// 2001), warm Archean/Proterozoic, Huronian and Cryogenian (Sturtian,
// Marinoan) snowballs, Hirnantian and Late Palaeozoic ice ages, Mesozoic
// and Eocene hothouses (after Scotese et al. 2021).
const CLIMATE_CURVE = [
    [4567, 60], [4560, 45], [4500, 25], [4000, 15], [3000, 12], [2450, 8], [2420, -25], [2250, -25], [2200, 8],
    [800, 6], [720, 2], [715, -60], [662, -60], [655, 5], [650, -60], [636, -60], [630, 8], [500, 12], [450, 6],
    [445, -2], [440, 8], [360, 6], [340, -2], [285, -1], [260, 8], [100, 12], [66, 10], [50, 13], [34, 4], [15, 5],
    [3, 2], [0, 0],
];
function climateAnomalyK(ma) {
    if (ma >= CLIMATE_CURVE[0][0]) return CLIMATE_CURVE[0][1];
    for (let i = 1; i < CLIMATE_CURVE.length; i++) {
        const [a1, k1] = CLIMATE_CURVE[i];
        if (ma >= a1) {
            const [a0, k0] = CLIMATE_CURVE[i - 1];
            return k1 + (k0 - k1) * (ma - a1) / (a0 - a1);
        }
    }
    return 0;
}

function initializeLithosphere(evo) {
    const gl = evo.gl;
    for (const target of evo.targets.state) {
        runPass(evo, target, evo.programs.initLid, () => gl.uniform1f(evo.uniforms.initLid.uTimeMyr, timeMyr(evo)));
    }
    evo.current = 0;
    evo.lastBakeMyr = timeMyr(evo);
    evo.lithosphereReady = true;
}

function partition(evo, setup) {
    const gl = evo.gl;
    const u = evo.uniforms.partition;
    const source = evo.current;
    const destination = 1 - source;
    runPass(evo, evo.targets.state[destination], evo.programs.partition, () => {
        bindTexture(gl, 0, evo.textures.state0[source], u.uS0);
        bindTexture(gl, 1, evo.textures.state1[source], u.uS1);
        setup(u);
    });
    evo.current = destination;
}

function compose(evo) {
    const gl = evo.gl;
    const u = evo.uniforms.compose;
    runPass(evo, evo.targets.compose, evo.programs.compose, () => {
        bindTexture(gl, 0, evo.textures.state0[evo.current], u.uS0);
        bindTexture(gl, 1, evo.textures.state1[evo.current], u.uS1);
        gl.uniform1i(u.uPlateCount, evo.plates.count);
        gl.uniformMatrix3fv(u.uBack, false, evo.plates.back);
        gl.uniform1fv(u.uRank, evo.plates.rank);
        gl.uniform1f(u.uTimeMyr, timeMyr(evo));
    });
}

function processLithosphere(evo, dtMyr) {
    const gl = evo.gl;
    const u = evo.uniforms.process;
    const destination = 1 - evo.current;
    const plates = evo.thermal.regime === 'plates';
    const heatPipe = evo.thermal.heatPipeShare;
    const plumeData = new Float32Array(MAX_PLUMES * 4);
    const plumeRates = new Float32Array(MAX_PLUMES);
    evo.plumes.slice(0, MAX_PLUMES).forEach((plume, i) => {
        plumeData.set([...plume.direction, plume.radius], i * 4);
        plumeRates[i] = plume.rate;
    });
    const impacts = evo.impacts.splice(0, evo.impacts.length).slice(-MAX_IMPACTS);
    const impactData = new Float32Array(MAX_IMPACTS * 4);
    impacts.forEach((impact, i) => impactData.set([...impact.direction, impact.radius], i * 4));
    const ventData = new Float32Array(MAX_VENTS * 4);
    evo.vents.slice(0, MAX_VENTS).forEach((vent, i) => ventData.set([...vent.direction, vent.radius], i * 4));
    runPass(evo, evo.targets.state[destination], evo.programs.process, () => {
        bindTexture(gl, 0, evo.textures.compose0, u.uC0);
        bindTexture(gl, 1, evo.textures.compose1, u.uC1);
        gl.uniform1f(u.uTimeMyr, timeMyr(evo));
        gl.uniform1f(u.uDtMyr, dtMyr);
        gl.uniform1i(u.uRegime, plates ? 2 : 1);
        gl.uniform1f(u.uRadiusKm, PARAMETERS.radiusMeters / 1000);
        gl.uniform1i(u.uPlumeCount, Math.min(evo.plumes.length, MAX_PLUMES));
        gl.uniform4fv(u.uPlumes, plumeData);
        gl.uniform1fv(u.uPlumeRates, plumeRates);
        gl.uniform1i(u.uVentCount, Math.min(evo.vents.length, MAX_VENTS));
        gl.uniform4fv(u.uVents, ventData);
        gl.uniform1i(u.uImpactCount, impacts.length);
        gl.uniform4fv(u.uImpacts, impactData);
        // Heat-pipe magma flux ~ share x q A / (rho (cp dT + L)): ~1-2 km/Myr
        // of global resurfacing (Moore & Webb 2013); recycled at the lid base.
        gl.uniform1f(u.uResurfaceKmPerMyr, plates ? 0.0 : 0.35 * heatPipe);
        gl.uniform1f(u.uRecycleMyr, 40.0);
        gl.uniform1f(u.uFelsicRate, 0.02);
        gl.uniform1f(u.uArcRate, 0.9);
        gl.uniform1f(u.uSeedPhase, (evo.seed % 997) / 997);
    });
    evo.current = destination;
    evo.vents = [];
}

// Plates are rigid, connected bodies. Resampling, continental override and
// the suture vote can leave detached pieces of one plate inside another; a
// stray piece would then plough through its host and smear into a ribbon.
// Every few bakes each plate keeps only its largest connected component and
// detached pieces are accreted to the plate that surrounds them.
function consolidatePlates(evo, continentMinority = 0.3) {
    const gl = evo.gl;
    const W = GRID_WIDTH, H = GRID_HEIGHT, N = W * H;
    const field = readEvolutionField(evo, 'state0');
    const data = field.data;
    const ids = new Int16Array(N);
    for (let i = 0; i < N; i++) ids[i] = Math.round(data[i * 4]);
    const unified = unifyContinents(evo, data, ids, continentMinority);
    const label = evo.labelScratch ?? (evo.labelScratch = new Int32Array(N));
    const stack = evo.stackScratch ?? (evo.stackScratch = new Int32Array(N));
    label.fill(-1);
    const sizes = [], owners = [];
    for (let start = 0; start < N; start++) {
        if (label[start] >= 0) continue;
        const id = ids[start];
        const component = sizes.length;
        let top = 0, size = 0;
        stack[top++] = start; label[start] = component;
        while (top > 0) {
            const i = stack[--top]; size++;
            const x = i % W, y = (i - x) / W;
            const n1 = y * W + (x + 1) % W, n2 = y * W + (x + W - 1) % W;
            if (label[n1] < 0 && ids[n1] === id) { label[n1] = component; stack[top++] = n1; }
            if (label[n2] < 0 && ids[n2] === id) { label[n2] = component; stack[top++] = n2; }
            if (y + 1 < H) { const n = i + W; if (label[n] < 0 && ids[n] === id) { label[n] = component; stack[top++] = n; } }
            if (y > 0) { const n = i - W; if (label[n] < 0 && ids[n] === id) { label[n] = component; stack[top++] = n; } }
        }
        sizes.push(size); owners.push(id);
    }
    const largest = new Map();
    for (let k = 0; k < sizes.length; k++) {
        const best = largest.get(owners[k]);
        if (best === undefined || sizes[k] > sizes[best]) largest.set(owners[k], k);
    }
    const strays = new Map();
    for (let k = 0; k < sizes.length; k++) if (largest.get(owners[k]) !== k && owners[k] >= 0) strays.set(k, new Map());
    const areas = new Float64Array(MAX_PLATES);
    for (let k = 0; k < sizes.length; k++) if (owners[k] >= 0 && owners[k] < MAX_PLATES) areas[owners[k]] += sizes[k];
    if (strays.size === 0 && unified === 0) { evo.plateAreas = Array.from(areas, a => a / N); return 0; }
    for (let i = 0; i < N; i++) {
        const votes = strays.get(label[i]);
        if (!votes) continue;
        const x = i % W, y = (i - x) / W;
        for (const n of [y * W + (x + 1) % W, y * W + (x + W - 1) % W, y + 1 < H ? i + W : -1, y > 0 ? i - W : -1]) {
            if (n < 0 || label[n] === label[i]) continue;
            votes.set(ids[n], (votes.get(ids[n]) ?? 0) + 1);
        }
    }
    const target = new Map();
    for (const [k, votes] of strays) {
        let bestId = owners[k], bestVotes = 0;
        for (const [id, v] of votes) if (id >= 0 && v > bestVotes) { bestVotes = v; bestId = id; }
        target.set(k, bestId);
    }
    let moved = 0;
    for (let i = 0; i < N; i++) {
        const to = target.get(label[i]);
        if (to === undefined || to === ids[i]) continue;
        data[i * 4] = to; moved++;
        if (ids[i] >= 0) areas[ids[i]]--;
        areas[to]++;
    }
    evo.plateAreas = Array.from(areas, a => a / N);
    if (moved + unified === 0) return 0;
    gl.bindTexture(gl.TEXTURE_2D, evo.textures.state0[evo.current]);
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, W, H, gl.RGBA, gl.FLOAT, data);
    gl.bindTexture(gl.TEXTURE_2D, null);
    return moved;
}

// Plate boundaries localise in weak oceanic lithosphere, so a continent
// (connected crust > 12 km) rides on one plate: slivers cut off by the
// partition join the plate carrying most of it. A large share on another
// plate is a genuine rift and is left alone.
function unifyContinents(evo, data, ids, minorityLimit) {
    const W = GRID_WIDTH, H = GRID_HEIGHT, N = W * H;
    const mark = evo.continentScratch ?? (evo.continentScratch = new Uint8Array(N));
    const members = evo.memberScratch ?? (evo.memberScratch = new Int32Array(N));
    mark.fill(0);
    let moved = 0;
    const isContinent = i => data[i * 4 + 2] > 12.0 && ids[i] >= 0;
    for (let start = 0; start < N; start++) {
        if (mark[start] || !isContinent(start)) continue;
        let count = 0, head = 0;
        members[count++] = start; mark[start] = 1;
        while (head < count) {
            const i = members[head++];
            const x = i % W, y = (i - x) / W;
            const e = y * W + (x + 1) % W, w = y * W + (x + W - 1) % W;
            if (!mark[e] && isContinent(e)) { mark[e] = 1; members[count++] = e; }
            if (!mark[w] && isContinent(w)) { mark[w] = 1; members[count++] = w; }
            if (y + 1 < H) { const n = i + W; if (!mark[n] && isContinent(n)) { mark[n] = 1; members[count++] = n; } }
            if (y > 0) { const n = i - W; if (!mark[n] && isContinent(n)) { mark[n] = 1; members[count++] = n; } }
        }
        const votes = new Map();
        for (let m = 0; m < count; m++) votes.set(ids[members[m]], (votes.get(ids[members[m]]) ?? 0) + 1);
        if (votes.size < 2) continue;
        let owner = -1, best = 0;
        for (const [id, v] of votes) if (v > best) { best = v; owner = id; }
        if ((count - best) / count > minorityLimit) continue;
        for (let m = 0; m < count; m++) {
            const i = members[m];
            if (ids[i] !== owner) { ids[i] = owner; data[i * 4] = owner; moved++; }
        }
    }
    return moved;
}

function derive(evo, glowWindowMyr) {
    const gl = evo.gl;
    const u = evo.uniforms.derived;
    runPass(evo, evo.targets.derived, evo.programs.derived, () => {
        bindTexture(gl, 0, evo.textures.compose0, u.uC0);
        bindTexture(gl, 1, evo.textures.compose1, u.uC1);
        gl.uniform1f(u.uTimeMyr, timeMyr(evo));
        gl.uniform1f(u.uGlowWindowMyr, glowWindowMyr);
    });
}

// ---------------------------------------------------------------- impacts --

// Impactor flux: declining Hadean bombardment (e-folding ~150 Myr) on top of
// the present-day background, normalised to ~1000 craters D > 150 km over the
// first Gyr (Marchi et al. 2014); cumulative size-frequency N(>D) ~ D^-2.
function updateImpacts(evo, dtMyr) {
    const t = timeMyr(evo);
    const ratePerMyr = 6.7 * Math.exp(-t / 150) + 0.02;
    evo.impactDebt += ratePerMyr * dtMyr;
    while (evo.impactDebt >= 1) {
        evo.impactDebt -= 1;
        const u = Math.max(evo.random(), 1e-4);
        const radiusKm = Math.min(75 / Math.sqrt(u), 1200);
        evo.impacts.push({ direction: randomDirection(evo.random), radius: radiusKm / (PARAMETERS.radiusMeters / 1000) });
        evo.impactCount++;
        if (evo.impacts.length > 64) evo.impacts.shift();
    }
}

// Visible impacts into the magma ocean, paced in display (wall) time: one
// every few seconds stands in for the Hadean flux (display liberty).
function updateMagmaImpacts(evo) {
    evo.magmaImpacts = evo.magmaImpacts.filter(i => evo.wallTime - i.born < 30);
    if (evo.magmaWeight < 0.3 || evo.wallTime < evo.nextMagmaImpact) return;
    const random = evo.impactRandom ??= mulberry32((evo.seed ^ 0x5bd1e995) >>> 0);
    evo.nextMagmaImpact = evo.wallTime + 5 + 7 * random();
    // N(>R) ~ R^-2: mostly ~250-km craters, occasionally a basin.
    const radius = Math.min(0.028 / Math.sqrt(Math.max(random(), 0.05)), 0.09);
    // Aimed at the hemisphere facing the viewer so the events are seen
    // (display liberty; the real flux is isotropic).
    let direction = randomDirection(random);
    const view = evo.viewDirection;
    for (let tries = 0; view && tries < 24 && direction[0] * view[0] + direction[1] * view[1] + direction[2] * view[2] < 0.45; tries++) direction = randomDirection(random);
    evo.magmaImpacts.push({ direction, radius, born: evo.wallTime, seed: random() * 100 });
    if (evo.magmaImpacts.length > MAX_MAGMA_IMPACTS) evo.magmaImpacts.shift();
}

// ----------------------------------------------------------- volcanism/plates --

function updatePlumes(evo, dtMyr) {
    const now = timeMyr(evo);
    evo.plumes = evo.plumes.filter(plume => plume.endMyr > now);
    const plates = evo.thermal.regime === 'plates';
    // Heat-pipe era: many large volcanic centres; plate era: a few hotspots.
    const target = plates ? 7 : Math.round(6 + 22 * evo.thermal.heatPipeShare);
    let guard = 0;
    while (evo.plumes.length < target && guard++ < 64) {
        evo.plumes.push({
            direction: randomDirection(evo.random),
            radius: (plates ? 0.8 + evo.random() * 0.7 : 0.6 + evo.random() * 1.6) * Math.PI / 180,
            rate: plates ? 0.8 + evo.random() * 1.0 : 0.15 + evo.random() * 0.5,
            endMyr: now + (plates ? 80 + evo.random() * 220 : 40 + evo.random() * 160) * (0.4 + 0.6 * evo.random()),
        });
    }
    if (!plates) {
        const count = Math.min(MAX_VENTS, Math.round(evo.thermal.heatPipeShare * 7 * (0.5 + evo.random())));
        for (let i = 0; i < count; i++) {
            evo.vents.push({ direction: randomDirection(evo.random), radius: (0.5 + evo.random() * 1.6) * Math.PI / 180 });
        }
    }
}

function startPlateTectonics(evo) {
    const count = 9 + Math.floor(evo.random() * 4);
    const seeds = [];
    for (let i = 0; i < count; i++) seeds.push(randomDirection(evo.random));
    partition(evo, u => {
        evo.gl.uniform1i(u.uMode, 0);
        evo.gl.uniform1i(u.uSeedCount, count);
        evo.gl.uniform3fv(u.uSeeds, new Float32Array(Array.from({ length: MAX_PLATES }, (_, i) => seeds[i] ?? [0, 0, 0]).flat()));
        evo.gl.uniform1f(u.uWarp, 0.55);
    });
    evo.plates.count = count;
    evo.plates.centers = seeds.map(s => [...s]);
    // The weak zones that become the first plate boundaries avoid cratons.
    evo.consolidated = consolidatePlates(evo, 1.0);
    assignPlateMotions(evo);
    evo.lastBakeMyr = timeMyr(evo);
    evo.nextReorganizationMyr = timeMyr(evo) + 120 + evo.random() * 120;
}

function assignPlateMotions(evo) {
    const plates = evo.plates;
    for (let k = 0; k < plates.count; k++) {
        // Slab pull drives plates along great circles: Euler poles lie far
        // from the plate itself (motion is mostly translation, not spin).
        const center = plates.centers[k] ?? randomDirection(evo.random);
        const heading = randomDirection(evo.random);
        const pole = normalize3(add3(cross3(center, heading), scale3(center, (evo.random() - 0.5) * 0.5)));
        plates.poles[k] = pole;
        // 2-9 cm/yr surface speed at the plate's equator (present-day range).
        const speed = 0.02 + evo.random() * 0.05;
        plates.rates[k] = (speed / PARAMETERS.radiusMeters) * (evo.random() < 0.5 ? -1 : 1); // rad/yr
        plates.angles[k] = 0;
        plates.rank[k] = evo.random();
        identityBack(plates.back, k);
    }
}

function reorganizePlates(evo) {
    // Plate reorganisation (changing slab pull) and occasional rifting of a
    // plate into two, which opens new ocean basins (Wilson cycle).
    // Large plates rift (supercontinent insulation, slab roll-back); plates
    // consumed at trenches leave the census, so their ids are reused.
    const areas = evo.plateAreas ?? [];
    let split = Math.floor(evo.random() * evo.plates.count);
    for (let k = 0; k < evo.plates.count; k++) if ((areas[k] ?? 0) > (areas[split] ?? 0)) split = k;
    let newPlate = evo.plates.count;
    for (let k = 0; k < evo.plates.count; k++) if (k !== split && areas.length && (areas[k] ?? 0) < 0.004) { newPlate = k; break; }
    const live = areas.length ? areas.slice(0, evo.plates.count).filter(a => a >= 0.004).length : evo.plates.count;
    if (newPlate < MAX_PLATES && (live < 8 || (areas[split] ?? 0) > 0.2 || evo.random() < 0.5)) {
        const normal = randomDirection(evo.random);
        const center = evo.plates.centers[split] ?? randomDirection(evo.random);
        evo.plates.centers[split] = normalize3(add3(center, scale3(normal, -0.35)));
        evo.plates.centers[newPlate] = normalize3(add3(center, scale3(normal, 0.35)));
        partition(evo, u => {
            evo.gl.uniform1i(u.uMode, 1);
            evo.gl.uniform1i(u.uSplitPlate, split);
            evo.gl.uniform1i(u.uNewPlate, newPlate);
            evo.gl.uniform3fv(u.uSplitNormal, new Float32Array(normal));
            evo.gl.uniform1f(u.uWarp, 0.4);
            evo.gl.uniform1i(u.uSeedCount, 0);
        });
        if (newPlate === evo.plates.count) evo.plates.count++;
    }
    assignPlateMotions(evo);
    // Few, large plates are unstable (slab pull, supercontinent insulation):
    // rifting resumes sooner.
    evo.nextReorganizationMyr = timeMyr(evo) + (live < 7 ? 30 + evo.random() * 40 : 120 + evo.random() * 160);
}

// ------------------------------------------------------------------- step --

export function setEvolutionPlaying(evo, playing) { evo.playing = Boolean(playing); }
export function setEvolutionClouds(evo, visible) { evo.showClouds = Boolean(visible); }
export function setEvolutionSpeed(evo, yearsPerSecond) {
    if (Number.isFinite(yearsPerSecond) && yearsPerSecond > 0) evo.maximumYearsPerSecond = yearsPerSecond;
}

export function stepEvolution(evo, wallDtSeconds) {
    if (!evo.playing) return false;
    const wallDt = Math.min(Math.max(wallDtSeconds, 0), 0.1);
    if (wallDt <= 0) return false;
    if (timeMyr(evo) >= PRESENT_MYR) {
        // The present day: geology is frozen, the weather keeps moving.
        evo.rate = 0;
        evo.cloudPhase += wallDt * 0.8;
        return true;
    }
    evo.frame++;
    evo.wallTime += wallDt;
    updateMagmaImpacts(evo);
    const thermal = evo.thermal;
    let rate = physicalRate(thermal.timeSeconds, evo.maximumYearsPerSecond, 3.5);
    // Steam rain-out lasts ~1 Myr: play it slowly enough to watch.
    if (evo.lidOnsetSeconds !== null && evo.water < 0.98) rate = Math.min(rate, RAINOUT_PLAYBACK_MYR * MYR);
    evo.rate = rate;
    const regimeBefore = thermal.regime;
    advanceThermalHistory(thermal, rate * wallDt, raftSpacing(evo) * PARAMETERS.radiusMeters);

    if (regimeBefore === 'magma-ocean') {
        const magma = thermal.magma;
        // Display flow speed follows the physical slowdown (rate-limited).
        const slowdown = magma ? Math.max(0.12, Math.min(1, Math.sqrt(magma.velocity / 3.0))) : 0.12;
        evo.displaySpeed = 0.022 * slowdown;
        evo.foundered += advanceRafts(evo.rafts, wallDt, evo.displaySpeed, raftSpacing(evo));
        updateAgeTable(evo);
        uploadRafts(evo);
    }
    if (thermal.regime !== 'magma-ocean' && !evo.lithosphereReady) {
        initializeLithosphere(evo);
    }
    if (evo.lithosphereReady) {
        // The welded lid replaces the raft view over ~5 s of display time.
        evo.magmaWeight = Math.max(0, evo.magmaWeight - wallDt / 5.0);
        evo.lithosphereWeight = 1.0 - evo.magmaWeight;
        if (thermal.regime === 'plates' && regimeBefore !== 'plates') startPlateTectonics(evo);
        const glowWindow = Math.max(Math.min(0.6 * rate / MYR, 2.0), 1e-6);
        const earth = evo.earth;
        // Earth's own history takes over at plate onset (at the latest 3.0 Ga).
        if (earth?.ready && !earth.active && evo.water > 0.99
            && (thermal.regime === 'plates' || timeMyr(evo) >= earthLatestStartMyr())) {
            startEarthHistory(earth, timeMyr(evo), evo.textures.state0[evo.current], evo.textures.derived);
        }
        if (stepEarthHistory(earth, timeMyr(evo))) {
            deriveEarthHistory(earth, timeMyr(evo), glowWindow, evo.targets.derived);
            updateWater(evo, wallDt);
            updateExposure(evo, wallDt);
            return true;
        }
        const dtMyr = rate * wallDt / MYR;
        updatePlumes(evo, dtMyr);
        updateImpacts(evo, dtMyr);
        const now = timeMyr(evo);
        let bake = thermal.regime !== 'plates';
        if (thermal.regime === 'plates') {
            const plates = evo.plates;
            let maximumAngle = 0;
            for (let k = 0; k < plates.count; k++) {
                plates.angles[k] += plates.rates[k] * rate * wallDt / SECONDS_PER_YEAR;
                transposeInto(plates.back, k * 9, rotationMatrix(plates.poles[k], plates.angles[k]));
                maximumAngle = Math.max(maximumAngle, Math.abs(plates.angles[k]));
            }
            // Re-bake after ~3 texels of motion to bound resampling diffusion.
            if (maximumAngle * GRID_WIDTH / (2 * Math.PI) >= 3.0) bake = true;
        }
        compose(evo);
        if (bake) {
            processLithosphere(evo, Math.max(now - evo.lastBakeMyr, 0));
            evo.lastBakeMyr = now;
            if (thermal.regime === 'plates' && (evo.bakeCount = (evo.bakeCount ?? 0) + 1) % 3 === 0) evo.consolidated = consolidatePlates(evo);
            for (let k = 0; k < evo.plates.count; k++) {
                if (evo.plates.centers[k]) evo.plates.centers[k] = normalize3(applyRotation(rotationMatrix(evo.plates.poles[k], evo.plates.angles[k]), evo.plates.centers[k]));
                evo.plates.angles[k] = 0;
                identityBack(evo.plates.back, k);
            }
            compose(evo);
            if (thermal.regime === 'plates' && now >= evo.nextReorganizationMyr) {
                reorganizePlates(evo);
                compose(evo);
            }
        }
        derive(evo, glowWindow);
    }
    updateWater(evo, wallDt);
    updateExposure(evo, wallDt);
    return true;
}

// Hydrosphere: the magma ocean degasses a ~100-300 bar steam atmosphere; once
// the surface is solid it rains out within ~1 Myr (Abe 1993; Zahnle et al.
// 2010; Sleep 2010), filling the basins with Earth's ocean inventory.
function updateWater(evo, wallDt) {
    evo.cloudPhase += wallDt * 0.8;
    if (!evo.lithosphereReady) { evo.water = 0; evo.steam = 0; evo.seaLevel = evo.seaLevelTarget = -100; return; }
    const t = evo.thermal;
    if (evo.lidOnsetSeconds === null) evo.lidOnsetSeconds = t.timeSeconds;
    const sinceMyr = (t.timeSeconds - evo.lidOnsetSeconds) / MYR;
    const condensed = 1 - Math.exp(-sinceMyr / STEAM_RAINOUT_MYR);
    evo.water = condensed;
    // The deck closes as the last melt windows freeze (~60 kyr), before much
    // water has condensed: rain-out happens under the deck.
    const build = Math.min(1, sinceMyr / 0.06);
    evo.steam = Math.exp(-sinceMyr / STEAM_RAINOUT_MYR) * build * build * (3 - 2 * build);
    evo.life = Math.min(1, Math.max(0, (timeMyr(evo) - LAND_PLANTS_MYR) / 60));
    evo.climateK = climateAnomalyK(4567 - timeMyr(evo));
    evo.seaTimer -= wallDt;
    if (evo.seaTimer <= 0) {
        evo.seaTimer = 0.4;
        // Earth regime: reconstructed basins are idealised (age-depth only, no
        // lost continental crust), so the volume balance is replaced by the
        // palaeo-coastline datum of the PaleoDEMs (near-constant freeboard).
        evo.seaLevelTarget = evo.earth?.active
            ? seaLevelForVolume(evo, 1, 0)
            : seaLevelForVolume(evo, OCEAN_LAYER_KM * condensed);
        if (evo.seaLevel < -50) evo.seaLevel = evo.seaLevelTarget;
    }
    evo.seaLevel += (evo.seaLevelTarget - evo.seaLevel) * (1 - Math.exp(-wallDt / 0.4));
}

// Sea level that holds the given global-equivalent water layer, from the
// area-weighted hypsometry of the simulated elevation field.
function seaLevelForVolume(evo, layerKm, pinnedLevel = null) {
    if (layerKm <= 1e-4) return -100;
    const field = readEvolutionField(evo, 'derived');
    const BIN = 0.05, MIN = -15, COUNT = 600;
    const area = evo.hypsometry ?? (evo.hypsometry = new Float64Array(COUNT));
    area.fill(0);
    let total = 0, felsicArea = 0;
    const { width, height, data } = field;
    for (let y = 0; y < height; y += 2) {
        const w = Math.cos(((y + 0.5) / height - 0.5) * Math.PI);
        for (let x = 0; x < width; x += 2) {
            const e = data[(y * width + x) * 4];
            const b = Math.max(0, Math.min(COUNT - 1, Math.floor((e - MIN) / BIN)));
            area[b] += w; total += w;
            if (data[(y * width + x) * 4 + 2] > 0.5) felsicArea += w;
        }
    }
    // Volume below level s: integral of (s - e) over area with e < s.
    let below = 0, moment = 0;
    for (let b = 0; b < COUNT; b++) {
        const e = MIN + (b + 0.5) * BIN;
        const level = MIN + (b + 1) * BIN;
        below += area[b]; moment += area[b] * e;
        const volume = (level * below - moment) / total;
        if (pinnedLevel !== null ? level >= pinnedLevel : volume >= layerKm) {
            const prevLevel = level - BIN;
            const prevVolume = (prevLevel * (below - area[b]) - (moment - area[b] * e)) / total;
            const f = pinnedLevel !== null
                ? Math.min(1, Math.max(0, (pinnedLevel - prevLevel) / BIN))
                : (layerKm - prevVolume) / Math.max(volume - prevVolume, 1e-9);
            evo.landFraction = 1 - (below - area[b] * (1 - f)) / total;
            evo.felsicFraction = felsicArea / total;
            return prevLevel + f * BIN;
        }
    }
    return MIN + COUNT * BIN;
}

function radianceOfMelt(evo) { return emissionLinearRadiance(evo.thermal.potentialKelvin); }
function luminance(rgb) { return 0.2126 * rgb[0] + 0.7152 * rgb[1] + 0.0722 * rgb[2]; }

// Camera auto-exposure from the modelled mean scene radiance: a camera
// setting, it never changes the physical state.
function updateExposure(evo, wallDt) {
    const magma = evo.thermal.magma;
    let emitted = 0;
    if (magma && evo.magmaWeight > 0) {
        const lifetime = Math.max(magma.lifetimeSeconds, 1e-3);
        const reach = Math.min(lifetime, Math.max(evo.thermal.timeSeconds, 1e-3));
        const index = Math.max(0, Math.min(AGE_TABLE_SIZE - 1,
            Math.round((Math.log10(reach) - AGE_LOG_MIN) / (AGE_LOG_MAX - AGE_LOG_MIN) * (AGE_TABLE_SIZE - 1))));
        const d = evo.ageTableData;
        const meanRadiance = [d[index * 4], d[index * 4 + 1], d[index * 4 + 2]].map(v => v / reach);
        const widened = [d[index * 4], d[index * 4 + 1], d[index * 4 + 2]].map(v => SEAM_WIDENING * v / lifetime);
        emitted = Math.min(luminance(meanRadiance.map((v, i) => Math.max(v, widened[i]))), luminance(radianceOfMelt(evo))) * 0.9 * evo.magmaWeight;
    }
    const albedo = evo.magmaWeight > 0.5 ? 0.05 : 0.09;
    const reflected = albedo * SOLAR_REFLECTANCE_SCALE;
    // Sunlit rock is exposed to ~0.17 linear (mid-grey, like lunar basalt);
    // partial adaptation (square root of the emitted term) keeps genuine
    // dimming of the glowing seams visible instead of normalising it away.
    const target = Math.min(400, Math.max(1, 1 / (reflected / 0.17 + Math.sqrt(emitted * 0.01) / 0.25)));
    const blend = 1 - Math.exp(-wallDt / 0.6);
    evo.exposure += (target - evo.exposure) * blend;
}

// ----------------------------------------------------------------- render --

function invert4(m) {
    const o = new Float32Array(16);
    const [a00, a01, a02, a03, a10, a11, a12, a13, a20, a21, a22, a23, a30, a31, a32, a33] = m;
    const b00 = a00 * a11 - a01 * a10, b01 = a00 * a12 - a02 * a10, b02 = a00 * a13 - a03 * a10;
    const b03 = a01 * a12 - a02 * a11, b04 = a01 * a13 - a03 * a11, b05 = a02 * a13 - a03 * a12;
    const b06 = a20 * a31 - a21 * a30, b07 = a20 * a32 - a22 * a30, b08 = a20 * a33 - a23 * a30;
    const b09 = a21 * a32 - a22 * a31, b10 = a21 * a33 - a23 * a31, b11 = a22 * a33 - a23 * a32;
    const det = b00 * b11 - b01 * b10 + b02 * b09 + b03 * b08 - b04 * b07 + b05 * b06;
    if (!det) return o;
    const k = 1 / det;
    o[0] = (a11 * b11 - a12 * b10 + a13 * b09) * k; o[1] = (a02 * b10 - a01 * b11 - a03 * b09) * k;
    o[2] = (a31 * b05 - a32 * b04 + a33 * b03) * k; o[3] = (a22 * b04 - a21 * b05 - a23 * b03) * k;
    o[4] = (a12 * b08 - a10 * b11 - a13 * b07) * k; o[5] = (a00 * b11 - a02 * b08 + a03 * b07) * k;
    o[6] = (a32 * b02 - a30 * b05 - a33 * b01) * k; o[7] = (a20 * b05 - a22 * b02 + a23 * b01) * k;
    o[8] = (a10 * b10 - a11 * b08 + a13 * b06) * k; o[9] = (a01 * b08 - a00 * b10 - a03 * b06) * k;
    o[10] = (a30 * b04 - a31 * b02 + a33 * b00) * k; o[11] = (a21 * b02 - a20 * b04 - a23 * b00) * k;
    o[12] = (a11 * b07 - a10 * b09 - a12 * b06) * k; o[13] = (a00 * b09 - a01 * b07 + a02 * b06) * k;
    o[14] = (a31 * b01 - a30 * b03 - a32 * b00) * k; o[15] = (a20 * b03 - a21 * b01 + a22 * b00) * k;
    return o;
}

function renderStars(evo, frame) {
    const gl = evo.gl;
    const u = evo.uniforms.stars;
    gl.useProgram(evo.programs.stars);
    gl.uniformMatrix4fv(u.uInverseViewProjection, false, invert4(frame.viewProjection));
    gl.uniform3fv(u.uCamera, frame.camera);
    const viewport = gl.getParameter(gl.VIEWPORT);
    gl.uniform2f(u.uViewport, viewport[2], viewport[3]);
    gl.disable(gl.DEPTH_TEST);
    gl.depthMask(false);
    gl.bindVertexArray(evo.vao);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    gl.depthMask(true);
}

export function renderEvolutionGlobe(evo, frame) {
    const gl = evo.gl;
    renderStars(evo, frame);
    const camLength = Math.hypot(frame.camera[0], frame.camera[1], frame.camera[2]) || 1;
    evo.viewDirection = [frame.camera[0] / camLength, frame.camera[1] / camLength, frame.camera[2] / camLength];
    const u = evo.uniforms.globe;
    gl.useProgram(evo.programs.globe);
    gl.uniformMatrix4fv(u.uViewProjection, false, frame.viewProjection);
    gl.uniform3fv(u.uLight, frame.light);
    gl.uniform3fv(u.uCamera, frame.camera);
    gl.uniform1f(u.uExposure, evo.exposure);
    gl.uniform1f(u.uMagmaWeight, evo.magmaWeight);
    gl.uniform1f(u.uLithosphereWeight, evo.lithosphereWeight);
    gl.uniform1f(u.uRadiusMeters, PARAMETERS.radiusMeters);
    gl.uniform1f(u.uMeltKelvin, evo.thermal.potentialKelvin);
    const magma = evo.thermal.magma;
    gl.uniform1f(u.uRenewalVelocity, magma ? SEAM_WIDENING * raftSpacing(evo) * PARAMETERS.radiusMeters / Math.max(magma.lifetimeSeconds, 1e-3) : 0);
    gl.uniform1f(u.uDisplaySpeed, evo.displaySpeed ?? 0.02);
    gl.uniform1f(u.uSolar, SOLAR_REFLECTANCE_SCALE);
    gl.uniform1i(u.uRaftCount, evo.rafts.count);
    gl.uniform1i(u.uBinGrid, evo.rafts.binGrid);
    gl.uniform1f(u.uReliefScale, RELIEF_EXAGGERATION * 1000 / PARAMETERS.radiusMeters);
    gl.uniform1f(u.uDatumKm, evo.datumKm);
    gl.uniform1f(u.uVolcanicGlowKelvin, 1450.0);
    gl.uniform1f(u.uSeaLevel, evo.water > 0.001 ? evo.seaLevel : -100);
    // Earth's real lowlands sit ~0.1-0.3 km above sea level; the emergent
    // model's higher freeboard tolerates a coarser shoreline perturbation.
    gl.uniform1f(u.uShoreKm, evo.earth?.active ? 0.15 : 1.0);
    gl.uniform1f(u.uWater, evo.water);
    gl.uniform1f(u.uSteam, evo.steam);
    gl.uniform1f(u.uLife, evo.life);
    gl.uniform1f(u.uClimateK, evo.climateK ?? 0);
    // Great Oxidation Event ~2.45-2.2 Ga (Holland 2006; Lyons et al. 2014).
    const oxic = Math.min(Math.max((timeMyr(evo) - (PRESENT_MYR - 2450)) / 250, 0), 1);
    gl.uniform1f(u.uOxic, oxic * oxic * (3 - 2 * oxic));
    gl.uniform1f(u.uCloudPhase, evo.cloudPhase);
    gl.uniform1f(u.uClouds, evo.showClouds ? 1 : 0);
    gl.uniform1f(u.uSeamShare, evo.rafts.seamShare ?? 1);
    gl.uniform1f(u.uRaftSpacing, evo.rafts.spacing ?? 0.05);
    gl.uniform1f(u.uShell, 0);
    const impactPositions = new Float32Array(MAX_MAGMA_IMPACTS * 4), impactInfo = new Float32Array(MAX_MAGMA_IMPACTS * 4);
    evo.magmaImpacts.forEach((impact, i) => {
        impactPositions.set([...impact.direction, impact.radius], i * 4);
        impactInfo.set([evo.wallTime - impact.born, impact.seed, 0, 0], i * 4);
    });
    gl.uniform1i(u.uMagmaImpactCount, evo.magmaImpacts.length);
    gl.uniform4fv(u.uMagmaImpacts, impactPositions);
    gl.uniform4fv(u.uMagmaImpactInfo, impactInfo);
    bindTexture(gl, 5, evo.textures.raftPosition, u.uRaftPosition);
    bindTexture(gl, 6, evo.textures.raftVelocity, u.uRaftVelocity);
    bindTexture(gl, 7, evo.textures.bins, u.uBins);
    bindTexture(gl, 8, evo.textures.ageTable, u.uAgeTable);
    bindTexture(gl, 9, evo.textures.derived, u.uDerived);
    bindTexture(gl, 10, frame.thermalRadianceTexture, u.uThermalRadianceTexture);
    bindTexture(gl, 11, evo.textures.raftFrame, u.uRaftFrame);
    bindTexture(gl, 12, evo.textures.raftLink, u.uRaftLink);
    gl.uniform1f(u.uMaterialScale, evo.rafts.initialSpacing ?? 0.05);
    gl.bindVertexArray(evo.vao);
    gl.enableVertexAttribArray(evo.globePosition);
    gl.enable(gl.DEPTH_TEST);
    gl.enable(gl.CULL_FACE);
    for (const tile of frame.tiles) {
        gl.bindBuffer(gl.ARRAY_BUFFER, tile.positionBuffer);
        gl.vertexAttribPointer(evo.globePosition, 3, gl.FLOAT, false, 0, 0);
        gl.drawArrays(gl.TRIANGLES, 0, tile.surfaceVertexCount);
    }
    // Atmosphere: additive in-scatter through a thin shell (limb glow).
    gl.uniform1f(u.uShell, ATMOSPHERE_SHELL);
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.ONE, gl.ONE);
    gl.depthMask(false);
    for (const tile of frame.tiles) {
        gl.bindBuffer(gl.ARRAY_BUFFER, tile.positionBuffer);
        gl.vertexAttribPointer(evo.globePosition, 3, gl.FLOAT, false, 0, 0);
        gl.drawArrays(gl.TRIANGLES, 0, tile.surfaceVertexCount);
    }
    gl.depthMask(true);
    gl.disable(gl.BLEND);
    gl.disableVertexAttribArray(evo.globePosition);
    gl.bindVertexArray(null);
    gl.activeTexture(gl.TEXTURE0);
}

// ----------------------------------------------------------------- status --

// Diagnostics: read back a lithosphere field as a flat equirectangular map.
export function readEvolutionField(evo, field = 'state0') {
    const gl = evo.gl;
    const target = field === 'derived' ? evo.targets.derived
        : field === 'compose1' || field === 'compose0' ? evo.targets.compose : evo.targets.state[evo.current];
    const attachment = field.endsWith('1') ? 1 : 0;
    const data = new Float32Array(GRID_WIDTH * GRID_HEIGHT * 4);
    gl.bindFramebuffer(gl.FRAMEBUFFER, target.framebuffer);
    gl.readBuffer(gl.COLOR_ATTACHMENT0 + attachment);
    gl.readPixels(0, 0, GRID_WIDTH, GRID_HEIGHT, gl.RGBA, gl.FLOAT, data);
    gl.readBuffer(gl.COLOR_ATTACHMENT0);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    return { width: GRID_WIDTH, height: GRID_HEIGHT, data };
}

function livePlates(evo) {
    return evo.plateAreas ? evo.plateAreas.slice(0, evo.plates.count).filter(a => a >= 0.004).length : evo.plates.count;
}

export function evolutionStatus(evo) {
    const t = evo.thermal;
    const phi = crystalFraction(t.potentialKelvin);
    let phase;
    if (evo.earth?.active && t.regime !== 'magma-ocean' && evo.steam <= 0.2) {
        const earth = earthHistoryStatus(evo.earth, timeMyr(evo));
        phase = earth.ma <= 0 ? `Present day · Earth · ${earth.plates} plates`
            : `${earth.era} · ${Math.round(earth.ma)} Ma · ${earth.synthetic ? 'synthetic drift of real cratons' : 'reconstructed plates'}`;
    } else if (t.regime === 'magma-ocean') {
        const glow = t.magma?.glowingFraction ?? 1;
        if (glow > 0.5) phase = 'Magma ocean · incandescent surface';
        else if (phi < 0.2) phase = 'Magma ocean · quench-crust rafts founder';
        else phase = 'Magma ocean · rafts weld, rifts still glow';
    } else if (t.regime === 'stagnant-lid') {
        phase = evo.steam > 0.2 ? 'Steam atmosphere rains out · first oceans'
            : t.heatPipeShare > 0.5 ? 'Water world · heat-pipe volcanism' : 'Water world · waning volcanism';
    } else {
        phase = timeMyr(evo) >= PRESENT_MYR ? `Present day · ${livePlates(evo)} plates`
            : `${evo.life > 0.05 ? 'Land plants · ' : ''}Plate tectonics · ${livePlates(evo)} plates`;
    }
    return {
        timeSeconds: t.timeSeconds,
        timeLabel: formatDuration(t.timeSeconds),
        rateLabel: `${formatDuration(evo.rate ?? 1)} / s`,
        regime: t.regime,
        phase,
        potentialKelvin: t.potentialKelvin,
        crystalFraction: phi,
        heatFluxWattsPerSquareMeter: t.heatFlux,
        crustLifetimeLabel: t.magma ? formatDuration(t.magma.lifetimeSeconds) : null,
        glowingFraction: t.magma?.glowingFraction ?? 0,
        convectionVelocity: t.magma?.velocity ?? 0,
        rafts: t.regime === 'magma-ocean' ? evo.rafts.count : 0,
        plates: evo.earth?.active ? earthHistoryStatus(evo.earth, timeMyr(evo)).plates
            : t.regime === 'plates' ? livePlates(evo) : (t.regime === 'stagnant-lid' ? 1 : 0),
        earthHistory: evo.earth?.active ? earthHistoryStatus(evo.earth, timeMyr(evo)) : null,
        plumes: evo.plumes.length,
        impacts: evo.impactCount,
        landFraction: evo.landFraction ?? 0,
        felsicFraction: evo.felsicFraction ?? 0,
        heatPipeShare: t.heatPipeShare,
        lidOnsetLabel: t.lidOnsetSeconds ? formatDuration(t.lidOnsetSeconds) : null,
        plateOnsetLabel: t.plateOnsetSeconds ? formatDuration(t.plateOnsetSeconds) : null,
        rheologicalTransitionKelvin: rheologicalTransitionKelvin(),
        exposure: evo.exposure,
        playing: evo.playing,
        liberties: evo.earth?.active ? [...DEMO_LIBERTIES, ...EARTH_LIBERTIES] : DEMO_LIBERTIES,
    };
}
