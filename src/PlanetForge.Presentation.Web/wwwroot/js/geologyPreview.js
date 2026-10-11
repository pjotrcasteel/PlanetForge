// Research-only visualization of REAL C# crust/mantle column outputs.
// No invented tectonic shapes, height exaggeration or canonical terrain edits.
// Equal world coordinates yield the same interpolation regardless of LOD.
const WIDTH = 256;
const HEIGHT = 128;
const RADIUS = 0.16;
const MIN_DOT = Math.cos(RADIUS);
const BACKGROUND_WEIGHT = 0.35;
const neutral = [29, 40, 47];
const cold = [66, 164, 207];
const warm = [233, 159, 90];

function clamp(v, lo, hi) { return Math.max(lo, Math.min(hi, v)); }

function drawMap(id, values, maxAbs) {
    const canvas = document.getElementById(id);
    if (!canvas) throw new Error('Geology preview canvas missing: ' + id);
    canvas.width = WIDTH;
    canvas.height = HEIGHT;
    const ctx = canvas.getContext('2d', { alpha: false });
    if (!ctx) throw new Error('Geology preview needs 2D canvas support.');
    const image = ctx.createImageData(WIDTH, HEIGHT);
    const scale = maxAbs > 1e-9 ? maxAbs : 1;
    for (let i = 0; i < values.length; i++) {
        const v = values[i];
        const intensity = Math.pow(clamp(Math.abs(v) / scale, 0, 1), 0.65);
        const endpoint = v < 0 ? cold : warm;
        const offset = i * 4;
        for (let ch = 0; ch < 3; ch++) image.data[offset + ch] =
            Math.round(neutral[ch] * (1 - intensity) + endpoint[ch] * intensity);
        image.data[offset + 3] = 255;
    }
    ctx.putImageData(image, 0, 0);
}

export function drawGeologyPreview(material) {
    if (!Array.isArray(material) || material.length !== 1024) {
        throw new Error('A complete, canonical 1024-parcel physical snapshot is required.');
    }
    for (const c of material) {
        if (![c.x, c.y, c.z, c.upliftMeters, c.thicknessChangeMeters].every(Number.isFinite)) {
            throw new Error('Invalid nonfinite physical geology column.');
        }
    }
    const uplift = new Float64Array(WIDTH * HEIGHT);
    const thickness = new Float64Array(WIDTH * HEIGHT);
    let maxAbsUpliftMeters = 0;
    let maxAbsThicknessMeters = 0;

    // Normalized compact spherical kernels match the research isostatic-field
    // sampling contract (not a renderer-only fake mountain effect).
    for (let y = 0; y < HEIGHT; y++) {
        const latitude = Math.PI * (0.5 - (y + 0.5) / HEIGHT);
        const north = Math.sin(latitude);
        const parallel = Math.cos(latitude);
        for (let x = 0; x < WIDTH; x++) {
            const longitude = Math.PI * 2 * (x + 0.5) / WIDTH;
            const px = parallel * Math.cos(longitude);
            const pz = parallel * Math.sin(longitude);
            let sumUplift = 0, sumThickness = 0, weight = BACKGROUND_WEIGHT;
            for (let index = 0; index < material.length; index++) {
                const c = material[index];
                const dot = px * c.x + north * c.y + pz * c.z;
                if (dot <= MIN_DOT) continue;
                const t = clamp((dot - MIN_DOT) / (1 - MIN_DOT), 0, 1);
                const influence = t * t * (3 - 2 * t);
                sumUplift += influence * c.upliftMeters;
                sumThickness += influence * c.thicknessChangeMeters;
                weight += influence;
            }
            const position = y * WIDTH + x;
            uplift[position] = sumUplift / weight;
            thickness[position] = sumThickness / weight;
            maxAbsUpliftMeters = Math.max(maxAbsUpliftMeters, Math.abs(uplift[position]));
            maxAbsThicknessMeters = Math.max(maxAbsThicknessMeters, Math.abs(thickness[position]));
        }
    }

    drawMap('geology-uplift', uplift, maxAbsUpliftMeters);
    drawMap('geology-thickness', thickness, maxAbsThicknessMeters);
    window.__planetForgeGeologyPreviewReady = true;
    window.__planetForgeGeologyPreviewStats = { maxAbsUpliftMeters, maxAbsThicknessMeters };
    return { maxAbsUpliftMeters, maxAbsThicknessMeters };
}
