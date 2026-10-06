let state;

const verticalFieldOfViewRadians = Math.PI / 4.2;
const minimumCameraAltitudeMeters = 3.0;
const maximumCameraAltitudeRatio = 4.2;
const localTransitionAltitudeMeters = 20_000.0;
const localExitAltitudeMeters = 25_000.0;
const landReliefExaggeration = 28.0;
const visualNormalExaggeration = 30.0;

export function initialize(inputCanvasId, snapshot) {
    const inputCanvas = document.getElementById(inputCanvasId);
    const canvas = document.getElementById('coastal-relief-canvas');
    if (!inputCanvas || !canvas) return;

    const gl = canvas.getContext('webgl2', { antialias: true, alpha: true, premultipliedAlpha: false });
    if (!gl) return;

    const program = createProgram(gl);
    state = {
        canvas,
        inputCanvas,
        gl,
        program,
        yaw: -0.65,
        pitch: 0.24,
        distance: 3.15,
        dragging: false,
        lastX: 0,
        lastY: 0,
        planetRadiusMeters: 6_371_000.0,
        landIceFraction: 1.0,
        snowCoverFraction: 1.0,
        visible: true,
        tiles: new Map(),
        dirty: true,
        attributes: {
            position: gl.getAttribLocation(program, 'aPosition'),
            normal: gl.getAttribLocation(program, 'aNormal')
        },
        uniforms: {
            viewProjection: gl.getUniformLocation(program, 'uViewProjection'),
            lightDirection: gl.getUniformLocation(program, 'uLightDirection'),
            cameraPosition: gl.getUniformLocation(program, 'uCameraPosition'),
            planetRadiusMeters: gl.getUniformLocation(program, 'uPlanetRadiusMeters'),
            landIceFraction: gl.getUniformLocation(program, 'uLandIceFraction'),
            snowCoverFraction: gl.getUniformLocation(program, 'uSnowCoverFraction'),
            depthOnly: gl.getUniformLocation(program, 'uDepthOnly')
        }
    };

    installInput(state);
    setPlanet(snapshot);
    requestAnimationFrame(render);
}

export function setPlanet(snapshot) {
    if (!state || !snapshot) return;
    state.planetRadiusMeters = snapshot.physicalParameters?.radiusMeters ?? state.planetRadiusMeters;
    state.landIceFraction = snapshot.climateFeedback?.landIceFraction ?? snapshot.climateFeedback?.cryosphereFraction ?? state.landIceFraction;
    state.snowCoverFraction = snapshot.climateFeedback?.snowCoverFraction ?? snapshot.climateFeedback?.cryosphereFraction ?? state.snowCoverFraction;
    state.visible = !snapshot.localSurface;

    const surfaceTiles = snapshot.surfaceTiles ?? [];
    if (surfaceTiles.length > 0) {
        const activeKeys = new Set();
        for (const tile of surfaceTiles) {
            if (!tile?.key || !tile.surfaceVertexCount) continue;
            activeKeys.add(tile.key);
            if (!tile.positions?.length || !tile.normals?.length) continue;
            replaceTile(state, tile);
        }

        for (const [key, tile] of state.tiles) {
            if (activeKeys.has(key)) continue;
            deleteTileBuffers(state.gl, tile);
            state.tiles.delete(key);
        }
    }

    state.dirty = true;
}

export function dispose() {
    if (!state) return;
    for (const tile of state.tiles.values()) deleteTileBuffers(state.gl, tile);
    state.tiles.clear();
    state.gl.deleteProgram(state.program);
    state = null;
}

function replaceTile(s, tile) {
    const existing = s.tiles.get(tile.key);
    if (existing) deleteTileBuffers(s.gl, existing);

    const positionBuffer = s.gl.createBuffer();
    s.gl.bindBuffer(s.gl.ARRAY_BUFFER, positionBuffer);
    s.gl.bufferData(s.gl.ARRAY_BUFFER, new Float32Array(tile.positions), s.gl.STATIC_DRAW);

    const normalBuffer = s.gl.createBuffer();
    s.gl.bindBuffer(s.gl.ARRAY_BUFFER, normalBuffer);
    s.gl.bufferData(s.gl.ARRAY_BUFFER, new Float32Array(tile.normals), s.gl.STATIC_DRAW);

    s.tiles.set(tile.key, { positionBuffer, normalBuffer, vertexCount: tile.surfaceVertexCount });
}

