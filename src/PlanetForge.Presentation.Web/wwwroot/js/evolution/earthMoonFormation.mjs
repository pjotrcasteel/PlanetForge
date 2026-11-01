// F0 Earth-Moon formation prologue: deterministic, evidence-labelled reconstruction.
// Moon assembly advances a scenario-local year, not another 22 million years.
// Theia's orbit/debris are a visual scenario, NOT a solved hydrodynamic giant impact.
// The 60 Myr selected impact instant is a scenario parameter within a broad disputed window.
export const SECONDS_PER_YEAR = 365.25 * 24 * 3600;
export const IMPACT_AGE_YEARS = 60e6;
export const HANDOFF_AGE_YEARS = IMPACT_AGE_YEARS + 1;
export const IMPACT_SCENE_SECONDS = 9;
export const MOON_SCENE_SECONDS = 12;
export const AFTERMATH_SECONDS = 3;

const clamp = (x, low = 0, high = 1) => Math.min(high, Math.max(low, x));
const smooth = x => { const t = clamp(x); return t * t * (3 - 2 * t); };
const stageAt = formation => formation.stage;

export function createEarthMoonFormation(seed = 1) {
    return {
        seed: Number(seed) >>> 0, ageYears: 0, stage: 'accretion',
        stageSeconds: 0, impactJustBegun: false, complete: false,
        playbackYearsPerSecond: 0, visualSeconds: 0, resources: null,
    };
}

export function stepEarthMoonFormation(formation, elapsedSeconds, maximumYearsPerSecond) {
    if (formation.complete) return false;
    if (!Number.isFinite(elapsedSeconds) || elapsedSeconds < 0) throw new RangeError('Invalid formation elapsed time.');
    if (!Number.isFinite(maximumYearsPerSecond) || maximumYearsPerSecond <= 0)
        throw new RangeError('Invalid formation time rate.');
    // On low-FPS phones a single WebGL frame can take longer than a second.
    // Preserve actual foreground elapsed time; the previous 1-second cap made
    // a 9-second impact cinematic take minutes on overloaded GPUs.
    let remaining = Math.min(elapsedSeconds, 120);
    if (remaining === 0) return false;
    formation.visualSeconds += remaining;
    // Consume leftover time at each scene boundary so a stalled frame does
    // not stretch or repeat a cinematic. At most four distinct stages exist.
    for (let transitions = 0; transitions < 4 && remaining > 0 && !formation.complete; transitions++) {
        switch (formation.stage) {
            case 'accretion': {
                const untilImpact = Math.max(0, (IMPACT_AGE_YEARS - formation.ageYears) / maximumYearsPerSecond);
                const spent = Math.min(remaining, untilImpact);
                formation.playbackYearsPerSecond = maximumYearsPerSecond;
                formation.ageYears = Math.min(IMPACT_AGE_YEARS, formation.ageYears + spent * maximumYearsPerSecond);
                remaining -= spent;
                if (formation.ageYears >= IMPACT_AGE_YEARS - 1e-6) {
                    formation.stage = 'impact';
                    formation.stageSeconds = 0;
                    formation.impactJustBegun = true;
                } else {
                    remaining = 0;
                }
                break;
            }
            case 'impact': {
                const spent = Math.min(remaining, Math.max(0, IMPACT_SCENE_SECONDS - formation.stageSeconds));
                formation.playbackYearsPerSecond = 0;
                formation.stageSeconds += spent;
                remaining -= spent;
                if (formation.stageSeconds >= IMPACT_SCENE_SECONDS) {
                    formation.stage = 'moon-assembly';
                    formation.stageSeconds = 0;
                } else {
                    remaining = 0;
                }
                break;
            }
            case 'moon-assembly': {
                const spent = Math.min(remaining, Math.max(0, MOON_SCENE_SECONDS - formation.stageSeconds));
                formation.stageSeconds += spent;
                remaining -= spent;
                formation.ageYears = IMPACT_AGE_YEARS + (HANDOFF_AGE_YEARS - IMPACT_AGE_YEARS)
                    * smooth(formation.stageSeconds / MOON_SCENE_SECONDS);
                formation.playbackYearsPerSecond = (HANDOFF_AGE_YEARS - IMPACT_AGE_YEARS)
                    / MOON_SCENE_SECONDS;
                if (formation.stageSeconds >= MOON_SCENE_SECONDS) {
                    formation.stage = 'aftermath';
                    formation.stageSeconds = 0;
                } else {
                    remaining = 0;
                }
                break;
            }
            case 'aftermath': {
                const spent = Math.min(remaining, Math.max(0, AFTERMATH_SECONDS - formation.stageSeconds));
                formation.playbackYearsPerSecond = 0;
                formation.ageYears = HANDOFF_AGE_YEARS;
                formation.stageSeconds += spent;
                remaining -= spent;
                if (formation.stageSeconds >= AFTERMATH_SECONDS) formation.complete = true;
                break;
            }
            default:
                throw new Error('Unknown Earth-Moon formation stage.');
        }
    }
    return true;
}

