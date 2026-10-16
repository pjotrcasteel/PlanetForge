// Earth history regime (milestone E1/E2): from 1.8 Ga the plates follow a
// published full-plate reconstruction (Cao et al. 2024, which embeds Merdith
// et al. 2021), baked offline by tools/earth-bake/bake.py. The GPU still does
// the physics on top of the kinematics:
//  - ocean floor age is advected with the plates; crust is created where a
//    plate pulls away from its neighbour (ridges), so depth follows
//    Parsons & Sclater (1977) subsidence;
//  - continental relief is stored in present-day coordinates (Lagrangian per
//    continental block) and grows where the reconstruction makes blocks
//    converge (collisional orogens, Andean-type arcs) and thins at rifts;
//  - in the last 60 Myr the relief is assimilated toward ETOPO 2022 so the
//    run ends on today's Earth.

export const EARTH_START_MA = 1800;
// Before the reconstruction (milestone E4) the Earth regime starts at plate
// onset (at the latest 3.0 Ga): the 24 real 1.8 Ga continental blocks drift
// as rigid plates on synthetic smooth rotations that converge on the Nuna
// configuration at 1.8 Ga.
export const EARTH_LATEST_START_MA = 3000;
const PRE_CYCLE_MYR = 800;
// Subaerial emergence of the cratons (Ma): the oldest nuclei (Pilbara,
// Kaapvaal, Dharwar) first, most continental crust only at ~2.5 Ga
// (Flament et al. 2008; Bindeman et al. 2018). Keyed by plate ID.
const CRATON_EMERGENCE_MA = {
    801: 3250, 701: 3200, 501: 2950, 101: 2750, 102: 2700, 302: 2750, 401: 2650, 714: 2650, 715: 2600,
    201: 2550, 601: 2500, 80101: 2600, 80103: 2500, 8011: 2450, 602: 2300,
};
const DEFAULT_EMERGENCE_MA = 2250;
const PRESENT_MYR = 4567;
const ASSIMILATION_MA = 30;
const HANDOFF_MYR = 40;
const PASS_MYR = 2.0;

export const EARTH_LIBERTIES = Object.freeze([
    'Before 1.8 Ga (no published full-plate model) the 24 continental blocks of the 1.8 Ga reconstruction drift as rigid plates on synthetic smooth rotations (800 Myr dispersal-assembly cycles) that converge on Nuna at 1.8 Ga; the older supercratons (Vaalbara, Kenorland/Superia) are not reconstructed and the ocean is one plate.',
    'Craton emergence ages are approximate (oldest nuclei ~3.25 Ga, most crust ~2.7-2.25 Ga); before emergence a block is a submerged plateau, and its present outline is used for the Archean craton.',
    'From 1.8 Ga plate motions follow the published reconstruction of Cao et al. (2024) and Merdith et al. (2021); the emergent planet hands over by cross-fading over ~40 Myr.',
    'Before 540 Ma continental relief is a compressed copy of the 540 Ma PaleoDEM (Precambrian relief is unknown); mountain belts grow from modelled plate convergence and relax over ~150 Myr.',
    'From 540 Ma continental relief is nudged (~5 Myr) toward the PALEOMAP PaleoDEMs (Scotese & Wright 2018), mapped onto the crust with the PALEOMAP plate model; shallow seas and orogens follow the palaeogeography rather than the plate physics.',
    'In the final 30 Myr the surface is nudged toward the present-day relief (NOAA ETOPO 2022) so the run ends on today\'s Earth.',
    'Sea level is held at the palaeo-coastline datum of the PaleoDEMs (constant freeboard) instead of an ocean-volume balance over the idealised reconstructed basins.',
    'Before 540 Ma reconstructed continental blocks are kept at least 120 m above sea level (their relief is unknown), and gaps between blocks narrower than ~550 km before 540 Ma (~230 km after) are treated as polygon artefacts and filled with the flanking continental relief.',
]);

const ERAS = [
    [2500, 'Archean · cratons rise from the sea'],
    [2100, 'Paleoproterozoic · Great Oxidation'],
    [1850, 'Nuna assembles'],
    [1400, 'Nuna (Columbia) supercontinent'],
    [1100, 'Nuna breaks up'],
    [750, 'Rodinia supercontinent'],
    [600, 'Rodinia breaks up'],
    [335, 'Gondwana & Laurussia'],
    [175, 'Pangaea'],
    [66, 'Pangaea breaks up · Atlantic opens'],
    [-1, 'Modern continents'],
];

export function earthEra(ma) {
    for (const [floor, label] of ERAS) if (ma > floor) return label;
    return ERAS[ERAS.length - 1][1];
}

export function earthAgeMa(timeMyr) { return Math.max(PRESENT_MYR - timeMyr, 0); }
export function earthStartMyr() { return PRESENT_MYR - EARTH_START_MA; }
export function earthLatestStartMyr() { return PRESENT_MYR - EARTH_LATEST_START_MA; }

// ------------------------------------------------------------- shaders --

const plateCommon = `
uniform highp sampler2D uPlates;   // rows: 0 q(t), 1 q(frame age), 2 q(previous pass), 3 omega (rad/Myr), w = emergence Ma
uniform highp usampler2D uFrame;   // r = topology plate + 1, g = continental block + 1
uniform highp usampler2D uLive;    // pre-1.8 Ga: rasterised rigid blocks at the current time
uniform int uPre;
vec4 plateQ(int i, int row) { return texelFetch(uPlates, ivec2(i, row), 0); }
vec3 qrot(vec4 q, vec3 v) { return v + 2.0 * cross(q.xyz, cross(q.xyz, v) + q.w * v); }
vec3 qinv(vec4 q, vec3 v) { return qrot(vec4(-q.xyz, q.w), v); }
uvec2 frameAt(vec3 d) { return texelFetch(uFrame, texelOf(d, textureSize(uFrame, 0)), 0).rg; }
uvec2 liveAt(vec3 d) { return texelFetch(uLive, texelOf(d, textureSize(uLive, 0)), 0).rg; }
// The baked raster is at the nearest 5 Myr frame; move the lookup with the
// candidate plate so boundaries glide instead of jumping between frames.
vec3 toFrame(int i, vec3 p) { return qrot(plateQ(i, 1), qinv(plateQ(i, 0), p)); }
// Reconstruction polygons have straight, smoothed edges: perturb the block
// outline by up to ~100 km with noise fixed to the block (present-day
// coordinates), so coasts are irregular and still move rigidly.
vec3 coastWarp(int i, vec3 p) {
    vec3 q = qinv(plateQ(i, 0), p) * 12.0;
    vec3 w = vec3(noise3(q + 1.7), noise3(q + 8.3), noise3(q * 2.3 + 4.1)) - 0.5;
    return normalize(p + qrot(plateQ(i, 0), w) * 0.03);
}
int topologyAt(vec3 p) {
    if (uPre == 1) return int(liveAt(p).r) - 1;
    uint k0 = frameAt(p).r;
    if (k0 == 0u) return 0;
    int i = int(k0) - 1;
    uint k1 = frameAt(toFrame(i, p)).r;
    if (k1 == k0 || k1 == 0u) return i;
    int j = int(k1) - 1;
    return frameAt(toFrame(j, p)).r == k1 ? j : i;
}
int continentAt(vec3 p, ivec2 me) {
    if (uPre == 1) return int(liveAt(p).g) - 1;
    ivec2 size = textureSize(uFrame, 0);
    for (int n = 0; n < 5; n++) {
        ivec2 o = n == 0 ? ivec2(0) : (n == 1 ? ivec2(5, 0) : (n == 2 ? ivec2(-5, 0) : (n == 3 ? ivec2(0, 5) : ivec2(0, -5))));
        uint c = texelFetch(uFrame, wrapTexel(me + o, size), 0).g;
        if (c == 0u) continue;
        int i = int(c) - 1;
        if (frameAt(toFrame(i, coastWarp(i, p))).g == c) return i;
    }
    return -1;
}
`;

