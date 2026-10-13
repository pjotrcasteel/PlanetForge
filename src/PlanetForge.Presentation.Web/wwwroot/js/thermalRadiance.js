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

export function emissionDisplayLinear(kelvin) {
    const [x, y, z] = visibleTristimulus(kelvin);
    // XYZ -> linear sRGB (D65 primaries), no chromatic adaptation of the emitter.
    const rgb = [3.2406 * x - 1.5372 * y - 0.4986 * z, -0.9689 * x + 1.8758 * y + 0.0415 * z, 0.0557 * x - 0.2040 * y + 1.0570 * z];
    // One fixed display exposure for every epoch and cell. No per-frame or
    // per-temperature normalization: cooled rock really loses visible radiance.
    return rgb.map(value => 1 - Math.exp(-Math.max(0, value) * 0.9 * 3 / referenceLuminance));
}

export function createThermalRadianceData() {
    const data = new Uint8Array(thermalTextureSize * 4);
    for (let i = 0; i < thermalTextureSize; i++) {
        const rgb = emissionDisplayLinear(maximumDisplayKelvin * i / (thermalTextureSize - 1));
        for (let channel = 0; channel < 3; channel++) data[i * 4 + channel] = Math.round(rgb[channel] * 255);
        data[i * 4 + 3] = Math.round(Math.max(...rgb) * 255);
    }
    return data;
}

export const thermalMaterialShader = `
uniform sampler2D uThermalRadianceTexture;
vec4 thermalRadiance(float kelvin) {
    float coordinate = (0.5 + clamp(kelvin / ${maximumDisplayKelvin.toFixed(1)}, 0.0, 1.0) * ${thermalTextureSize - 1}.0) / ${thermalTextureSize}.0;
    return texture(uThermalRadianceTexture, vec2(coordinate, 0.5));
}
vec3 displaySrgb(vec3 linear) {
    linear = clamp(linear, 0.0, 1.0);
    return mix(12.92 * linear, 1.055 * pow(linear, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), linear));
}
vec3 phaseMaterial(vec2 phase, vec3 meanNormal, vec3 view, vec3 light, float solarFlux) {
    float melt = clamp(phase.y, 0.0, 1.0);
    float direct = max(dot(meanNormal, light), 0.0);
    vec3 halfInput = light + view;
    float specular = length(halfInput) > 0.0001 ? pow(max(dot(meanNormal, normalize(halfInput)), 0.0), mix(12.0, 180.0, melt)) : 0.0;
    // Explicit neutral optical reference. Phase controls reflectance/roughness;
    // BOTH solid and liquid rock emit at their actual Kelvin. No old relief tint.
    vec3 reflected = vec3(mix(0.024, 0.008, melt) * direct + specular * mix(0.002, 0.022, melt));
    reflected *= max(solarFlux, 0.0) / 1361.0;
    return displaySrgb(thermalRadiance(phase.x).rgb + reflected);
}
`;
