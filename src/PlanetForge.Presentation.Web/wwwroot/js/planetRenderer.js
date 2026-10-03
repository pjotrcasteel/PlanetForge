let state;

export function initialize(canvasId, snapshot) {
    const canvas = document.getElementById(canvasId);
    const gl = canvas?.getContext('webgl2', { antialias: true, alpha: true });
    if (!canvas || !gl) throw new Error('PlanetForge requires WebGL 2.');

    state = createState(canvas, gl);
    installInput(state);
    setPlanet(snapshot);
    requestAnimationFrame(render);
}

export function setPlanet(snapshot) {
    if (!state) return;
    state.seaLevel = snapshot.seaLevel;
    state.atmosphereDensity = snapshot.atmosphereDensity;
    state.surfaceTemperature = snapshot.climate.surfaceTemperatureKelvin;
    state.solarFlux = snapshot.physics.solarFluxWattsPerSquareMeter;
    state.iceFraction = snapshot.water.iceFraction;
    state.liquidFraction = snapshot.water.liquidFraction;
    state.vaporFraction = snapshot.water.vaporFraction;
    state.pressurePascals = snapshot.atmosphere.surfacePressurePascals;
    uploadMesh(state, snapshot.mesh.positions, snapshot.mesh.normals);
}

function createState(canvas, gl) {
    const program = createProgram(gl, vertexShaderSource, fragmentShaderSource);
    return {
        canvas, gl, program, vertexCount: 0, yaw: -0.65, pitch: 0.24, distance: 3.15,
        dragging: false, lastX: 0, lastY: 0, seaLevel: -0.004, atmosphereDensity: 0.62,
        surfaceTemperature: 288, solarFlux: 1361, iceFraction: 0, liquidFraction: 1, vaporFraction: 0, pressurePascals: 101325,
        positionBuffer: gl.createBuffer(), normalBuffer: gl.createBuffer(),
        attributes: {
            position: gl.getAttribLocation(program, 'aPosition'),
            normal: gl.getAttribLocation(program, 'aNormal')
        },
        uniforms: {
            model: gl.getUniformLocation(program, 'uModel'),
            viewProjection: gl.getUniformLocation(program, 'uViewProjection'),
            light: gl.getUniformLocation(program, 'uLightDirection'),
            seaLevel: gl.getUniformLocation(program, 'uSeaLevel'),
            atmosphere: gl.getUniformLocation(program, 'uAtmosphere'),
            surfaceTemperature: gl.getUniformLocation(program, 'uSurfaceTemperature'),
            solarFlux: gl.getUniformLocation(program, 'uSolarFlux'),
            iceFraction: gl.getUniformLocation(program, 'uIceFraction'),
            liquidFraction: gl.getUniformLocation(program, 'uLiquidFraction'),
            vaporFraction: gl.getUniformLocation(program, 'uVaporFraction'),
            pressure: gl.getUniformLocation(program, 'uPressure'),
            mode: gl.getUniformLocation(program, 'uMode')
        }
    };
}

function uploadMesh(s, positions, normals) {
    const gl = s.gl;
    gl.bindBuffer(gl.ARRAY_BUFFER, s.positionBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(positions), gl.STATIC_DRAW);
    gl.bindBuffer(gl.ARRAY_BUFFER, s.normalBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(normals), gl.STATIC_DRAW);
    s.vertexCount = positions.length / 3;
}

function installInput(s) {
    const canvas = s.canvas;
    canvas.addEventListener('pointerdown', event => {
        s.dragging = true;
        s.lastX = event.clientX;
        s.lastY = event.clientY;
        canvas.setPointerCapture(event.pointerId);
    });
    canvas.addEventListener('pointerup', event => {
        s.dragging = false;
        canvas.releasePointerCapture(event.pointerId);
    });
    canvas.addEventListener('pointercancel', () => { s.dragging = false; });
    canvas.addEventListener('pointermove', event => {
        if (!s.dragging) return;
        s.yaw += (event.clientX - s.lastX) * 0.008;
        s.pitch = clamp(s.pitch + (event.clientY - s.lastY) * 0.008, -1.25, 1.25);
        s.lastX = event.clientX;
        s.lastY = event.clientY;
    });
    canvas.addEventListener('wheel', event => {
        event.preventDefault();
        s.distance = clamp(s.distance + event.deltaY * 0.002, 2.05, 5.2);
    }, { passive: false });
}