// Pass 1 (simulation grid, reconstructed frame): plate membership, ocean
// floor age and boundary processes.
// out0 = (topology plate, crust formation time, continental block or -1, uplift km/Myr)
// out1 = (trench, arc, last eruption, 0)
const tectonicsFragment = (common) => `#version 300 es
precision highp float;
precision highp int;
precision highp usampler2D;
uniform highp sampler2D uPrev0;
uniform highp sampler2D uPrev1;
uniform highp sampler2D uEmergent;
uniform int uInit;
uniform float uTimeMyr;
uniform float uDtMyr;
uniform float uRadiusKm;
layout(location = 0) out vec4 out0;
layout(location = 1) out vec4 out1;
${common}
${plateCommon}
void main() {
    ivec2 size = textureSize(uPrev0, 0);
    ivec2 me = ivec2(gl_FragCoord.xy);
    vec2 uv = (vec2(me) + 0.5) / vec2(size);
    vec3 p = dirFromUv(uv);
    float coslat = max(cos((uv.y - 0.5) * PI), 0.05);
    int k = topologyAt(p);
    int c = continentAt(p, me);
    float formation;
    vec4 prev1 = texelFetch(uPrev1, me, 0);
    if (uInit == 1) {
        formation = texelFetch(uEmergent, me, 0).g;
        prev1 = vec4(0.0, 0.0, -1e6, 0.0);
    } else {
        // Semi-Lagrangian advection with the plate. Landing on another plate
        // that borders this one means the texel was accreted at a ridge;
        // a whole-plate relabel (reorganisation) keeps the old crust.
        vec3 pb = qrot(plateQ(k, 2), qinv(plateQ(k, 0), p));
        ivec2 tb = texelOf(pb, size);
        vec4 b = texelFetch(uPrev0, tb, 0);
        formation = b.g;
        // Bilinear from same-plate texels: nearest sampling would never move
        // the field for sub-texel displacements.
        vec2 x = uvFromDir(pb) * vec2(size) - 0.5;
        ivec2 base = ivec2(floor(x));
        vec2 f = x - vec2(base);
        float sum = 0.0, weight = 0.0;
        for (int j = 0; j < 4; j++) {
            ivec2 o = ivec2(j & 1, j >> 1);
            vec4 n = texelFetch(uPrev0, wrapTexel(base + o, size), 0);
            if (abs(n.r - float(k)) > 0.5) continue;
            float w = (o.x == 1 ? f.x : 1.0 - f.x) * (o.y == 1 ? f.y : 1.0 - f.y) + 1e-5;
            sum += w * n.g; weight += w;
        }
        if (weight >= 0.5) {
            formation = sum / weight;
        } else {
            bool borders = false;
            for (int a = 0; a < 8 && !borders; a++) {
                vec2 dir = vec2(cos(float(a) * PI * 0.25) / coslat, sin(float(a) * PI * 0.25));
                for (int r = 2; r <= 6; r += 2) {
                    if (abs(texelFetch(uPrev0, wrapTexel(tb + ivec2(round(dir * float(r))), size), 0).r - float(k)) < 0.5) { borders = true; break; }
                }
            }
            if (borders) formation = uTimeMyr - 0.5 * uDtMyr;
        }
    }
    float uplift = 0.0, trench = 0.0, arc = 0.0, rift = 0.0;
    if (uInit == 0) {
        bool cont = c >= 0;
        float age = uTimeMyr - formation;
        vec3 wk = plateQ(k, 3).xyz;
        for (int a = 0; a < 8; a++) {
            vec2 dir = vec2(cos(float(a) * PI * 0.25) / coslat, sin(float(a) * PI * 0.25));
            for (int s = 0; s < 4; s++) {
                float r = s == 0 ? 2.0 : (s == 1 ? 4.0 : (s == 2 ? 7.0 : 11.0));
                ivec2 t = wrapTexel(me + ivec2(round(dir * r)), size);
                vec4 n = texelFetch(uPrev0, t, 0);
                int j = int(n.r + 0.5);
                if (j == k) continue;
                // First texel across the boundary in this direction.
                vec3 pn = dirFromUv((vec2(t) + 0.5) / vec2(size));
                vec3 toward = normalize(pn - p * dot(pn, p) + 1e-9);
                vec3 v = cross(plateQ(j, 3).xyz - wk, p) * uRadiusKm;  // km/Myr, neighbour relative to me
                float closing = -dot(v, toward);
                // Reconstruction glitches (e.g. near the 1.8 Ga model start)
                // can imply absurd speeds; real convergence stays below
                // ~15 cm/yr (150 km/Myr).
                float rate = min(closing, 150.0);
                bool ncont = n.b > -0.5;
                // A topology boundary cutting through one rigid continental
                // block is a model artefact: no collision or rift within it.
                bool sameBlock = cont && abs(n.b - float(c)) < 0.5;
                float near = s == 0 ? 1.0 : (s == 1 ? 0.9 : (s == 2 ? 0.6 : 0.3));
                if (closing > 10.0) {
                    if (cont && ncont) {
                        // Continental collision: crust stacks into an orogen
                        // a few hundred km wide.
                        if (s <= 2 && !sameBlock) uplift = max(uplift, 0.003 * rate * near);
                    } else if (cont || (!ncont && k < j)) {
                        // Overriding plate above a subducting oceanic slab:
                        // arc magmatism ~100-300 km behind the trench.
                        // Ocean-ocean polarity is fixed per plate pair so a
                        // trench does not flip sides along strike.
                        float belt = s == 0 ? 0.4 : (s == 3 ? 0.5 : 1.0);
                        arc = max(arc, belt);
                        uplift = max(uplift, 0.0012 * rate * belt);
                    } else if (s <= 1) {
                        trench = max(trench, s == 0 ? 1.0 : 0.5);
                    }
                } else if (cont && ncont && closing < 0.0 && !sameBlock) {
                    rift = max(rift, min(-closing, 150.0) * near);
                }
                break;
            }
        }
        if (!cont) uplift = 0.0;
        uplift -= 0.0015 * rift;
    }
    float lastEruption = prev1.b;
    // Arc volcanoes erupt every ~10-100 kyr; only a sparse subset glows.
    if (arc > 0.0 && hash12(vec2(me) * 1.37 + uTimeMyr) < 0.03 * arc) lastEruption = uTimeMyr;
    out0 = vec4(float(k), formation, float(c), uplift);
    out1 = vec4(trench, arc, lastEruption, 0.0);
}`;