export function earthMoonFormationStatus(formation) {
    const phase = {
        accretion: 'Proto-Earth · gravitational accretion',
        impact: 'Moon-forming giant impact · Theia scenario',
        'moon-assembly': 'Orbiting ejecta · Moon assembly scenario',
        aftermath: 'Impact aftermath · global magma ocean',
    }[stageAt(formation)] ?? 'Impact aftermath · magma ocean';
    const progress = formation.stage === 'accretion' ? formation.ageYears / IMPACT_AGE_YEARS
        : formation.stage === 'impact' ? formation.stageSeconds / IMPACT_SCENE_SECONDS
            : formation.stage === 'moon-assembly' ? formation.stageSeconds / MOON_SCENE_SECONDS
                : formation.stageSeconds / AFTERMATH_SECONDS;
    const localSceneDuration = {
        impact: IMPACT_SCENE_SECONDS,
        'moon-assembly': MOON_SCENE_SECONDS,
        aftermath: AFTERMATH_SECONDS,
    }[formation.stage];
    const rateLabel = formation.stage === 'accretion'
        ? (formation.playbackYearsPerSecond / 1e6).toFixed(1) + ' Myr / s'
        : 'scene ' + formation.stageSeconds.toFixed(1) + ' / ' + localSceneDuration + ' s';
    return {
        phase, progress: clamp(progress), ageYears: formation.ageYears,
        timeSeconds: formation.ageYears * SECONDS_PER_YEAR,
        stage: formation.stage,
        // The planetary clock pauses during the local collision; show visible
        // real-time scene progress so T+60 Myr cannot look permanently stalled.
        rateLabel,
        authority: 'reconstruction',
        source: 'NASA Moon Formation; National Academies 2023 Earth-Moon formation',
        limitations: 'Illustrative impact scenario; not a hydrodynamic/N-body solution. The 60 Myr event age, debris, and Moon assembly path are uncertain.',
    };
}

export function moonFormationImpactEvent(seed) {
    const instant = IMPACT_AGE_YEARS * SECONDS_PER_YEAR;
    return Object.freeze({
        id: 'moon-forming-scenario-' + (Number(seed) >>> 0),
        key: 'moon-forming-impact', type: 'earth-milestone',
        title: 'Moon-forming giant impact · reconstructed scenario',
        planetSeconds: instant,
        interval: { startSeconds: 50e6 * SECONDS_PER_YEAR, endSeconds: 150e6 * SECONDS_PER_YEAR },
        authority: 'reconstruction', scope: 'global', scale: 'S0 Earth-Moon formation · two-body cinematic',
        priority: 5, trigger: 'Selected Theia-impact scenario enters its collision phase, once per restart.',
        uncertainty: 'Impact timing, initial angular momentum, impactor composition and assembly route remain debated.',
        source: 'NASA Science, Moon Formation; National Academies, Origins, Worlds, and Life (2023), Q3.3',
        limitations: 'This is an illustrative reconstruction, not an impact solved by PlanetForge; no exact collision site or orbit is claimed.',
    });
}

// These shaders use view-dependent ray/sphere intersections and coherent
// rocky materials. All bodies and ejecta share one physically motivated light;
// visual timing and deformation are explicitly reconstructive, not SPH physics.
const sceneVertex = `#version 300 es
precision highp float;
out vec2 vUv;
void main() {
    vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    // p spans 0..2 on the oversized full-screen triangle; the viewport
    // samples 0..1. Halving p projected the planet into the upper-right corner.
    vUv = p;
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}`;

