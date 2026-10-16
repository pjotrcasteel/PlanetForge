// PF-PERF-01: opt-in, bounded CPU-side profiling. This module does NOT request
// extra GPU readbacks, alter the simulation clock, or send telemetry anywhere.
const MAX_RECENT_SAMPLES = 512;
let active = null;

export function configurePerformanceMetrics(enabled) {
    active = enabled ? { startedAt: performance.now(), durations: new Map(), counters: new Map() } : null;
}

export function performanceMarkStart() {
    return active === null ? null : performance.now();
}

export function recordPerformanceDuration(name, startedAt) {
    if (active === null || startedAt === null) return;
    recordPerformanceSample(name, performance.now() - startedAt);
}

export function recordPerformanceSample(name, value) {
    if (active === null || !Number.isFinite(value) || value < 0) return;
    let entry = active.durations.get(name);
    if (!entry) {
        entry = { count: 0, sum: 0, max: 0, samples: new Float64Array(MAX_RECENT_SAMPLES) };
        active.durations.set(name, entry);
    }
    const index = entry.count % MAX_RECENT_SAMPLES;
    entry.samples[index] = value;
    entry.count++;
    entry.sum += value;
    entry.max = Math.max(entry.max, value);
}

export function recordPerformanceReadback(bytes) {
    if (active === null || !Number.isSafeInteger(bytes) || bytes < 0) return;
    const entry = active.counters.get('evolutionFloatReadback') ?? { count: 0, bytes: 0 };
    entry.count++;
    entry.bytes += bytes;
    active.counters.set('evolutionFloatReadback', entry);
}

export function snapshotPerformanceMetrics() {
    if (active === null) return null;
    const durations = {};
    for (const [name, entry] of active.durations) {
        const count = Math.min(entry.count, MAX_RECENT_SAMPLES);
        const sample = Array.from(entry.samples.subarray(0, count)).sort((a, b) => a - b);
        const percentile = p => sample[Math.max(0, Math.ceil(p * count) - 1)] ?? null;
        durations[name] = {
            count: entry.count,
            sampleCount: count,
            averageMs: entry.sum / entry.count,
            p50Ms: percentile(0.5),
            p95Ms: percentile(0.95),
            p99Ms: percentile(0.99),
            maxMs: entry.max,
        };
    }
    const readbacks = {};
    for (const [name, entry] of active.counters) readbacks[name] = { count: entry.count, bytes: entry.bytes };
    return {
        schemaVersion: 1,
        enabled: true,
        elapsedMs: performance.now() - active.startedAt,
        percentileWindow: MAX_RECENT_SAMPLES,
        durations,
        readbacks,
        // All timings above are CPU wall-clock durations, including synchronous
        // GPU stalls. They are NOT independent GPU execution timings.
        measurementType: 'cpu-wall-and-gpu-readback-sync',
    };
}