function render(time) {
    if (!state) return;
    resize(state);
    const { gl, canvas, program } = state;
    gl.viewport(0, 0, canvas.width, canvas.height);
    gl.enable(gl.DEPTH_TEST);
    gl.enable(gl.CULL_FACE);
    gl.clearColor(0, 0, 0, 0);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    gl.useProgram(program);

    bindAttributes(state);
    const aspect = canvas.width / Math.max(canvas.height, 1);
    const projection = perspective(Math.PI / 4.2, aspect, 0.1, 20);
    const eye = orbitEye(state.yaw, state.pitch, state.distance);
    const view = lookAt(eye, [0, 0, 0], [0, 1, 0]);
    const viewProjection = multiply(projection, view);
    const model = rotationY(time * 0.000045);

    gl.uniformMatrix4fv(state.uniforms.model, false, model);
    gl.uniformMatrix4fv(state.uniforms.viewProjection, false, viewProjection);
    gl.uniform3f(state.uniforms.light, 0.7, 0.35, 0.6);
    gl.uniform1f(state.uniforms.seaLevel, state.seaLevel);
    gl.uniform1f(state.uniforms.atmosphere, state.atmosphereDensity);
    gl.uniform1f(state.uniforms.surfaceTemperature, state.surfaceTemperature);
    gl.uniform1f(state.uniforms.solarFlux, state.solarFlux);
    gl.uniform1f(state.uniforms.iceFraction, state.iceFraction);
    gl.uniform1f(state.uniforms.liquidFraction, state.liquidFraction);
    gl.uniform1f(state.uniforms.vaporFraction, state.vaporFraction);
    gl.uniform1f(state.uniforms.pressure, state.pressurePascals);
    gl.uniform1i(state.uniforms.mode, 0);
    gl.drawArrays(gl.TRIANGLES, 0, state.vertexCount);

    if (state.atmosphereDensity > 0.001) {
        gl.enable(gl.BLEND);
        gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
        gl.disable(gl.CULL_FACE);
        gl.uniform1i(state.uniforms.mode, 1);
        gl.uniformMatrix4fv(state.uniforms.model, false, scaleMatrix(1.065));
        gl.drawArrays(gl.TRIANGLES, 0, state.vertexCount);
        gl.disable(gl.BLEND);
    }

    requestAnimationFrame(render);
}

function bindAttributes(s) {
    const gl = s.gl;
    gl.bindBuffer(gl.ARRAY_BUFFER, s.positionBuffer);
    gl.enableVertexAttribArray(s.attributes.position);
    gl.vertexAttribPointer(s.attributes.position, 3, gl.FLOAT, false, 0, 0);
    gl.bindBuffer(gl.ARRAY_BUFFER, s.normalBuffer);
    gl.enableVertexAttribArray(s.attributes.normal);
    gl.vertexAttribPointer(s.attributes.normal, 3, gl.FLOAT, false, 0, 0);
}

function resize(s) {
    const ratio = Math.min(window.devicePixelRatio || 1, 2);
    const width = Math.floor(s.canvas.clientWidth * ratio);
    const height = Math.floor(s.canvas.clientHeight * ratio);
    if (s.canvas.width !== width || s.canvas.height !== height) {
        s.canvas.width = width;
        s.canvas.height = height;
    }
}

function createProgram(gl, vertexSource, fragmentSource) {
    const vertex = compile(gl, gl.VERTEX_SHADER, vertexSource);
    const fragment = compile(gl, gl.FRAGMENT_SHADER, fragmentSource);
    const program = gl.createProgram();
    gl.attachShader(program, vertex);
    gl.attachShader(program, fragment);
    gl.linkProgram(program);
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
    for (let column=0; column<4; column++) for (let row=0; row<4; row++) out[column*4+row] = a[row]*b[column*4] + a[4+row]*b[column*4+1] + a[8+row]*b[column*4+2] + a[12+row]*b[column*4+3];
    return out;
}

function rotationY(angle) { const c=Math.cos(angle), s=Math.sin(angle); return new Float32Array([c,0,-s,0, 0,1,0,0, s,0,c,0, 0,0,0,1]); }
function scaleMatrix(s) { return new Float32Array([s,0,0,0, 0,s,0,0, 0,0,s,0, 0,0,0,1]); }
function normalize(v) { const l=Math.hypot(v[0],v[1],v[2])||1; return [v[0]/l,v[1]/l,v[2]/l]; }
function cross(a,b) { return [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]; }
function dot(a,b) { return a[0]*b[0]+a[1]*b[1]+a[2]*b[2]; }
function clamp(value,min,max) { return Math.max(min, Math.min(max, value)); }