function deleteTileBuffers(gl, tile) {
    gl.deleteBuffer(tile.positionBuffer);
    gl.deleteBuffer(tile.normalBuffer);
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
    const { gl, canvas, program } = s;
    gl.viewport(0, 0, canvas.width, canvas.height);
    gl.clearColor(0, 0, 0, 0);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    if (!s.visible || (s.distance - 1.0) * s.planetRadiusMeters <= localTransitionAltitudeMeters) return;

    const eye = orbitEye(s.yaw, s.pitch, s.distance);
    const projection = perspective(verticalFieldOfViewRadians, canvas.width / Math.max(canvas.height, 1), 0.002, 20.0);
    const viewProjection = multiply(projection, lookAt(eye, [0, 0, 0], [0, 1, 0]));

    gl.enable(gl.DEPTH_TEST);
    gl.depthMask(true);
    gl.enable(gl.CULL_FACE);
    gl.frontFace(gl.CCW);
    gl.cullFace(gl.BACK);
    gl.useProgram(program);
    gl.uniformMatrix4fv(s.uniforms.viewProjection, false, viewProjection);
    gl.uniform3f(s.uniforms.lightDirection, 0.72, 0.42, 0.55);
    gl.uniform3f(s.uniforms.cameraPosition, eye[0], eye[1], eye[2]);
    gl.uniform1f(s.uniforms.planetRadiusMeters, s.planetRadiusMeters);
    gl.uniform1f(s.uniforms.landIceFraction, s.landIceFraction);
    gl.uniform1f(s.uniforms.snowCoverFraction, s.snowCoverFraction);

    gl.disable(gl.BLEND);
    gl.colorMask(false, false, false, false);
    gl.uniform1i(s.uniforms.depthOnly, 1);
    drawTiles(s);

    gl.colorMask(true, true, true, true);
    gl.depthMask(false);
    gl.depthFunc(gl.LEQUAL);
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    gl.uniform1i(s.uniforms.depthOnly, 0);
    drawTiles(s);

    gl.disable(gl.BLEND);
    gl.depthMask(true);
    gl.depthFunc(gl.LESS);
}

function drawTiles(s) {
    const { gl } = s;
    for (const tile of s.tiles.values()) {
        gl.bindBuffer(gl.ARRAY_BUFFER, tile.positionBuffer);
        gl.enableVertexAttribArray(s.attributes.position);
        gl.vertexAttribPointer(s.attributes.position, 3, gl.FLOAT, false, 0, 0);
        gl.bindBuffer(gl.ARRAY_BUFFER, tile.normalBuffer);
        gl.enableVertexAttribArray(s.attributes.normal);
        gl.vertexAttribPointer(s.attributes.normal, 3, gl.FLOAT, false, 0, 0);
        gl.drawArrays(gl.TRIANGLES, 0, tile.vertexCount);
    }
}

function createProgram(gl) {
    const vertex = compile(gl, gl.VERTEX_SHADER, vertexShaderSource);
    const fragment = compile(gl, gl.FRAGMENT_SHADER, fragmentShaderSource);
    const program = gl.createProgram();
    gl.attachShader(program, vertex);
    gl.attachShader(program, fragment);
    gl.linkProgram(program);
    gl.deleteShader(vertex);
    gl.deleteShader(fragment);
    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(program));
    return program;
}

function compile(gl, type, source) {
    const shader = gl.createShader(type);
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(shader));
    return shader;
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
    const magnitude = Math.hypot(v[0], v[1], v[2]) || 1.0;
    return [v[0]/magnitude, v[1]/magnitude, v[2]/magnitude];
}

function cross(a,b) { return [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]; }
function dot(a,b) { return a[0]*b[0] + a[1]*b[1] + a[2]*b[2]; }
function clamp(value,min,max) { return Math.max(min, Math.min(max, value)); }

const vertexShaderSource = `#version 300 es
precision highp float;
in vec3 aPosition;
in vec3 aNormal;
uniform mat4 uViewProjection;
uniform float uPlanetRadiusMeters;
out vec3 vDirection;
out vec3 vVisualNormal;
out float vPhysicalSlope;
out float vElevationMeters;
void main() {
    float physicalRadius = length(aPosition);
    vec3 radial = normalize(aPosition);
    vec3 physicalNormal = normalize(aNormal);
    if (dot(physicalNormal, radial) < 0.0) physicalNormal = -physicalNormal;
    vec3 tangentNormal = physicalNormal - (radial * dot(physicalNormal, radial));
    vDirection = radial;
    vVisualNormal = normalize(radial + (tangentNormal * ${visualNormalExaggeration.toFixed(1)}));
    vPhysicalSlope = clamp(1.0 - dot(physicalNormal, radial), 0.0, 0.5);
    vElevationMeters = (physicalRadius - 1.0) * uPlanetRadiusMeters;
    float visualRadius = 1.0 + ((vElevationMeters * ${landReliefExaggeration.toFixed(1)}) / uPlanetRadiusMeters);
    gl_Position = uViewProjection * vec4(radial * visualRadius, 1.0);
}`;