const sceneFragment = `#version 300 es
precision highp float;
in vec2 vUv;
out vec4 outColor;
uniform vec3 uCamera;
uniform float uAspect;
uniform float uTanHalfFov;
uniform float uAge;
uniform float uStage;
uniform float uProgress;
uniform float uSeconds;
uniform float uSeed;
const vec3 LIGHT = vec3(-0.58, 0.48, 0.66);
float hash21(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}
float hash31(vec3 p) {
    return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453);
}
float noise3(vec3 p) {
    vec3 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash31(i);
    float b = hash31(i + vec3(1, 0, 0));
    float c = hash31(i + vec3(0, 1, 0));
    float d = hash31(i + vec3(1, 1, 0));
    float e = hash31(i + vec3(0, 0, 1));
    float g = hash31(i + vec3(1, 0, 1));
    float h = hash31(i + vec3(0, 1, 1));
    float k = hash31(i + vec3(1, 1, 1));
    return mix(mix(mix(a,b,f.x),mix(c,d,f.x),f.y),mix(mix(e,g,f.x),mix(h,k,f.x),f.y),f.z);
}
float fbm(vec3 p) {
    float value = 0.0;
    float amp = 0.52;
    for (int k = 0; k < 4; k++) {
        value += amp * noise3(p);
        p = p * 2.03 + vec3(13.1, 7.7, 19.4);
        amp *= 0.50;
    }
    return value;
}
vec2 intersectSphere(vec3 ro, vec3 rd, vec3 center, float radius) {
    vec3 delta = ro - center;
    float b = dot(delta, rd);
    float c = dot(delta, delta) - radius * radius;
    float d = b*b - c;
    if (d <= 0.0) return vec2(-1.0);
    float root = sqrt(d);
    return vec2(-b-root, -b+root);
}
vec3 background(vec3 rd) {
    // Sparse, tiny stars and a very dark Galactic dust lane: no flat sprite sheet.
    vec3 p = normalize(rd);
    vec2 sk = vec2(atan(p.z,p.x), asin(p.y))*350.0;
    vec2 cell = floor(sk);
    vec2 f = fract(sk) - 0.5;
    float star = hash21(cell);
    float nearest = length(f - (vec2(hash21(cell+13.7), hash21(cell+5.2))-0.5)*0.7);
    float glint = pow(max(0.0, 1.0 - nearest*3.8), 12.0) *
        step(0.995, star) * (0.25 + pow(star,12.0));
    float galaxy = pow(max(0.0, 1.0 - abs(dot(rd, normalize(vec3(0.12,0.87,0.47))))*3.0), 5.0);
    galaxy *= 0.012 + 0.02*fbm(rd*65.0);
    return vec3(0.0014,0.0018,0.0031) + galaxy*vec3(0.42,0.53,0.78) + glint*vec3(0.84,0.91,1.0);
}
// Proto-Earth F0.5a: one material shot. The surface is an incandescent
// silicate melt ocean with dark transient quenched scum. No stable tectonic
// plates are implied. The spectrum and coverage are visual reconstructions.
vec3 protoEarthSurface(vec3 n, vec3 rd, float hot, float time) {
    vec3 q=n*6.2+vec3(uSeed*0.011,0.0,time*0.0012);
    // Multi-scale turbulent domains instead of circles, cellular polygons,
    // crosshatched sine lines or expensive per-pixel nearest-cell loops.
    float warp=fbm(q*0.71+vec3(4.2,1.1,6.3));
    vec3 shifted=q+vec3(1.2*(warp-0.5),
        0.8*(warp-0.5),-1.1*(warp-0.5));
    float crustField=fbm(shifted*0.86+11.7);
    float flow=fbm(shifted*2.25+vec3(time*0.005,-time*0.003,13.1));
    float grit=noise3(q*23.0+31.5);
    float ash=noise3(q*67.0+61.7);
    // Cooling rafts are mottled and partially broken, not outlined in neon.
    float crust=smoothstep(0.38+hot*0.018,0.58+hot*0.018,
        crustField*0.72+flow*0.28);
    float fracture=1.0-smoothstep(0.018,0.094,abs(flow-0.49));
    float molten=clamp((1.0-crust)*0.92+fracture*crust*0.21,0.0,1.0);
    vec3 bump=vec3(grit-0.5,
        noise3(q*23.0+vec3(19.7,4.2,32.5))-0.5,
        noise3(q*23.0+vec3(4.1,23.2,11.9))-0.5);
    vec3 rockNormal=normalize(n+0.48*crust*(bump-n*dot(n,bump)));

    // Same upper-left cinematic Sun setup as the regular main globe.
    vec3 towardEye=normalize(uCamera);
    vec3 right=normalize(cross(vec3(0.0,1.0,0.0),towardEye));
    if (length(right)<0.01) right=vec3(1.0,0.0,0.0);
    vec3 above=normalize(cross(towardEye,right));
    vec3 sun=normalize(towardEye*0.62-right*0.64+above*0.43);
    float sunlight=max(dot(rockNormal,sun),0.0);
    vec3 basalt=mix(vec3(0.042,0.045,0.049),
        vec3(0.245,0.205,0.168),smoothstep(0.24,0.79,crustField));
    basalt*=0.68+0.38*grit+0.13*ash;
    vec3 rock=basalt*(0.036+1.32*sunlight);

    // Broad cooled cherry-red melt and smaller incandescent overturning.
    // A restrained self-lit palette avoids uniform bright orange and
    // unphysical white-hot spots.
    float heat=clamp(0.25+0.66*hot+0.27*(flow-0.5),0.0,1.0);
    vec3 hotTint=mix(vec3(0.34,0.014,0.002),
        vec3(1.92,0.53,0.054),smoothstep(0.27,0.97,heat));
    float thermal=pow(smoothstep(0.17,1.0,heat),2.45);
    vec3 lava=hotTint*thermal*(0.76+0.32*flow);
    vec3 color=rock*(1.0-molten)+lava*molten;
    vec3 halfway=normalize(sun-rd);
    float mineralGlint=pow(max(dot(rockNormal,halfway),0.0),19.0)*sunlight;
    color+=vec3(0.13)*mineralGlint*crust*0.030;
    return max(color,vec3(0.0));
}
vec3 rockySurface(vec3 pos, vec3 norm, vec3 rd, float hot, float kind, float time) {
    // Multi-scale silicate materials: broad dark rock, granular regolith,
    // irregular flowing melt. No Voronoi polygon "plate" outlines at F0.
    vec3 q = norm * 4.8 + vec3(uSeed * 0.011, 0.0, time * 0.003);
    float low = fbm(q * 0.85);
    float mid = fbm(q * 2.8 + 17.2);
    float grain = noise3(q * 18.0 + 2.0);
    float flows = fbm(q * 1.35 + vec3(time * 0.014, -time * 0.009, 0.0));
    float veins = 1.0 - smoothstep(0.035, 0.18, abs(mid - 0.49));
    float melt = clamp(0.12 + 0.60 * smoothstep(0.37, 0.69, flows)
        + 0.32 * veins + 0.18 * (grain - 0.5), 0.0, 1.0);
    vec3 basalt = mix(vec3(0.018, 0.022, 0.027),
        vec3(0.20, 0.17, 0.15), smoothstep(0.30, 0.76, low));
    basalt *= 0.80 + 0.36 * grain;
    vec3 sun = normalize(LIGHT);
    float nl = max(dot(norm, sun), 0.0);
    vec3 halfDir = normalize(sun - rd);
    float spec = pow(max(dot(norm, halfDir), 0.0), 40.0)
        * (0.025 + 0.045 * grain);
    vec3 reflected = basalt * (0.035 + 1.25 * nl) + vec3(spec);
    float thermal = clamp(hot, 0.0, 1.0);
    // Approximate visible incandescent silicate emission with dark cooling
    // islands, cherry-red melt, and a few near-white upwelling filaments.
    vec3 redMelt = mix(vec3(0.23, 0.010, 0.002),
        vec3(1.55, 0.27, 0.026), smoothstep(0.18, 0.88, melt));
    vec3 whiteHot = vec3(2.7, 1.13, 0.29) *
        pow(max(0.0, (melt - 0.65) / 0.35), 3.0) * thermal;
    vec3 incandescent = (redMelt + whiteHot) * (0.48 + thermal * 0.72);
    float exposed = thermal * (0.52 + melt * 0.46);
    // Theia remains a mostly solid, dusty differentiated protoplanet.
    if (kind > 0.5 && kind < 1.5) {
        exposed *= 0.11;
        incandescent *= 0.42;
    }
    // The newborn Moon is also hot; it must not look like a modern grey orb
    // five seconds after leaving a vapour-and-melt-rich collision.
    if (kind > 1.5) exposed *= 0.64;
    vec3 visible = mix(reflected, incandescent, exposed);
    visible += incandescent * veins * thermal * (kind < 0.5 ? 0.12 : 0.025);
    float mu = clamp(dot(norm, -rd), 0.0, 1.0);
    float fresnel = pow(1.0 - mu, 4.3);
    visible += fresnel * mix(vec3(0.03, 0.055, 0.065),
        vec3(0.32, 0.09, 0.025), thermal) * (kind < 0.5 ? 1.0 : 0.35);
    return visible;
}
vec3 limbGlow(vec3 ro, vec3 rd, vec3 center, float radius, vec3 color, float strength) {
    float along = max(0.0,dot(center-ro,rd));
    float impact = length(ro + rd*along - center);
    float d = max(0.0,impact-radius);
    float fade = exp(-d*d / max(0.0001,0.035*radius*radius));
    return color * strength * fade * clamp(impact/radius,0.0,1.0);
}
// A layered, widening silicate vapour curtain. This is a low-cost cinematic
// radiance approximation of ejecta, not an impact hydrodynamics solution.
vec3 impactVapor(vec3 camera, vec3 rd, vec3 right, vec3 up, vec3 forward) {
    float young = clamp((uProgress - 0.10) / 0.78, 0.0, 1.0);
    float strength = smoothstep(0.08,0.34,uProgress) *
        (1.0 - smoothstep(0.70,1.0,uProgress));
    vec3 axis = normalize(right*0.62 + up*0.78 + forward*0.10);
    vec3 source = right*0.73 + up*0.21;
    vec3 centre = source + axis*(0.15 + 1.12*young);
    float t = max(0.0, dot(centre-camera, rd));
    vec3 q = camera + rd*t;
    vec3 rel = q-centre;
    float axial = dot(rel, axis);
    float radial = length(rel - axis*axial);
    float rough = 0.75 + 0.45*noise3(q*8.0+vec3(uSeconds*0.09));
    float width = (0.15 + 0.72*young) * (1.0 - 0.45*clamp(abs(axial),0.0,1.0));
    float cloud = exp(-pow(radial*rough/max(width,0.05),2.0)) *
        exp(-pow(axial/(0.36+1.1*young),2.0));
    vec3 ash = mix(vec3(0.54,0.11,0.018),vec3(1.9,0.80,0.24),
        clamp(1.0-young+cloud*0.2,0.0,1.0));
    return ash*cloud*strength*1.6;
}
void main() {
    vec2 uv = vUv*2.0-1.0;
    vec3 forward = normalize(-uCamera);
    vec3 right = normalize(cross(forward,vec3(0,1,0)));
    if (length(right)<0.01) right=vec3(1,0,0);
    vec3 up = normalize(cross(right,forward));
    vec3 ray = normalize(forward+right*uv.x*uAspect*uTanHalfFov+up*uv.y*uTanHalfFov);
    vec3 camera = uCamera;
    float growth = smoothstep(0.0,1.0,clamp(uAge/60.0,0.0,1.0));
    float earthRadius = mix(0.035,0.97,pow(growth,0.54));
    if (uStage>1.5) earthRadius=1.0;
    vec3 earth = vec3(0.0);
    // Grazing impact geometry: the collider enters the camera's central
    // third as contact approaches, including on a tall narrow phone screen.
    vec3 theia = right*(2.42-1.93*clamp(uProgress,0.0,1.0)) + up*0.24;
    float theiaVisible = 0.0;
    if (uStage < 0.5) {
        theiaVisible = smoothstep(0.68,0.91,growth);
        theia = right*(3.1-1.9*smoothstep(0.77,1.0,growth))+up*0.15;
    } else if (uStage < 1.5) {
        theiaVisible = 1.0-smoothstep(0.22,0.54,uProgress);
    }
    vec3 moon = right*(1.43+0.2*sin(uSeconds*0.05)) + up*0.17;
    float moonVisible = uStage>1.5 ? smoothstep(0.04,0.72,uProgress) : 0.0;
    if (uStage > 2.5) moonVisible=1.0;
    float moonRadius = mix(0.04,0.273,moonVisible);
    vec2 earthHit = intersectSphere(camera,ray,earth,earthRadius);
    vec2 theiaHit = intersectSphere(camera,ray,theia,0.49);
    vec2 moonHit = intersectSphere(camera,ray,moon,moonRadius);
    float nearest = 1e6;
    float body = -1.0;
    if (earthHit.x>0.0 && earthHit.x<nearest) { nearest=earthHit.x; body=0.0; }
    if (theiaVisible>0.001 && theiaHit.x>0.0 && theiaHit.x<nearest) { nearest=theiaHit.x; body=1.0; }
    if (moonVisible>0.001 && moonHit.x>0.0 && moonHit.x<nearest) { nearest=moonHit.x; body=2.0; }
    vec3 col = background(ray);
    // Finite-thickness, partially opaque hot orbital debris: broad dust
    // structure punctuated by cooler, darker clumps, not a hairline ellipse.
    if (uStage>1.5) {
        float diskOpacity = uStage>2.5 ? 0.35 : (1.12-uProgress*0.58);
        vec3 diskNormal = normalize(up + forward*0.27);
        float plane = dot(ray,diskNormal);
        if (abs(plane)>0.0001) {
            float distanceToPlane = -dot(camera,diskNormal)/plane;
            vec3 q = camera+ray*distanceToPlane;
            float r=length(q);
            float band = smoothstep(0.60,0.90,r)*(1.0-smoothstep(2.05,2.65,r));
            float dust=fbm(q*11.0+vec3(uSeconds*0.035));
            float furrows = 0.75 + 0.25*sin(r*33.0 + atan(q.y,q.x)*8.0);
            float density = band*(0.18+0.82*dust)*furrows*diskOpacity;
            if (distanceToPlane>0.0 && distanceToPlane<nearest) {
                vec3 smoulder = mix(vec3(0.23,0.20,0.18),vec3(1.12,0.38,0.08),
                    clamp(1.0-uProgress*0.72,0.0,1.0));
                col += density*smoulder*0.82;
            }
        }
    }
    if (body>=0.0) {
        vec3 center=body<0.5 ? earth : body<1.5 ? theia : moon;
        float radius=body<0.5 ? earthRadius : body<1.5 ? 0.49 : moonRadius;
        vec3 pos = camera+ray*nearest;
        vec3 norm = normalize(pos-center);
        float hot = body<0.5 ? mix(0.25,0.88,growth) : 0.10;
        if (uStage>0.5 && body<0.5)
            hot = mix(0.88,1.0,smoothstep(0.14,0.42,uProgress));
        if (body>1.5) hot = uStage>2.5 ? 0.36 : mix(0.86,0.45,uProgress);
        col = body<0.5 ? protoEarthSurface(norm,ray,hot,uSeconds)
            : rockySurface(pos,norm,ray,hot,body,uSeconds);
        if (body>0.5 && body<1.5 && uStage>0.5) {
            // Theia fragments into an irregular cloud rather than fading as
            // a pristine, suddenly disappearing smooth ball.
            float breakup = smoothstep(0.22,0.57,uProgress);
            float fragmentation = noise3(norm*24.0+vec3(uSeconds*0.08));
            col = mix(col,background(ray),
                breakup*smoothstep(0.2,0.8,fragmentation)*0.85);
        }
        float atmo = 0.0; // The F0 pre-atmosphere world has no artificial rim haze.
        col += pow(1.0-max(dot(norm,-ray),0.0),4.0)*atmo*vec3(0.32,0.20,0.15);
    }
    // Glowing splashed mantle and a turbulent ejecta curtain at the grazing
    // contact point. A hard flash is brief; dusty vapour survives afterward.
    if (uStage>0.5 && uStage<1.5) {
        vec3 epicenter = right*0.77 + up*0.18;
        float d=length(cross(ray,normalize(epicenter-camera)));
        float peak=exp(-pow((uProgress-0.36)/0.115,2.0));
        float glare = exp(-d*d/0.0025)*peak;
        col += glare*vec3(3.5,1.72,0.53);
        col += exp(-d*d/0.021)*peak*vec3(0.70,0.19,0.025);
        col += impactVapor(camera,ray,right,up,forward);
    }
    float ejectaStrength = uStage>0.5 && uStage<1.5 ? smoothstep(0.15,0.5,uProgress)
        : uStage>1.5 ? (uStage>2.5?0.15:1.0-uProgress*0.5):0.0;
    col+=limbGlow(camera,ray,earth,earthRadius,vec3(0.65,0.12,0.015),0.12*ejectaStrength);
    // Filmic shoulder: retain colour in incandescent regions rather than
    // clipping flat orange. The site's optical bloom handles bright highlights.
    col = col/(1.0+col*0.65);
    col = pow(max(col,vec3(0.0)),vec3(0.86));
    outColor=vec4(col,1.0);
}`;