// Pass 2 (present-day frame, 2048x1024): continental relief per block.
const reliefFragment = (common) => `#version 300 es
precision highp float;
precision highp int;
precision highp usampler2D;
uniform highp sampler2D uRelief;
uniform highp sampler2D uPresent;
uniform highp usampler2D uPresentFrame;
uniform highp sampler2D uTectonics;
uniform highp sampler2D uPaleoA;
uniform highp sampler2D uPaleoB;
uniform highp sampler2D uPaleoBase;
uniform float uPaleoMix;
uniform float uPaleoWeight;
uniform int uHasPaleo;
uniform int uInit;
uniform float uDtMyr;
layout(location = 0) out vec4 outRelief;
${common}
${plateCommon}
float baseline(float e) {
    return e < 0.0 ? max(e, -2.0) : 0.12 + min(0.35 * e, 1.0);
}
void main() {
    ivec2 size = textureSize(uRelief, 0);
    ivec2 me = ivec2(gl_FragCoord.xy);
    vec2 uv = (vec2(me) + 0.5) / vec2(size);
    vec3 q = dirFromUv(uv);
    // Precambrian baseline: the oldest PaleoDEM (540 Ma) compressed, so no
    // younger orogen (Himalaya, Andes) is carried back in time.
    float e0 = uHasPaleo == 1 ? texture(uPaleoBase, uv).r : -9.0;
    float b = e0 > -1.0 ? (e0 < 0.0 ? max(0.5 * e0, -0.5) : 0.12 + min(0.35 * e0, 1.0))
                        : baseline(texelFetch(uPresent, me, 0).r);
    if (uInit == 1) { outRelief = vec4(b, 0.0, 0.0, 0.0); return; }
    float relief = texelFetch(uRelief, me, 0).r;
    uint c = texelFetch(uPresentFrame, texelOf(q, textureSize(uPresentFrame, 0)), 0).g;
    if (c > 0u) {
        int i = int(c) - 1;
        vec4 t = texelFetch(uTectonics, texelOf(qrot(plateQ(i, 0), q), textureSize(uTectonics, 0)), 0);
        if (abs(t.b - float(i)) < 0.5) {
            // Thickening saturates: ~4.5 km excess is a Tibet-like plateau.
            float room = clamp(1.0 - (relief - b) / 4.5, 0.0, 1.0);
            relief += t.a * uDtMyr * (1.0 - uPaleoWeight) * (t.a > 0.0 ? room : 1.0);
        }
    }
    // Orogens decay by erosion and gravitational collapse (~150 Myr).
    relief = b + clamp((relief - b) * exp(-uDtMyr / 150.0), -1.5, 4.5);
    // Phanerozoic: nudge toward the PaleoDEM of this crust (tau 5 Myr).
    if (uPaleoWeight > 0.0) {
        float dem = mix(texture(uPaleoA, uv).r, texture(uPaleoB, uv).r, uPaleoMix);
        relief = mix(relief, dem, uPaleoWeight * (1.0 - exp(-uDtMyr / 5.0)));
    }
    outRelief = vec4(relief, 0.0, 0.0, 0.0);
}`;

