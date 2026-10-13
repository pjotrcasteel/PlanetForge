// A fixed camera point-spread approximation, not atmosphere, steam, magma motion or a physical heat source.
// Bright display energy is redistributed into a normalized blur; it is not simply added twice.
export const opticalBloomVertexShader = `#version 300 es
precision highp float;
out vec2 uv;
void main() {
    vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
    uv = p;
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}`;

export const opticalBloomFragmentShader = `#version 300 es
precision highp float;
in vec2 uv;
uniform sampler2D uImage;
uniform vec2 uResolution;
out vec4 color;
vec3 bright(vec2 coordinate) {
    vec3 value = texture(uImage, coordinate).rgb;
    float peak = max(value.r, max(value.g, value.b));
    return value * max(peak - 0.45, 0.0) / max(peak, 0.0001);
}
void main() {
    vec2 pixel = 1.0 / uResolution;
    vec3 core = bright(uv);
    vec3 spread = core * 0.40;
    spread += (bright(uv + vec2(3.0, 0.0) * pixel) + bright(uv - vec2(3.0, 0.0) * pixel)
        + bright(uv + vec2(0.0, 3.0) * pixel) + bright(uv - vec2(0.0, 3.0) * pixel)) * 0.09;
    spread += (bright(uv + vec2(16.0, 0.0) * pixel) + bright(uv - vec2(16.0, 0.0) * pixel)
        + bright(uv + vec2(0.0, 16.0) * pixel) + bright(uv - vec2(0.0, 16.0) * pixel)) * 0.06;
    color = vec4(texture(uImage, uv).rgb + 0.18 * (spread - core), 1.0);
}`;

export function createOpticalBloom(gl, createProgram) {
    const program = createProgram(gl, opticalBloomVertexShader, opticalBloomFragmentShader);
    const texture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    return { program, texture, vao: gl.createVertexArray(), width: 0, height: 0,
        image: gl.getUniformLocation(program, 'uImage'), resolution: gl.getUniformLocation(program, 'uResolution') };
}

export function renderOpticalBloom(gl, canvas, optics) {
    gl.activeTexture(gl.TEXTURE3);
    gl.bindTexture(gl.TEXTURE_2D, optics.texture);
    if (optics.width !== canvas.width || optics.height !== canvas.height) {
        gl.copyTexImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, 0, 0, canvas.width, canvas.height, 0);
        optics.width = canvas.width; optics.height = canvas.height;
    } else gl.copyTexSubImage2D(gl.TEXTURE_2D, 0, 0, 0, 0, 0, canvas.width, canvas.height);
    gl.disable(gl.DEPTH_TEST); gl.disable(gl.BLEND); gl.disable(gl.CULL_FACE);
    gl.useProgram(optics.program);
    gl.uniform1i(optics.image, 3);
    gl.uniform2f(optics.resolution, canvas.width, canvas.height);
    gl.bindVertexArray(optics.vao);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    gl.bindVertexArray(null);
    gl.enable(gl.DEPTH_TEST); gl.enable(gl.CULL_FACE);
}

export function disposeOpticalBloom(gl, optics) {
    if (!optics) return;
    gl.deleteTexture(optics.texture); gl.deleteProgram(optics.program); gl.deleteVertexArray(optics.vao);
}
