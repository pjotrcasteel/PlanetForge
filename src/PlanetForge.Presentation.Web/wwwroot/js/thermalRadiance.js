// Read-only optical projection: Planck spectral radiance + CIE 1931 observer.
// Sources: https://cie.co.at/eilvterm/17-24-005 and
// Wyman, Sloan & Shirley (2013), https://jcgt.org/published/0002/02/01/.
// This module cannot mutate temperature, phase, mass, enthalpy or terrain.
export const thermalTextureSize = 1024;
export const maximumDisplayKelvin = 6553.5;
const h = 6.62607015e-34;
const c = 299792458;
const k = 1.380649e-23;

export function spectralRadiance(wavelengthNanometers, kelvin) {
    if (!Number.isFinite(wavelengthNanometers) || wavelengthNanometers <= 0 || !Number.isFinite(kelvin) || kelvin < 0)
        throw new RangeError('Spectral radiance needs a positive wavelength and nonnegative finite Kelvin.');
    if (kelvin === 0) return 0;
    const wavelength = wavelengthNanometers * 1e-9;
    const exponent = h * c / (wavelength * k * kelvin);
    return exponent > 700 ? 0 : (2 * h * c * c) / (Math.pow(wavelength, 5) * Math.expm1(exponent));
}

function lobe(wavelength, center, left, right) {
    const t = (wavelength - center) * (wavelength < center ? left : right);
    return Math.exp(-0.5 * t * t);
}

// Piecewise Gaussian fits (Eq. 4), wavelengths in nanometres.
function observer(w) {
    return [
        0.362 * lobe(w, 442.0, 0.0624, 0.0374) + 1.056 * lobe(w, 599.8, 0.0264, 0.0323) - 0.065 * lobe(w, 501.1, 0.0490, 0.0382),
        0.821 * lobe(w, 568.8, 0.0213, 0.0247) + 0.286 * lobe(w, 530.9, 0.0613, 0.0322),
        1.217 * lobe(w, 437.0, 0.0845, 0.0278) + 0.681 * lobe(w, 459.0, 0.0385, 0.0725)
    ];
}

export function visibleTristimulus(kelvin) {
    const xyz = [0, 0, 0];
    for (let w = 380; w <= 780; w += 5) {
        const weight = w === 380 || w === 780 ? 0.5 : 1;
        const radiance = spectralRadiance(w, kelvin) * 5e-9 * weight;
        const response = observer(w);
        for (let i = 0; i < 3; i++) xyz[i] += radiance * response[i];
    }
    return xyz;
}

const referenceLuminance = visibleTristimulus(2200)[1];

export function emissionLinearRadiance(kelvin) {
    const [x, y, z] = visibleTristimulus(kelvin);
    return [3.2406 * x - 1.5372 * y - 0.4986 * z, -0.9689 * x + 1.8758 * y + 0.0415 * z, 0.0557 * x - 0.2040 * y + 1.0570 * z]
        .map(value => Math.max(0, value) / referenceLuminance);
}

export function emissionDisplayLinear(kelvin) {
    const rgb = emissionLinearRadiance(kelvin).map(value => value * 1.4 * 0.9);
    const peak = Math.max(...rgb);
    return rgb.map(value => value / (1 + peak));
}

// Log-radiance retains faint red incandescence below a linear RGBA8 lookup's first quantum.
// It contains optical radiance only: no authored hot patches or extra thermal state.
export function encodeRadiance(value) { return Math.round(Math.max(0, Math.min(1, (Math.log2(Math.max(value, 2 ** -40)) + 40) / 64)) * 255); }
export function decodeRadiance(byte) { return byte === 0 ? 0 : 2 ** (byte / 255 * 64 - 40); }

export function createThermalRadianceData() {
    const data = new Float32Array(thermalTextureSize * 4);
    for (let i = 0; i < thermalTextureSize; i++) {
        const rgb = emissionLinearRadiance(maximumDisplayKelvin * i / (thermalTextureSize - 1));
        for (let channel = 0; channel < 3; channel++) data[i * 4 + channel] = Math.max(0, Math.min(1, (Math.log2(Math.max(rgb[channel], 2 ** -40)) + 40) / 64));
        data[i * 4 + 3] = 1;
    }
    return data;
}

export const thermalMaterialShader = `
uniform sampler2D uThermalRadianceTexture;
vec4 thermalRadiance(float kelvin) {
    float coordinate = (0.5 + clamp(kelvin / ${maximumDisplayKelvin.toFixed(1)}, 0.0, 1.0) * ${thermalTextureSize - 1}.0) / ${thermalTextureSize}.0;
    vec3 encoded = texture(uThermalRadianceTexture, vec2(coordinate, 0.5)).rgb;
    vec3 radiance = exp2(encoded * 64.0 - 40.0) * step(vec3(0.5 / 255.0), encoded);
    return vec4(radiance, 1.0);
}
vec3 displaySrgb(vec3 linear) {
    linear = clamp(linear, 0.0, 1.0);
    return mix(12.92 * linear, 1.055 * pow(linear, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), linear));
}
vec3 phaseMaterial(vec2 phase, vec3 meanNormal, vec3 view, vec3 light, float solarFlux) {
    vec3 n = normalize(meanNormal);
    float melt = clamp(phase.y, 0.0, 1.0);
    float nv = max(dot(n, view), 0.0001);
    float nl = max(dot(n, light), 0.0);
    vec3 halfInput = light + view;
    vec3 halfDirection = length(halfInput) > 0.0001 ? normalize(halfInput) : n;
    float nh = max(dot(n, halfDirection), 0.0);
    float vh = max(dot(view, halfDirection), 0.0);
    float roughness = mix(0.78, 0.10, melt);
    float alpha = roughness * roughness;
    float denominator = nh * nh * (alpha * alpha - 1.0) + 1.0;
    float distribution = alpha * alpha / max(3.14159265 * denominator * denominator, 0.000001);
    float smithK = (roughness + 1.0) * (roughness + 1.0) / 8.0;
    float geometry = nv / (nv * (1.0 - smithK) + smithK) * nl / max(nl * (1.0 - smithK) + smithK, 0.0001);
    float fresnel = 0.04 + 0.96 * pow(1.0 - vh, 5.0);
    float specular = distribution * geometry * fresnel / max(4.0 * nv * nl, 0.0001);
    vec3 reflected = vec3((mix(0.13, 0.035, melt) / 3.14159265 + specular) * nl * max(solarFlux, 0.0) / 1361.0);
    // Opaque silicate optical hypothesis: Schlick angular emissivity for molten material.
    // 0.984375 normalizes its cosine-weighted hemispherical mean to the solver's grey epsilon=0.9.
    // Solid rock is rough/Lambertian. This changes angular optics, not physical heat or topography.
    float angleFresnel = 0.04 + 0.96 * pow(1.0 - nv, 5.0);
    float emissivity = mix(0.9, 0.984375 * (1.0 - angleFresnel), melt);
    vec3 linear = thermalRadiance(phase.x).rgb * emissivity + reflected;
    linear *= 1.4; // Fixed camera exposure at every simulation time; no temperature normalization.
    linear /= 1.0 + max(linear.r, max(linear.g, linear.b));
    return displaySrgb(linear);
}
`;