const fragmentShaderSource = `#version 300 es
precision highp float;
in vec3 vDirection;
in vec3 vVisualNormal;
in float vPhysicalSlope;
in float vElevationMeters;
uniform vec3 uLightDirection;
uniform vec3 uCameraPosition;
uniform float uLandIceFraction;
uniform float uSnowCoverFraction;
uniform bool uDepthOnly;
out vec4 outColor;

float hash31(vec3 p) {
    p = fract(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}

float valueNoise(vec3 p) {
    vec3 cell = floor(p);
    vec3 local = fract(p);
    vec3 f = local * local * (3.0 - (2.0 * local));
    float n000 = hash31(cell + vec3(0.0));
    float n100 = hash31(cell + vec3(1.0,0.0,0.0));
    float n010 = hash31(cell + vec3(0.0,1.0,0.0));
    float n110 = hash31(cell + vec3(1.0,1.0,0.0));
    float n001 = hash31(cell + vec3(0.0,0.0,1.0));
    float n101 = hash31(cell + vec3(1.0,0.0,1.0));
    float n011 = hash31(cell + vec3(0.0,1.0,1.0));
    float n111 = hash31(cell + vec3(1.0));
    float nx00 = mix(n000,n100,f.x), nx10 = mix(n010,n110,f.x), nx01 = mix(n001,n101,f.x), nx11 = mix(n011,n111,f.x);
    return mix(mix(nx00,nx10,f.y),mix(nx01,nx11,f.y),f.z);
}

void main() {
    if (uDepthOnly) {
        outColor = vec4(0.0);
        return;
    }

    if (vElevationMeters < 0.0) discard;

    float elevation = vElevationMeters;
    vec3 radial = normalize(vDirection);
    vec3 normal = normalize(vVisualNormal);
    vec3 viewDirection = normalize(uCameraPosition - radial);
    if (dot(radial, viewDirection) <= 0.0) discard;

    float lowCoast = 1.0 - smoothstep(35.0, 185.0, elevation);
    float extendedCoast = 1.0 - smoothstep(160.0, 760.0, elevation);
    float slope = smoothstep(0.003, 0.026, vPhysicalSlope);
    float steep = smoothstep(0.014, 0.060, vPhysicalSlope);
    float cliff = smoothstep(0.030, 0.090, vPhysicalSlope);

    float macro = valueNoise((radial * 18.0) + vec3(4.0,-3.0,7.0));
    float detail = valueNoise((radial * 48.0) + vec3(-6.0,5.0,2.0));
    float breakup = clamp((macro * 0.72) + (detail * 0.28), 0.0, 1.0);

    float beach = lowCoast * (1.0 - steep) * smoothstep(0.30, 0.64, breakup);
    float rocky = lowCoast * smoothstep(0.18, 0.72, slope) * (1.0 - cliff * 0.55);
    float cliffCoast = extendedCoast * cliff;
    float coastalStrength = max(beach, max(rocky * 0.86, cliffCoast));

    float thaw = 1.0 - max(clamp(uLandIceFraction, 0.0, 1.0), clamp(uSnowCoverFraction, 0.0, 1.0));
    float exposure = smoothstep(0.08, 0.58, thaw);
    coastalStrength *= exposure;
    if (coastalStrength < 0.025) discard;

    vec3 beachColor = vec3(0.58, 0.50, 0.37);
    vec3 rockColor = vec3(0.30, 0.28, 0.25);
    vec3 cliffColor = vec3(0.18, 0.18, 0.17);
    vec3 cliffTop = vec3(0.46, 0.42, 0.34);

    vec3 material = beachColor;
    material = mix(material, rockColor, clamp(rocky, 0.0, 1.0));
    material = mix(material, cliffColor, clamp(cliffCoast, 0.0, 1.0));

    vec3 light = normalize(uLightDirection);
    float direct = max(dot(normal, light), 0.0);
    float radialDirect = max(dot(radial, light), 0.0);
    float faceDelta = clamp(direct - radialDirect, -0.45, 0.45);
    float cliffShadow = 1.0 - (max(-faceDelta, 0.0) * 0.85) - (cliffCoast * 0.20);
    float illumination = clamp(0.64 + (0.36 * direct), 0.42, 1.03) * cliffShadow;
    material *= illumination;

    float cliffHighlight = cliffCoast * smoothstep(0.20, 0.78, direct) * smoothstep(0.18, 0.62, breakup);
    material = mix(material, cliffTop, cliffHighlight * 0.42);

    float wetEdge = lowCoast * (1.0 - steep) * (1.0 - smoothstep(0.0, 72.0, elevation));
    material = mix(material, vec3(0.22, 0.27, 0.25), wetEdge * 0.30);

    float alpha = clamp((beach * 0.56) + (rocky * 0.62) + (cliffCoast * 0.88), 0.0, 0.92) * exposure;
    outColor = vec4(material, alpha);
}`;