// Pass 3: isostatic elevation and display fields for the globe.
const derivedFragment = (common) => `#version 300 es
precision highp float;
precision highp int;
precision highp usampler2D;
uniform highp sampler2D uTectonics0;
uniform highp sampler2D uTectonics1;
uniform highp sampler2D uRelief;
uniform highp sampler2D uPresent;
uniform highp sampler2D uHandoff;
uniform float uTimeMyr;
uniform float uGlowWindowMyr;
uniform float uHandoffWeight;
uniform float uAssimilation;
uniform int uBridge;               // gap-bridging half-width, texels
uniform float uMa;
layout(location = 0) out vec4 outDerived;
${common}
${plateCommon}
float reliefAt(vec3 q) {
    ivec2 size = textureSize(uRelief, 0);
    vec2 x = uvFromDir(q) * vec2(size) - 0.5;
    ivec2 b = ivec2(floor(x));
    vec2 f = x - vec2(b);
    float r00 = texelFetch(uRelief, wrapTexel(b, size), 0).r;
    float r10 = texelFetch(uRelief, wrapTexel(b + ivec2(1, 0), size), 0).r;
    float r01 = texelFetch(uRelief, wrapTexel(b + ivec2(0, 1), size), 0).r;
    float r11 = texelFetch(uRelief, wrapTexel(b + ivec2(1, 1), size), 0).r;
    return mix(mix(r00, r10, f.x), mix(r01, r11, f.x), f.y);
}
// Relief of continental block b at p (present-day coordinates).
float blockLand(int b, vec3 p) {
    vec3 q = qinv(plateQ(b, 0), p);
    float land = reliefAt(q);
    // Before 540 Ma block relief is unknown; blocks whose present-day
    // footprint is ocean would otherwise flood as deep strips.
    if (uBridge > 3) land = max(land, 0.12);
    // Pre-Nuna cratons were low-lying (hot, thin lithosphere; Flament et al.
    // 2008); present-day relief, e.g. the Antarctic ice surface, is compressed.
    if (uPre == 1 && land > 0.0) land = 1.0 * land / (land + 1.0);
    // Archean: a craton is a submerged plateau until it emerges; its
    // oldest nuclei surface first as islands that grow and coalesce.
    float em = plateQ(b, 3).w;
    if (em > 0.0 && uMa > em - 150.0) {
        float rise = clamp((em + 150.0 - uMa) / 300.0, 0.0, 1.0);
        float nuclei = noise3(q * 5.0 + 3.1) * 0.65 + noise3(q * 14.0 + 7.3) * 0.35;
        float emerged = smoothstep(0.0, 0.06, rise * 1.3 - nuclei);
        land = mix(-0.45 - 0.5 * nuclei, land, emerged);
    }
    return land;
}
void main() {
    ivec2 size = textureSize(uTectonics0, 0);
    ivec2 me = ivec2(gl_FragCoord.xy);
    vec3 p = dirFromUv((vec2(me) + 0.5) / vec2(size));
    vec4 t0 = texelFetch(uTectonics0, me, 0);
    vec4 t1 = texelFetch(uTectonics1, me, 0);
    int k = int(t0.r + 0.5);
    int c = int(t0.b + 0.5);
    float age = clamp(uTimeMyr - t0.g, 0.0, 90.0);
    float trench = 0.0;
    for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) {
        float r = texelFetch(uTectonics1, wrapTexel(me + ivec2(dx, dy), size), 0).r;
        trench = max(trench, r * (1.0 - length(vec2(dx, dy)) / 3.2));
    }
    float elevation = -2.6 - 0.35 * sqrt(age) - 2.5 * trench;
    // Continental margins: coverage over a 3x3 tent gives a ~80 km shelf
    // and slope instead of a texel staircase. Membership is evaluated at the
    // current time (not the last tectonics pass) so coasts glide smoothly.
    float cover = 0.0;
    int cn = -1;
    for (int j = 0; j < 9; j++) {
        ivec2 o = ivec2(j % 3 - 1, j / 3 - 1);
        ivec2 t = wrapTexel(me + o, size);
        int b = continentAt(dirFromUv((vec2(t) + 0.5) / vec2(size)), t);
        if (b < 0) continue;
        cover += (o.x == 0 ? 2.0 : 1.0) * (o.y == 0 ? 2.0 : 1.0) / 16.0;
        if (cn < 0 || j == 4) cn = b;
    }
    if (cn >= 0) {
        float land = blockLand(cn, p);
        elevation = mix(elevation, land, smoothstep(0.0, 1.0, cover));
    }
    // Reconstruction polygons are coarse: slivers of 'ocean' between two
    // continental blocks narrower than 2*uBridge texels are polygon gaps.
    if (cover < 0.99) {
        float bridge = 0.0, bridged = 0.0;
        for (int a = 0; a < 4; a++) {
            ivec2 dir = a == 0 ? ivec2(1, 0) : (a == 1 ? ivec2(0, 1) : (a == 2 ? ivec2(1, 1) : ivec2(1, -1)));
            int r = a < 2 ? uBridge : (uBridge * 2 + 1) / 3;
            ivec2 ta = wrapTexel(me + dir * r, size), tb = wrapTexel(me - dir * r, size);
            vec3 pa = dirFromUv((vec2(ta) + 0.5) / vec2(size)), pb = dirFromUv((vec2(tb) + 0.5) / vec2(size));
            int ba = continentAt(pa, ta), bb = continentAt(pb, tb);
            // Before 1.8 Ga gaps between drifting blocks are real oceans.
            if (ba < 0 || bb < 0 || (uPre == 1 && ba != bb)) continue;
            float ea = blockLand(ba, pa), eb = blockLand(bb, pb);
            if (ea < 0.0 || eb < 0.0) continue;
            bridge += 0.5 * (ea + eb);
            bridged += 1.0;
        }
        if (bridged >= 2.0) {
            elevation = mix(elevation, bridge / bridged, 1.0 - cover);
            cover = 1.0;
        }
    }
    if (uAssimilation > 0.0) {
        vec3 q = qinv(plateQ(cn >= 0 ? cn : k, 0), p);
        elevation = mix(elevation, texture(uPresent, uvFromDir(q)).r, uAssimilation);
    }
    float glow = exp(-max(uTimeMyr - t1.b, 0.0) / max(uGlowWindowMyr, 1e-9));
    vec4 earth = vec4(elevation, glow, cover, 0.3 * t1.g);
    outDerived = mix(texelFetch(uHandoff, me, 0), earth, uHandoffWeight);
}`;

const copyFragment = `#version 300 es
precision highp float;
uniform highp sampler2D uSource;
layout(location = 0) out vec4 outColor;
void main() { outColor = texelFetch(uSource, ivec2(gl_FragCoord.xy), 0); }`;

// Pre-1.8 Ga: rasterise the rigid blocks of the 1.8 Ga frame at their
// current (synthetic) rotation; everything else is the single ocean plate.
const MAX_PRE_BLOCKS = 32;
const rasterFragment = (common) => `#version 300 es
precision highp float;
precision highp int;
precision highp usampler2D;
uniform int uPreCount;
uniform int uPreBlocks[${MAX_PRE_BLOCKS}];
uniform int uPreOcean;
layout(location = 0) out uvec4 outLive;
${common}
${plateCommon}
void main() {
    ivec2 size = textureSize(uFrame, 0);
    vec3 p = dirFromUv((gl_FragCoord.xy) / vec2(size));
    for (int n = 0; n < ${MAX_PRE_BLOCKS}; n++) {
        if (n >= uPreCount) break;
        int i = uPreBlocks[n];
        if (frameAt(toFrame(i, coastWarp(i, p))).g == uint(i + 1)) { outLive = uvec4(uint(i + 1), uint(i + 1), 0u, 1u); return; }
    }
    outLive = uvec4(uint(uPreOcean + 1), 0u, 0u, 1u);
}`;

// --------------------------------------------------------------- assets --

async function gunzip(buffer) {
    const stream = new Blob([buffer]).stream().pipeThrough(new DecompressionStream('gzip'));
    return new Response(stream).arrayBuffer();
}

async function fetchBuffer(url) {
    const response = await fetch(url);
    if (!response.ok) throw new Error(`${url}: ${response.status}`);
    return response.arrayBuffer();
}

export async function loadEarthAssets(baseUrl = 'data/earth/') {
    const manifest = await (await fetch(`${baseUrl}manifest.json`)).json();
    const [frames, rotations, present, paleo] = await Promise.all([
        fetchBuffer(`${baseUrl}frames.bin`),
        fetchBuffer(`${baseUrl}rotations.bin`).then(gunzip),
        fetchBuffer(`${baseUrl}present.bin`).then(gunzip),
        loadPaleo(baseUrl),
    ]);
    return { manifest, frames, rotations: new Float32Array(rotations), present: new Int16Array(present), paleo };
}