const ejectaVertex = `#version 300 es
precision highp float;
uniform vec3 uCamera;
uniform float uAspect;
uniform float uTanHalfFov;
uniform float uSeconds;
uniform float uStage;
uniform float uProgress;
uniform float uSeed;
uniform float uViewHeight;
out float vHeat;
out float vOpacity;
float h(float x) { return fract(sin(x*127.1+uSeed*1.97)*43758.5453); }
void main() {
    float id = float(gl_VertexID)+1.0;
    vec3 forward = normalize(-uCamera);
    vec3 right = normalize(cross(forward,vec3(0,1,0)));
    if (length(right)<0.01) right=vec3(1,0,0);
    vec3 up=normalize(cross(right,forward));
    float jitter=h(id*13.73);
    float r = 0.78 + pow(h(id*3.31),0.70)*1.9;
    float a = id*2.399963 + uSeconds*(0.55/pow(r,1.5)) + h(id*7.21);
    vec3 p = right*(cos(a)*r) + up*(sin(a)*r*0.27) + forward*(sin(a)*r*0.36);
    vOpacity = 0.56 + 0.44*h(id*2.41);
    vHeat = h(id*1.19);
    if (uStage < 0.5) {
        // Swarming planetesimals, gradually captured by growing proto-Earth.
        float growth=clamp(uProgress,0.0,1.0);
        p = right*(cos(a)*(1.1+2.0*h(id*3.31))) +
            up*(sin(a)*1.1)+forward*(sin(a*1.73)*0.9);
        p *= mix(1.32,0.66,growth);
        vHeat *= 0.22;
        if (h(id*3.51)>0.38) vOpacity=0.0;
    } else if (uStage < 1.5) {
        // A conical curtain launched from the grazing contact point.
        // Particle trajectories are illustrative, not integrated orbital
        // mechanics or SPH/pressure-driven ejecta.
        float t=clamp((uProgress-0.12)/0.84,0.0,1.0);
        float phi=id*2.399963 + jitter*3.0;
        vec3 direction=normalize(right*(0.4+0.6*h(id*3.3)) +
            up*(0.20+sin(phi)*0.70) + forward*(cos(phi)*0.43));
        float distance=(0.20+3.0*t)*(0.23+0.77*h(id*9.73));
        p = right*0.72+up*0.22+direction*distance-up*(t*t*0.28);
        vHeat = mix(0.3,1.0,h(id*3.73));
        vOpacity *= smoothstep(0.07,0.20,uProgress)*(1.0-t*0.40);
    } else {
        // The ejecta settles into a thick, differentially orbiting debris
        // field. Small point sprites provide real parallax over the hot disk.
        p += up*(h(id*5.2)-0.5)*0.18;
        vOpacity *= uStage>2.5 ? 0.15 : (0.90-0.52*uProgress);
        vHeat *= uStage>2.5 ? 0.15 : (0.90-0.55*uProgress);
    }
    vec3 delta=p-uCamera;
    float depth=dot(delta,forward);
    vec2 ndc=vec2(dot(delta,right)/(max(depth,0.01)*uTanHalfFov*uAspect),
        dot(delta,up)/(max(depth,0.01)*uTanHalfFov));
    gl_Position=vec4(ndc,0.2,1.0);
    float size=mix(1.0,4.5,h(id*5.11));
    gl_PointSize=min(9.0,size*uViewHeight/920.0);
    // Suppress particles behind Earth's projected silhouette.
    if (depth>length(uCamera) && length(cross(normalize(delta),forward))<0.99/length(uCamera))
        vOpacity*=0.10;
    if (depth<=0.0 || vOpacity<0.015) gl_PointSize=0.0;
}`;

