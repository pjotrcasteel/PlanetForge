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
vec3 rockySurface(vec3 pos, vec3 norm, vec3 rd, float hot, float theia, float time) {
    vec3 p = norm * 4.5 + vec3(uSeed*0.011, 0.0, time*0.004);
    float geologic = fbm(p);
    float detail = fbm(p*6.3);
    float fracture = abs(fbm(p*2.1) - 0.48);
    float crust = smoothstep(0.15,0.70,geologic*0.76+detail*0.24);
    vec3 rock = mix(vec3(0.012,0.018,0.024),vec3(0.15,0.125,0.105),crust);
    rock = mix(rock,vec3(0.25,0.21,0.175),0.23*detail);
    // Young cooling proto-Earth may be patchily molten, but not covered by
    // coherent plate boundaries. Breakup is visually chaotic and multiscale.
    float lava = (1.0 - smoothstep(0.0,0.25,fracture)) * (0.40 + 0.60*detail);
    lava *= smoothstep(0.30, 0.62, 1.0 - geologic*0.48);
    vec3 diffuse = rock * (0.10 + 1.10*max(dot(norm,normalize(LIGHT)),0.0));
    vec3 halfDir = normalize(normalize(LIGHT) - rd);
    float spec = pow(max(dot(norm, halfDir),0.0),26.0) * (0.07+0.09*geologic);
    float thermalFraction = clamp(hot,0.0,1.0);
    vec3 deepMagma = mix(vec3(0.39,0.016,0.003), vec3(1.0,0.26,0.035),lava);
    vec3 incandescent = mix(deepMagma,vec3(2.7,1.14,0.38),pow(lava,4.0)*0.40);
    vec3 visible = mix(diffuse + vec3(spec), incandescent,
        thermalFraction*(0.66+0.34*lava));
    visible += incandescent*lava*thermalFraction*0.55;
    float fresnel = pow(1.0 - clamp(dot(norm,-rd),0.0,1.0),4.5);
    visible += fresnel*mix(vec3(0.028,0.055,0.10),vec3(0.44,0.10,0.025),thermalFraction);
    if (theia > 0.5) visible = diffuse*0.7 + vec3(spec*0.55) + lava*vec3(0.012,0.007,0.005);
    return visible;
}
vec3 limbGlow(vec3 ro, vec3 rd, vec3 center, float radius, vec3 color, float strength) {
    float along = max(0.0,dot(center-ro,rd));
    float impact = length(ro + rd*along - center);
    float d = max(0.0,impact-radius);
    float fade = exp(-d*d / max(0.0001,0.035*radius*radius));
    return color * strength * fade * clamp(impact/radius,0.0,1.0);
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
    float collision = step(1.0,uStage);
    if (uStage>1.5) earthRadius=1.0;
    vec3 earth = vec3(0.0);
    vec3 theia = right*(2.65-2.09*clamp(uProgress,0.0,1.0)) + up*0.12;
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
    // A thin analytic orbital debris sheet gives the post-impact orbit scale.
    if (uStage>1.5) {
        float diskOpacity = uStage>2.5 ? 0.18 : (1.0-uProgress*0.78);
        vec3 diskNormal = normalize(up + forward*0.35);
        float plane = dot(ray,diskNormal);
        if (abs(plane)>0.0001) {
            float distanceToPlane = -dot(camera,diskNormal)/plane;
            vec3 q = camera+ray*distanceToPlane;
            float r=length(q);
            float band = smoothstep(0.58,0.82,r)*(1.0-smoothstep(1.8,2.30,r));
            float dust=fbm(q*19.0+vec3(uSeconds*0.02));
            float density = band*(0.28+0.72*dust)*diskOpacity;
            if (distanceToPlane>0.0 && distanceToPlane<nearest)
                col += density*vec3(0.85,0.31,0.13)*0.35;
        }
    }
    if (body>=0.0) {
        vec3 center=body<0.5 ? earth : body<1.5 ? theia : moon;
        float radius=body<0.5 ? earthRadius : body<1.5 ? 0.49 : moonRadius;
        vec3 pos = camera+ray*nearest;
        vec3 norm = normalize(pos-center);
        float hot = body<0.5 ? mix(0.16,0.86,growth) : 0.0;
        if (uStage>0.5 && body<0.5)
            hot = mix(0.86,1.0,smoothstep(0.1,0.35,uProgress));
        if (body>1.5) hot = 0.03;
        col = rockySurface(pos,norm,ray,hot,body>0.5?1.0:0.0,uSeconds);
        float atmo = body<0.5 ? 0.17 : 0.0;
        col += pow(1.0-max(dot(norm,-ray),0.0),4.0)*atmo*vec3(0.32,0.20,0.15);
    }
    // Flash and expanding shock are cinematic approximations, never counted as
    // a physical explosion or as a second geological impact event.
    if (uStage>0.5 && uStage<1.5) {
        vec3 epicenter = right*0.62;
        float d=length(cross(ray,normalize(epicenter-camera)));
        float peak=exp(-pow((uProgress-0.30)/0.125,2.0));
        float glare = exp(-d*d/0.003)*peak;
        col+=glare*vec3(2.1,0.77,0.21);
        col+=exp(-d*d/0.035)*peak*vec3(0.40,0.09,0.012);
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
float h(float x) { return fract(sin(x*127.1+uSeed*1.97)*43758.5453); }
void main() {
    float id = float(gl_VertexID)+1.0;
    vec3 forward = normalize(-uCamera);
    vec3 right = normalize(cross(forward,vec3(0,1,0)));
    if (length(right)<0.01) right=vec3(1,0,0);
    vec3 up=normalize(cross(right,forward));
    float r = 0.70 + pow(h(id*3.31),0.65)*1.5;
    float a = id*2.399963 + uSeconds*(0.4/pow(r,1.5)) + h(id*7.21);
    vec3 p = right*(cos(a)*r)+up*(sin(a)*r*0.28)+forward*(sin(a)*r*0.7);
    if (uStage<0.5) {
        float grow = clamp(uProgress,0.0,1.0);
        p = right*(cos(a)*(1.2+1.7*h(id*3.31)))+
            up*(sin(a)*1.0)+forward*(sin(a*1.73)*1.0);
        p *= mix(1.3,0.6,grow);
    }
    vec3 delta=p-uCamera;
    float depth=dot(delta,forward);
    vec2 ndc=vec2(dot(delta,right)/(max(depth,0.01)*uTanHalfFov*uAspect),
        dot(delta,up)/(max(depth,0.01)*uTanHalfFov));
    gl_Position=vec4(ndc,0.2,1.0);
    float size = mix(1.4,4.0,h(id*5.11));
    gl_PointSize = min(10.0,size*uViewHeight/850.0);
    vHeat = h(id*1.19);
    if (depth<=0.0 || (uStage<0.5 && h(id*3.51) > 0.3)
        || (uStage>0.5 && uStage<1.5 && uProgress<0.15))
        gl_PointSize=0.0;
}`;

const ejectaFragment = `#version 300 es
precision highp float;
in float vHeat;
out vec4 outColor;
void main() {
    float r=length(gl_PointCoord-0.5)*2.0;
    if (r>1.0) discard;
    float alpha=pow(1.0-r,2.0)*0.68;
    vec3 color=mix(vec3(0.42,0.38,0.34),vec3(1.0,0.42,0.10),vHeat);
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
    gl.drawArrays(gl.POINTS,0,320);
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