// PALEOMAP PaleoDEMs, present-referenced (optional asset).
async function loadPaleo(baseUrl) {
    try {
        const response = await fetch(`${baseUrl}paleo.json`);
        if (!response.ok) return null;
        const info = await response.json();
        const data = new Int16Array(await gunzip(await fetchBuffer(`${baseUrl}paleo.bin`)));
        return { ...info, data };
    } catch (error) {
        console.warn('PaleoDEMs unavailable:', error);
        return null;
    }
}

async function decodeFrame(assets, index) {
    const [offset, length] = assets.manifest.frames[index];
    const planar = new Uint16Array(await gunzip(assets.frames.slice(offset, offset + length)));
    const n = assets.manifest.width * assets.manifest.height;
    const interleaved = new Uint16Array(n * 2);
    const plates = new Set();
    for (let i = 0; i < n; i++) {
        interleaved[i * 2] = planar[i];
        interleaved[i * 2 + 1] = planar[n + i];
        plates.add(planar[i]);
    }
    return { data: interleaved, plates: plates.size };
}

// -------------------------------------------------------------- GPU side --

function texture(gl, width, height, internalFormat, format, type, data = null, filter = gl.NEAREST) {
    const t = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, t);
    gl.texImage2D(gl.TEXTURE_2D, 0, internalFormat, width, height, 0, format, type, data);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, filter);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, filter);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    return t;
}

function target(gl, textures) {
    const framebuffer = gl.createFramebuffer();
    gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer);
    textures.forEach((t, i) => gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0 + i, gl.TEXTURE_2D, t, 0));
    const status = gl.checkFramebufferStatus(gl.FRAMEBUFFER);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    if (status !== gl.FRAMEBUFFER_COMPLETE) throw new Error(`Earth framebuffer incomplete (${status}).`);
    return { framebuffer, count: textures.length };
}

function locations(gl, program, names) {
    const map = {};
    for (const name of names) map[name] = gl.getUniformLocation(program, name);
    return map;
}

// Creates the GPU resources once the assets have loaded.
export function createEarthHistory(gl, assets, createProgram, fullscreenVertex, sphereCommon, gridWidth, gridHeight) {
    const m = assets.manifest;
    const plateCount = m.plateIds.length;
    const programs = {
        tectonics: createProgram(gl, fullscreenVertex, tectonicsFragment(sphereCommon)),
        relief: createProgram(gl, fullscreenVertex, reliefFragment(sphereCommon)),
        derived: createProgram(gl, fullscreenVertex, derivedFragment(sphereCommon)),
        copy: createProgram(gl, fullscreenVertex, copyFragment),
        raster: createProgram(gl, fullscreenVertex, rasterFragment(sphereCommon)),
    };
    const presentKm = new Float32Array(assets.present.length);
    let oceanVolume = 0, totalArea = 0;
    for (let y = 0; y < m.presentHeight; y++) {
        const w = Math.cos(((y + 0.5) / m.presentHeight - 0.5) * Math.PI);
        for (let x = 0; x < m.presentWidth; x++) {
            const i = y * m.presentWidth + x;
            presentKm[i] = assets.present[i] / 1000;
            oceanVolume += w * Math.max(-presentKm[i], 0); totalArea += w;
        }
    }
    const tex = {
        plates: texture(gl, plateCount, 4, gl.RGBA32F, gl.RGBA, gl.FLOAT),
        frame: texture(gl, m.width, m.height, gl.RG16UI, gl.RG_INTEGER, gl.UNSIGNED_SHORT),
        presentFrame: texture(gl, m.width, m.height, gl.RG16UI, gl.RG_INTEGER, gl.UNSIGNED_SHORT),
        live: texture(gl, m.width, m.height, gl.RGBA16UI, gl.RGBA_INTEGER, gl.UNSIGNED_SHORT),
        present: texture(gl, m.presentWidth, m.presentHeight, gl.R16F, gl.RED, gl.FLOAT, presentKm, gl.LINEAR),
        tect0: [0, 1].map(() => texture(gl, gridWidth, gridHeight, gl.RGBA32F, gl.RGBA, gl.FLOAT)),
        tect1: [0, 1].map(() => texture(gl, gridWidth, gridHeight, gl.RGBA32F, gl.RGBA, gl.FLOAT)),
        relief: [0, 1].map(() => texture(gl, m.presentWidth, m.presentHeight, gl.R32F, gl.RED, gl.FLOAT)),
        handoff: texture(gl, gridWidth, gridHeight, gl.RGBA16F, gl.RGBA, gl.HALF_FLOAT),
    };
    const paleo = assets.paleo;
    if (paleo) {
        const size = paleo.width * paleo.height;
        tex.paleoA = texture(gl, paleo.width, paleo.height, gl.R16F, gl.RED, gl.FLOAT, null, gl.LINEAR);
        tex.paleoB = texture(gl, paleo.width, paleo.height, gl.R16F, gl.RED, gl.FLOAT, null, gl.LINEAR);
        tex.paleoBase = texture(gl, paleo.width, paleo.height, gl.R16F, gl.RED, gl.FLOAT,
            paleoKm(paleo, paleo.ages.length - 1, new Float32Array(size)), gl.LINEAR);
        paleo.scratch = new Float32Array(size);
    }
    const earth = {
        gl, assets, programs, tex, plateCount,
        // Present-day ocean volume as a global layer (ETOPO, sea level 0).
        oceanLayerKm: oceanVolume / totalArea,
        gridWidth, gridHeight,
        vao: gl.createVertexArray(),
        targets: {
            tect: [0, 1].map(i => target(gl, [tex.tect0[i], tex.tect1[i]])),
            relief: [0, 1].map(i => target(gl, [tex.relief[i]])),
            handoff: target(gl, [tex.handoff]),
            live: target(gl, [tex.live]),
        },
        uniforms: {
            tectonics: locations(gl, programs.tectonics, ['uLive', 'uPre', 'uPlates', 'uFrame', 'uPrev0', 'uPrev1', 'uEmergent', 'uInit', 'uTimeMyr', 'uDtMyr', 'uRadiusKm']),
            relief: locations(gl, programs.relief, ['uLive', 'uPre', 'uPlates', 'uFrame', 'uRelief', 'uPresent', 'uPresentFrame', 'uTectonics', 'uInit', 'uDtMyr',
                'uPaleoA', 'uPaleoB', 'uPaleoBase', 'uPaleoMix', 'uPaleoWeight', 'uHasPaleo']),
            derived: locations(gl, programs.derived, ['uLive', 'uPre', 'uMa', 'uPlates', 'uFrame', 'uTectonics0', 'uTectonics1', 'uRelief', 'uPresent', 'uHandoff',
                'uTimeMyr', 'uGlowWindowMyr', 'uHandoffWeight', 'uAssimilation', 'uBridge']),
            copy: locations(gl, programs.copy, ['uSource']),
            raster: locations(gl, programs.raster, ['uLive', 'uPre', 'uPlates', 'uFrame', 'uPreCount', 'uPreBlocks', 'uPreOcean']),
        },
        plateData: new Float32Array(plateCount * 16),
        previousQuats: null,
        frames: new Map(),
        pending: new Set(),
        uploadedFrame: -1,
        paleoSlots: [-1, -1],
        livePlates: 0,
        current: 0,
        relief: 0,
        startMyr: null,
        lastPassMyr: null,
        active: false,
    };
    return earth;
}