const ejectaFragment = `#version 300 es
precision highp float;
in float vHeat;
in float vOpacity;
out vec4 outColor;
void main() {
    float r=length(gl_PointCoord-0.5)*2.0;
    if (r>1.0) discard;
    float alpha=pow(1.0-r,1.6)*0.74*vOpacity;
    vec3 color=mix(vec3(0.31,0.27,0.24),vec3(1.55,0.51,0.14),vHeat);
    outColor=vec4(color,alpha);
}`;

function linkUniforms(gl, program, names) {
    return Object.fromEntries(names.map(name => [name,gl.getUniformLocation(program,name)]));
}

export function renderEarthMoonFormation(formation, gl, createProgram, frame) {
    if (!formation.resources) {
        const scene = createProgram(gl,sceneVertex,sceneFragment);
        const ejecta = createProgram(gl,ejectaVertex,ejectaFragment);
        formation.resources = {
            scene, ejecta, vao:gl.createVertexArray(),
            sceneU:linkUniforms(gl,scene,['uCamera','uAspect','uTanHalfFov','uAge','uStage','uProgress','uSeconds','uSeed']),
            ejectaU:linkUniforms(gl,ejecta,['uCamera','uAspect','uTanHalfFov','uStage','uProgress','uSeconds','uSeed','uViewHeight']),
        };
    }
    const {scene,ejecta,vao,sceneU,ejectaU}=formation.resources;
    const status=earthMoonFormationStatus(formation);
    const stage = {accretion:0,impact:1,'moon-assembly':2,aftermath:3}[formation.stage];
    const aspect=frame.viewportWidth/Math.max(1,frame.viewportHeight);
    const tanHalfFov=Math.tan(Math.PI/8.4);
    const progress=status.progress;
    gl.bindVertexArray(vao);
    gl.disable(gl.DEPTH_TEST);
    gl.depthMask(false);
    gl.useProgram(scene);
    gl.uniform3fv(sceneU.uCamera,frame.camera);
    gl.uniform1f(sceneU.uAspect,aspect);
    gl.uniform1f(sceneU.uTanHalfFov,tanHalfFov);
    gl.uniform1f(sceneU.uAge,formation.ageYears/1e6);
    gl.uniform1f(sceneU.uStage,stage);
    gl.uniform1f(sceneU.uProgress,progress);
    gl.uniform1f(sceneU.uSeconds,formation.visualSeconds);
    gl.uniform1f(sceneU.uSeed,formation.seed);
    gl.drawArrays(gl.TRIANGLES,0,3);
    gl.useProgram(ejecta);
    gl.uniform3fv(ejectaU.uCamera,frame.camera);
    gl.uniform1f(ejectaU.uAspect,aspect);
    gl.uniform1f(ejectaU.uTanHalfFov,tanHalfFov);
    gl.uniform1f(ejectaU.uStage,stage);
    gl.uniform1f(ejectaU.uProgress,progress);
    gl.uniform1f(ejectaU.uSeconds,formation.visualSeconds);
    gl.uniform1f(ejectaU.uSeed,formation.seed);
    gl.uniform1f(ejectaU.uViewHeight,frame.viewportHeight);
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.SRC_ALPHA,gl.ONE);
    gl.drawArrays(gl.POINTS,0,2600);
    gl.disable(gl.BLEND);
    gl.depthMask(true);
    gl.bindVertexArray(null);
}

export function disposeEarthMoonFormation(formation, gl) {
    const r=formation?.resources;
    if (!r) return;
    gl.deleteProgram(r.scene);
    gl.deleteProgram(r.ejecta);
    gl.deleteVertexArray(r.vao);
    formation.resources=null;
}
