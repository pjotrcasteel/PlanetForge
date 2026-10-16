// Formats a GitHub-visible, machine-readable summary. Full samples remain in
// the versioned workflow artifact; do not store user identifiers or geolocation.
export const REPORT_MARKER = '<!-- PLANETFORGE_AUTOMATED_PERFORMANCE_BASELINE_V1 -->';

export function metric(report, key, field = 'p95Ms') {
  const n = report?.durations?.[key]?.[field];
  return typeof n === 'number' && Number.isFinite(n) ? Number(n.toFixed(2)) : null;
}

export function summarizeBaseline(bundle, runUrl) {
  if (!Array.isArray(bundle?.measurements) || bundle.measurements.length === 0) throw new Error('No performance measurements to publish');
  const rows = bundle.measurements.map(m => ({
    profile: m.id,
    status: m.status,
    actualIphoneSafari: m.actualIphoneSafari === true,
    build: m.buildBadge ?? null,
    epoch: m.report?.geologyRegime ?? null,
    frameP50Ms: metric(m.report, 'visibleFrameIntervalMs', 'p50Ms'),
    frameP95Ms: metric(m.report, 'visibleFrameIntervalMs'),
    stepP95Ms: metric(m.report, 'evolutionStepCpuMs'),
    sceneSubmissionP95Ms: metric(m.report, 'sceneDrawCpuSubmissionMs'),
    bloomSubmissionP95Ms: metric(m.report, 'bloomCpuSubmissionMs'),
    readbackWaitP95Ms: metric(m.report, 'evolutionGpuReadbackCpuWaitMs'),
    readbackCount: m.report?.readbacks?.evolutionFloatReadback?.count ?? 0,
    readbackMiB: Number(((m.report?.readbacks?.evolutionFloatReadback?.bytes ?? 0) / 1048576).toFixed(2)),
    error: m.error ?? null,
  }));
  const format = n => n === null ? 'n/a' : String(n);
  const lines = [
    REPORT_MARKER,
    '### Automated PlanetForge performance baseline (latest)',
    '',
    'Collected: ' + bundle.collectedAt + ' · [GitHub Actions run](' + runUrl + ')',
    '',
    '**These are Ubuntu Chromium / SwiftShader measurements. The mobile-sized profile is viewport emulation, NOT real iPhone Safari.** CPU wall time is not independent GPU execution time.',
    '',
    '| Profile | Result | RAF p50 / p95 (ms) | Evolution step p95 (ms) | Scene / bloom submission p95 (ms) | Readbacks (count / MiB) |',
    '| --- | --- | ---: | ---: | ---: | ---: |',
    ...rows.map(r => '| ' + r.profile + ' | ' + r.status + ' | ' + format(r.frameP50Ms) + ' / ' + format(r.frameP95Ms)
      + ' | ' + format(r.stepP95Ms) + ' | ' + format(r.sceneSubmissionP95Ms) + ' / ' + format(r.bloomSubmissionP95Ms)
      + ' | ' + r.readbackCount + ' / ' + r.readbackMiB + ' |'),
    '',
    'Raw session data and any errors are retained in the **planetforge-performance-baseline** workflow artifact (run link above). This comment is updated in place at each successful scheduled/manual run.',
    '',
    '<details><summary>Machine-readable latest summary</summary>',
    '',
    '```json',
    JSON.stringify({ schemaVersion: 1, collectedAt: bundle.collectedAt, sourceSha: bundle.collectorSourceSha, runUrl, measurements: rows }, null, 2),
    '```',
    '</details>',
  ];
  return lines.join('\n');
}