export async function prepareEarthHistory(earth) {
    const last = earth.assets.manifest.frameCount - 1;
    const [present, oldest] = await Promise.all([decodeFrame(earth.assets, 0), decodeFrame(earth.assets, last)]);
    earth.frames.set(0, present);
    earth.frames.set(last, oldest);
    earth.pre = preNunaBlocks(earth, oldest.data);
    const gl = earth.gl;
    const m = earth.assets.manifest;
    gl.bindTexture(gl.TEXTURE_2D, earth.tex.presentFrame);
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, m.width, m.height, gl.RG_INTEGER, gl.UNSIGNED_SHORT, present.data);
    gl.bindTexture(gl.TEXTURE_2D, null);
    earth.ready = true;
}

// The continental blocks of the oldest (1.8 Ga) frame and the dominant
// oceanic plate, for the synthetic pre-reconstruction drift.
function preNunaBlocks(earth, data) {
    const ids = earth.assets.manifest.plateIds;
    const blockTexels = new Map(), topoTexels = new Map();
    for (let i = 0; i < data.length; i += 2) {
        if (data[i + 1]) blockTexels.set(data[i + 1] - 1, (blockTexels.get(data[i + 1] - 1) ?? 0) + 1);
        else if (data[i]) topoTexels.set(data[i] - 1, (topoTexels.get(data[i] - 1) ?? 0) + 1);
    }
    const blocks = [...blockTexels.keys()].sort((a, b) => blockTexels.get(b) - blockTexels.get(a)).slice(0, MAX_PRE_BLOCKS);
    let ocean = 0, most = -1;
    for (const [k, n] of topoTexels) if (!blockTexels.has(k) && n > most) { most = n; ocean = k; }
    const emergence = new Float32Array(earth.plateCount);
    const isBlock = new Uint8Array(earth.plateCount);
    for (const b of blocks) {
        isBlock[b] = 1;
        emergence[b] = CRATON_EMERGENCE_MA[ids[b]] ?? DEFAULT_EMERGENCE_MA;
    }
    return { blocks: Int32Array.from(blocks), ocean, emergence, isBlock };
}

function hash01(a, b) {
    let h = Math.imul(a ^ 0x9e3779b9, 0x85ebca6b) ^ Math.imul(b + 0x632be5ab, 0xc2b2ae35);
    h = Math.imul(h ^ (h >>> 15), 0x2c1b3c6d);
    h = Math.imul(h ^ (h >>> 12), 0x297a2d39);
    return ((h ^ (h >>> 15)) >>> 0) / 4294967296;
}

// Synthetic drift before 1.8 Ga: per block and per 800 Myr cycle a random
// Euler pole and amplitude; the angle swings out and back (sin), so each
// cycle disperses the blocks and reassembles them (identity at 1.8 Ga).
function preDrift(plate, s, out) {
    const cycle = Math.floor(s / PRE_CYCLE_MYR);
    const phase = s / PRE_CYCLE_MYR - cycle;
    const z = 2 * hash01(plate, cycle * 3 + 1) - 1, lon = 2 * Math.PI * hash01(plate, cycle * 3 + 2);
    const r = Math.sqrt(1 - z * z);
    const amplitude = (35 + 45 * hash01(plate, cycle * 3 + 3)) * Math.PI / 180;
    const half = 0.5 * amplitude * Math.sin(Math.PI * phase);
    const sh = Math.sin(half);
    out[0] = r * Math.cos(lon) * sh; out[1] = z * sh; out[2] = r * Math.sin(lon) * sh; out[3] = Math.cos(half);
}

export function disposeEarthHistory(earth) {
    if (!earth) return;
    const gl = earth.gl;
    for (const program of Object.values(earth.programs)) gl.deleteProgram(program);
    const t = earth.tex;
    for (const x of [t.plates, t.frame, t.presentFrame, t.live, t.present, ...t.tect0, ...t.tect1, ...t.relief, t.handoff,
        t.paleoA, t.paleoB, t.paleoBase]) if (x) gl.deleteTexture(x);
    for (const x of [...earth.targets.tect, ...earth.targets.relief, earth.targets.handoff, earth.targets.live]) gl.deleteFramebuffer(x.framebuffer);
    gl.deleteVertexArray(earth.vao);
}

// ---------------------------------------------------------- kinematics --

function quaternionAt(earth, plate, ma, out, offset) {
    const m = earth.assets.manifest;
    const r = earth.assets.rotations;
    const f = Math.min(Math.max(ma / m.stepMyr, 0), m.frameCount - 1);
    const f0 = Math.floor(f), f1 = Math.min(f0 + 1, m.frameCount - 1), s = f - f0;
    const a = (f0 * earth.plateCount + plate) * 4, b = (f1 * earth.plateCount + plate) * 4;
    const sign = r[a] * r[b] + r[a + 1] * r[b + 1] + r[a + 2] * r[b + 2] + r[a + 3] * r[b + 3] < 0 ? -1 : 1;
    let len = 0;
    for (let c = 0; c < 4; c++) {
        const v = (1 - s) * r[a + c] + s * sign * r[b + c];
        out[offset + c] = v; len += v * v;
    }
    len = Math.sqrt(len) || 1;
    for (let c = 0; c < 4; c++) out[offset + c] /= len;
    const oldest = (m.frameCount - 1) * m.stepMyr;
    if (ma > oldest && earth.pre?.isBlock[plate]) {
        // q = R (x) q1800: rotate the 1.8 Ga position by the synthetic drift.
        preDrift(plate, ma - oldest, driftScratch);
        const [rx, ry, rz, rw] = driftScratch;
        const qx = out[offset], qy = out[offset + 1], qz = out[offset + 2], qw = out[offset + 3];
        out[offset] = rw * qx + rx * qw + ry * qz - rz * qy;
        out[offset + 1] = rw * qy - rx * qz + ry * qw + rz * qx;
        out[offset + 2] = rw * qz + rx * qy - ry * qx + rz * qw;
        out[offset + 3] = rw * qw - rx * qx - ry * qy - rz * qz;
    }
}
const driftScratch = new Float64Array(4);