const vertexShaderSource = `#version 300 es
precision highp float;
in vec3 aPosition;
in vec3 aNormal;
uniform mat4 uModel;
uniform mat4 uViewProjection;
out vec3 vNormal;
out vec3 vWorldPosition;
void main() {
    vec4 world = uModel * vec4(aPosition, 1.0);
    vWorldPosition = world.xyz;
    vNormal = normalize(mat3(uModel) * aNormal);
    gl_Position = uViewProjection * world;
}`;

const fragmentShaderSource = `#version 300 es
precision highp float;
in vec3 vNormal;
in vec3 vWorldPosition;
uniform vec3 uLightDirection;
uniform float uSeaLevel;
uniform float uAtmosphere;
uniform float uSurfaceTemperature;
uniform float uSolarFlux;
uniform float uIceFraction;
uniform float uLiquidFraction;
uniform float uVaporFraction;
uniform float uPressure;
uniform int uMode;
out vec4 outColor;
void main() {
    if (uMode == 1) {
        float rim = pow(1.0 - abs(dot(normalize(vNormal), normalize(-vWorldPosition))), 2.2);
        float fluxGlow = clamp(sqrt(max(uSolarFlux, 1.0) / 1361.0), 0.65, 1.35);
        float pressureGlow = clamp(sqrt(max(uPressure, 0.0) / 101325.0), 0.0, 1.6);
        vec3 dryAtmosphere = vec3(0.20, 0.72, 0.78);
        vec3 wetAtmosphere = vec3(0.66, 0.78, 0.82);
        vec3 atmosphereColor = mix(dryAtmosphere, wetAtmosphere, clamp(uVaporFraction, 0.0, 1.0));
        outColor = vec4(atmosphereColor, rim * 0.24 * uAtmosphere * fluxGlow * pressureGlow);
        return;
    }

    float radius = length(vWorldPosition);
    float elevation = radius - 1.0;
    float latitude = abs(normalize(vWorldPosition).y);
    vec3 deepOcean = vec3(0.025, 0.12, 0.22);
    vec3 shallowOcean = vec3(0.05, 0.30, 0.38);
    vec3 lowland = vec3(0.18, 0.38, 0.22);
    vec3 highland = vec3(0.39, 0.36, 0.22);
    vec3 peak = vec3(0.62, 0.65, 0.59);
    vec3 baseColor;

    if (elevation < uSeaLevel - 0.012 && uLiquidFraction > 0.001) baseColor = deepOcean;
    else if (elevation < uSeaLevel && uLiquidFraction > 0.001) baseColor = shallowOcean;
    else if (elevation < 0.018) baseColor = lowland;
    else if (elevation < 0.038) baseColor = highland;
    else baseColor = peak;

    float polarIce = smoothstep(0.40, 0.94, latitude);
    float globalIce = clamp(uIceFraction, 0.0, 1.0);
    float frost = clamp(globalIce * (0.40 + 0.85 * polarIce), 0.0, 1.0);
    baseColor = mix(baseColor, vec3(0.78, 0.90, 0.93), frost * 0.94);

    float heat = smoothstep(320.0, 430.0, uSurfaceTemperature);
    if (elevation >= uSeaLevel || uLiquidFraction <= 0.001) baseColor = mix(baseColor, vec3(0.52, 0.25, 0.10), heat * 0.78);

    float desiccation = clamp(uVaporFraction, 0.0, 1.0);
    baseColor = mix(baseColor, vec3(0.45, 0.30, 0.17), desiccation * 0.45);

    float light = max(dot(normalize(vNormal), normalize(uLightDirection)), 0.0);
    float fluxFactor = clamp(sqrt(max(uSolarFlux, 1.0) / 1361.0), 0.45, 1.55);
    float ambient = 0.14 + 0.04 * fluxFactor;
    float terminator = smoothstep(-0.12, 0.18, light);
    vec3 color = baseColor * (ambient + (0.78 + 0.16 * fluxFactor) * terminator);
    outColor = vec4(color, 1.0);
}`;
