const clamp = (value, low, high) => Math.min(high, Math.max(low, value));
const mix = (a, b, weight) => a + (b - a) * weight;
const blend = (a, b, weight) => a.map((channel, index) => mix(channel, b[index], weight));

function colorForElevation(meters) {
    if (meters < -3000) return [24, 38, 55];
    if (meters < 0) return blend([38, 66, 74], [95, 115, 110], clamp((meters + 3000) / 3000, 0, 1));
    if (meters < 900) return blend([147, 114, 89], [178, 139, 102], meters / 900);
    if (meters < 2700) return blend([178, 139, 102], [128, 102, 83], (meters - 900) / 1800);
    return blend([128, 102, 83], [215, 203, 182], clamp((meters - 2700) / 4200, 0, 1));
}

function drawLayer(id, width, height, values, kind, elevations) {
    const canvas = document.getElementById(id);
    if (!canvas) throw new Error(`Missing terrain lab canvas: ${id}`);
    canvas.width = width;
    canvas.height = height;
    const context = canvas.getContext('2d', { alpha: false });
    if (!context) throw new Error('Terrain lab requires a Canvas 2D renderer.');
    const pixels = context.createImageData(width, height);

    for (let y = 0; y < height; y++) {
        for (let x = 0; x < width; x++) {
            const i = y * width + x;
            const value = values[i];
            let color;
            if (kind === 'crust') {
                const sea = [38, 60, 74];
                const land = [193, 169, 131];
                color = blend(sea, land, clamp(0.50 + (value * 2.0), 0, 1));
            } else if (kind === 'tectonic') {
                color = blend([29, 38, 49], [234, 151, 79], clamp(value * 7.0 + 0.05, 0, 1));
            } else if (kind === 'mountains') {
                color = blend([40, 50, 49], [226, 215, 177], clamp(value, 0, 1));
            } else if (kind === 'flow') {
                const drainage = clamp(Math.log1p(Math.max(0, value)) / 8.0, 0, 1);
                color = blend([24, 39, 39], [104, 230, 228], Math.pow(drainage, 1.5));
            } else if (kind === 'incision') {
                color = blend([34, 38, 41], [237, 150, 80], Math.sqrt(clamp(value / 180.0, 0, 1)));
            } else if (kind === 'sediment') {
                color = blend([37, 42, 44], [239, 201, 126], Math.sqrt(clamp(value / 80.0, 0, 1)));
            } else {
                color = colorForElevation(value);
                const west = elevations[y * width + (kind === 'regional-elevation' ? Math.max(0, x - 1) : (x + width - 1) % width)];
                const east = elevations[y * width + (kind === 'regional-elevation' ? Math.min(width - 1, x + 1) : (x + 1) % width)];
                const north = elevations[Math.max(0, y - 1) * width + x];
                const south = elevations[Math.min(height - 1, y + 1) * width + x];
                const slopeX = clamp((east - west) / 4000, -1, 1);
                const slopeY = clamp((south - north) / 4000, -1, 1);
                const hillshade = clamp(0.80 - (slopeX * 0.38) - (slopeY * 0.29), 0.38, 1.25);
                color = color.map(channel => channel * hillshade);
            }

            const offset = i * 4;
            pixels.data[offset] = clamp(Math.round(color[0]), 0, 255);
            pixels.data[offset + 1] = clamp(Math.round(color[1]), 0, 255);
            pixels.data[offset + 2] = clamp(Math.round(color[2]), 0, 255);
            pixels.data[offset + 3] = 255;
        }
    }

    context.putImageData(pixels, 0, 0);
}

export function drawTerrainLab(width, height, crust, tectonic, mountains, elevation) {
    drawLayer('lab-crust', width, height, crust, 'crust', elevation);
    drawLayer('lab-tectonic', width, height, tectonic, 'tectonic', elevation);
    drawLayer('lab-mountains', width, height, mountains, 'mountains', elevation);
    drawLayer('lab-elevation', width, height, elevation, 'elevation', elevation);
    window.__planetForgeRegionalWatershedReady = false;
    window.__planetForgeTerrainLabReady = true;
}


function drawElevationDifference(width, height, original, evolved) {
    const canvas = document.getElementById('lab-regional-difference');
    if (!canvas) throw new Error('Missing erosion difference canvas.');
    canvas.width = width;
    canvas.height = height;
    const context = canvas.getContext('2d', { alpha: false });
    if (!context) throw new Error('Erosion difference requires Canvas 2D.');
    const pixels = context.createImageData(width, height);

    for (let i = 0; i < original.length; i++) {
        const differenceMeters = original[i] - evolved[i];
        const magnitude = Math.sqrt(clamp(Math.abs(differenceMeters) / 120, 0, 1));
        const color = differenceMeters >= 0
            ? blend([30, 40, 43], [239, 143, 73], magnitude)
            : blend([30, 40, 43], [84, 217, 207], magnitude);
        const offset = i * 4;
        pixels.data[offset] = Math.round(color[0]);
        pixels.data[offset + 1] = Math.round(color[1]);
        pixels.data[offset + 2] = Math.round(color[2]);
        pixels.data[offset + 3] = 255;
    }

    context.putImageData(pixels, 0, 0);
}

export function drawRegionalWatershed(width, height, bedrock, accumulation, incision, sediment, evolved, original = bedrock) {
    drawLayer('lab-regional-bedrock', width, height, bedrock, 'regional-elevation', bedrock);
    drawLayer('lab-regional-flow', width, height, accumulation, 'flow', bedrock);
    drawLayer('lab-regional-incision', width, height, incision, 'incision', bedrock);
    drawLayer('lab-regional-sediment', width, height, sediment, 'sediment', bedrock);
    drawLayer('lab-regional-evolved', width, height, evolved, 'regional-elevation', evolved);
    drawElevationDifference(width, height, original, evolved);
    window.__planetForgeRegionalWatershedReady = true;
}