const scratch = new Float32Array(8);
function updatePlates(earth, ma, frameMa, previousMa) {
    const n = earth.plateCount;
    const d = earth.plateData;
    const dt = 0.5;
    for (let i = 0; i < n; i++) {
        quaternionAt(earth, i, ma, d, i * 4);
        const fr = (Math.round(frameMa / earth.assets.manifest.stepMyr) * earth.plateCount + i) * 4;
        for (let c = 0; c < 4; c++) d[(n + i) * 4 + c] = earth.assets.rotations[fr + c];
        quaternionAt(earth, i, previousMa, d, (2 * n + i) * 4);
        // omega from q(younger) * conj(q(older)): the rotation that carries
        // the plate forward in time.
        const younger = Math.max(ma - dt, 0), older = younger + dt, span = dt;
        quaternionAt(earth, i, younger, scratch, 0);
        quaternionAt(earth, i, older, scratch, 4);
        const ax = scratch[0], ay = scratch[1], az = scratch[2], aw = scratch[3];
        const bx = -scratch[4], by = -scratch[5], bz = -scratch[6], bw = scratch[7];
        // Hamilton product a * b.
        const x = aw * bx + ax * bw + ay * bz - az * by;
        const y = aw * by - ax * bz + ay * bw + az * bx;
        const z = aw * bz + ax * by - ay * bx + az * bw;
        const w = aw * bw - ax * bx - ay * by - az * bz;
        const sgn = w < 0 ? -1 : 1;
        const o = (3 * n + i) * 4;
        d[o] = 2 * sgn * x / span; d[o + 1] = 2 * sgn * y / span; d[o + 2] = 2 * sgn * z / span;
        d[o + 3] = earth.pre ? earth.pre.emergence[i] : 0;
    }
    const gl = earth.gl;
    gl.bindTexture(gl.TEXTURE_2D, earth.tex.plates);
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, n, 4, gl.RGBA, gl.FLOAT, d);
    gl.bindTexture(gl.TEXTURE_2D, null);
}

function requestFrame(earth, index) {
    const m = earth.assets.manifest;
    if (index < 0 || index >= m.frameCount || earth.frames.has(index) || earth.pending.has(index)) return;
    earth.pending.add(index);
    decodeFrame(earth.assets, index).then(frame => {
        earth.frames.set(index, frame);
        earth.pending.delete(index);
    }, () => earth.pending.delete(index));
}

// Uploads the nearest decoded frame; returns its age.
function updateFrame(earth, ma) {
    const m = earth.assets.manifest;
    const wanted = Math.min(Math.max(Math.round(ma / m.stepMyr), 0), m.frameCount - 1);
    for (let k = 0; k <= 4; k++) requestFrame(earth, wanted - k);
    for (const key of earth.frames.keys()) if (key !== 0 && (key > wanted + 2 || key < wanted - 8)) earth.frames.delete(key);
    let use = wanted;
    if (!earth.frames.has(use)) {
        use = earth.uploadedFrame >= 0 ? earth.uploadedFrame : m.frameCount - 1;
    }
    if (use !== earth.uploadedFrame && earth.frames.has(use)) {
        const frame = earth.frames.get(use);
        const gl = earth.gl;
        gl.bindTexture(gl.TEXTURE_2D, earth.tex.frame);
        gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, m.width, m.height, gl.RG_INTEGER, gl.UNSIGNED_SHORT, frame.data);
        gl.bindTexture(gl.TEXTURE_2D, null);
        earth.uploadedFrame = use;
        earth.livePlates = frame.plates;
    }
    return earth.uploadedFrame * m.stepMyr;
}

// ------------------------------------------------------------ GPU passes --

function pass(earth, tgt, width, height, program, setup) {
    const gl = earth.gl;
    gl.bindFramebuffer(gl.FRAMEBUFFER, tgt.framebuffer);
    gl.drawBuffers(Array.from({ length: tgt.count }, (_, i) => gl.COLOR_ATTACHMENT0 + i));
    gl.viewport(0, 0, width, height);
    gl.disable(gl.DEPTH_TEST);
    gl.disable(gl.BLEND);
    gl.disable(gl.CULL_FACE);
    gl.useProgram(program);
    setup();
    gl.bindVertexArray(earth.vao);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    gl.bindVertexArray(null);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
}

function bind(gl, unit, tex, location) {
    gl.activeTexture(gl.TEXTURE0 + unit);
    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.uniform1i(location, unit);
}

function preActive(earth, ma) {
    const m = earth.assets.manifest;
    return earth.pre && ma > (m.frameCount - 1) * m.stepMyr;
}

// Rasterises the drifting blocks (pre-1.8 Ga) and binds the live raster.
function updateLive(earth, ma) {
    const gl = earth.gl;
    const u = earth.uniforms.raster;
    const pre = earth.pre;
    if (!preActive(earth, ma)) return;
    const m = earth.assets.manifest;
    pass(earth, earth.targets.live, m.width, m.height, earth.programs.raster, () => {
        bind(gl, 0, earth.tex.plates, u.uPlates);
        bind(gl, 1, earth.tex.frame, u.uFrame);
        // uLive is unused here but must not alias the float unit 0.
        bind(gl, 2, earth.tex.presentFrame, u.uLive);
        gl.uniform1i(u.uPreCount, pre.blocks.length);
        gl.uniform1iv(u.uPreBlocks, pre.blocks);
        gl.uniform1i(u.uPreOcean, pre.ocean);
        gl.uniform1i(u.uPre, 0);
    });
}

function bindLive(earth, unit, u, ma) {
    const gl = earth.gl;
    bind(gl, unit, earth.tex.live, u.uLive);
    gl.uniform1i(u.uPre, preActive(earth, ma) ? 1 : 0);
}

// Takes over from the emergent simulation: snapshot its surface for the
// cross-fade and seed ocean ages from its crust.
export function startEarthHistory(earth, timeMyr, emergentState0, emergentDerived) {
    const gl = earth.gl;
    pass(earth, earth.targets.handoff, earth.gridWidth, earth.gridHeight, earth.programs.copy, () => {
        bind(gl, 0, emergentDerived, earth.uniforms.copy.uSource);
    });
    earth.startMyr = timeMyr;
    earth.lastPassMyr = timeMyr;
    earth.active = true;
    runPasses(earth, timeMyr, 0, true, emergentState0);
}

function runPasses(earth, timeMyr, dtMyr, init, emergentState0 = null) {
    const gl = earth.gl;
    const ma = earthAgeMa(timeMyr);
    const frameMa = updateFrame(earth, ma);
    updatePlates(earth, ma, frameMa, earthAgeMa(timeMyr - dtMyr));
    updateLive(earth, ma);
    const src = earth.current, dst = 1 - src;
    const u = earth.uniforms.tectonics;
    pass(earth, earth.targets.tect[dst], earth.gridWidth, earth.gridHeight, earth.programs.tectonics, () => {
        bind(gl, 0, earth.tex.plates, u.uPlates);
        bind(gl, 1, earth.tex.frame, u.uFrame);
        bind(gl, 2, earth.tex.tect0[src], u.uPrev0);
        bind(gl, 3, earth.tex.tect1[src], u.uPrev1);
        bind(gl, 4, emergentState0 ?? earth.tex.tect0[src], u.uEmergent);
        bindLive(earth, 5, u, ma);
        gl.uniform1i(u.uInit, init ? 1 : 0);
        gl.uniform1f(u.uTimeMyr, timeMyr);
        gl.uniform1f(u.uDtMyr, dtMyr);
        gl.uniform1f(u.uRadiusKm, 6371);
    });
    earth.current = dst;
    if (init) {
        // A second pass so boundary processes see a consistent neighbourhood.
        runPasses(earth, timeMyr, 0, false);
    }
    const r = earth.uniforms.relief;
    const m = earth.assets.manifest;
    const rdst = 1 - earth.relief;
    // Upload before binding: texSubImage2D rebinds the active texture unit.
    const p = updatePaleo(earth, ma);
    pass(earth, earth.targets.relief[rdst], m.presentWidth, m.presentHeight, earth.programs.relief, () => {
        bind(gl, 0, earth.tex.plates, r.uPlates);
        bind(gl, 1, earth.tex.frame, r.uFrame);
        bind(gl, 2, earth.tex.relief[earth.relief], r.uRelief);
        bind(gl, 3, earth.tex.present, r.uPresent);
        bind(gl, 4, earth.tex.presentFrame, r.uPresentFrame);
        bind(gl, 5, earth.tex.tect0[earth.current], r.uTectonics);
        gl.uniform1i(r.uInit, init ? 1 : 0);
        gl.uniform1f(r.uDtMyr, dtMyr);
        gl.uniform1i(r.uHasPaleo, earth.tex.paleoBase ? 1 : 0);
        bind(gl, 6, earth.tex.paleoA ?? earth.tex.present, r.uPaleoA);
        bind(gl, 7, earth.tex.paleoB ?? earth.tex.present, r.uPaleoB);
        bind(gl, 8, earth.tex.paleoBase ?? earth.tex.present, r.uPaleoBase);
        bindLive(earth, 9, r, ma);
        gl.uniform1f(r.uPaleoMix, p.mix);
        gl.uniform1f(r.uPaleoWeight, p.weight);
    });
    earth.relief = rdst;
}

function paleoKm(paleo, index, out) {
    const size = paleo.width * paleo.height;
    for (let i = 0, o = index * size; i < size; i++) out[i] = paleo.data[o + i] / 1000;
    return out;
}

// Uploads the two PaleoDEMs bracketing the age; returns the blend and the
// assimilation weight (fades in from 560 to 540 Ma).
function updatePaleo(earth, ma) {
    const paleo = earth.assets.paleo;
    if (!paleo) return { mix: 0, weight: 0 };
    const ages = paleo.ages;
    const oldest = ages[ages.length - 1];
    let i = 0;
    while (i < ages.length - 2 && ages[i + 1] < ma) i++;
    const mix = Math.min(Math.max((ma - ages[i]) / (ages[i + 1] - ages[i]), 0), 1);
    const gl = earth.gl;
    for (const [slot, index, tex] of [[0, i, earth.tex.paleoA], [1, i + 1, earth.tex.paleoB]]) {
        if (earth.paleoSlots[slot] === index) continue;
        gl.bindTexture(gl.TEXTURE_2D, tex);
        gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, paleo.width, paleo.height, gl.RED, gl.FLOAT, paleoKm(paleo, index, paleo.scratch));
        earth.paleoSlots[slot] = index;
    }
    gl.bindTexture(gl.TEXTURE_2D, null);
    const w = Math.min(Math.max((oldest + 20 - ma) / 20, 0), 1);
    return { mix, weight: w * w * (3 - 2 * w) };
}

// Advances the Earth regime; returns false if it is not running.
export function stepEarthHistory(earth, timeMyr) {
    if (!earth?.active) return false;
    const dt = timeMyr - earth.lastPassMyr;
    if (dt >= PASS_MYR) {
        runPasses(earth, timeMyr, Math.min(dt, 5), false);
        earth.lastPassMyr = timeMyr;
    }
    return true;
}

export function deriveEarthHistory(earth, timeMyr, glowWindowMyr, derivedTarget) {
    const gl = earth.gl;
    const u = earth.uniforms.derived;
    const ma = earthAgeMa(timeMyr);
    updatePlates(earth, ma, updateFrame(earth, ma), ma);
    updateLive(earth, ma);
    pass(earth, derivedTarget, earth.gridWidth, earth.gridHeight, earth.programs.derived, () => {
        bind(gl, 0, earth.tex.plates, u.uPlates);
        bind(gl, 1, earth.tex.frame, u.uFrame);
        bind(gl, 2, earth.tex.tect0[earth.current], u.uTectonics0);
        bind(gl, 3, earth.tex.tect1[earth.current], u.uTectonics1);
        bind(gl, 4, earth.tex.relief[earth.relief], u.uRelief);
        bind(gl, 5, earth.tex.present, u.uPresent);
        bind(gl, 6, earth.tex.handoff, u.uHandoff);
        bindLive(earth, 7, u, ma);
        gl.uniform1f(u.uMa, ma);
        gl.uniform1f(u.uTimeMyr, timeMyr);
        gl.uniform1f(u.uGlowWindowMyr, glowWindowMyr);
        const h = Math.min(Math.max((timeMyr - earth.startMyr) / HANDOFF_MYR, 0), 1);
        gl.uniform1f(u.uHandoffWeight, h * h * (3 - 2 * h));
        const a = Math.min(Math.max(1 - ma / ASSIMILATION_MA, 0), 1);
        gl.uniform1f(u.uAssimilation, a * a * (3 - 2 * a));
        gl.uniform1i(u.uBridge, ma > 540 ? 7 : 3);
    });
    gl.activeTexture(gl.TEXTURE0);
}

export function earthHistoryStatus(earth, timeMyr) {
    const ma = earthAgeMa(timeMyr);
    return {
        ma,
        era: earthEra(ma),
        plates: preActive(earth, ma) ? earth.pre.blocks.length + 1 : Math.max(earth.livePlates - 1, 0),
        synthetic: !!preActive(earth, ma),
        sources: earth.assets.manifest.sources,
    };
}
