import { test } from 'node:test';
import assert from 'node:assert/strict';
import { metric, summarizeBaseline, REPORT_MARKER } from './render-baseline-report.mjs';

const report = {
  schemaVersion: 1, collectedAt: '2026-10-17T00:00:00Z', collectorSourceSha: 'abc123',
  measurements: [
    { id: 'mobile-emulated-chromium-swiftshader', status: 'success', actualIphoneSafari: false,
      buildBadge: 'PF 0.0.49 · abc123', report: {
        geologyRegime: 'magma-ocean', durations: { visibleFrameIntervalMs: { p50Ms: 16.111, p95Ms: 29.333 }, evolutionStepCpuMs: { p95Ms: 2.5 } },
        readbacks: { evolutionFloatReadback: { count: 2, bytes: 16 * 1048576 } },
      } },
  ],
};

test('PerformanceReport_SummarizesMeasuredNumbersAndLabelsEmulation', () => {
  const out = summarizeBaseline(report, 'https://github.com/pjotrcasteel/PlanetForge/actions/runs/123');
  assert.ok(out.includes(REPORT_MARKER));
  assert.ok(out.includes('NOT real iPhone Safari'));
  assert.ok(out.includes('16.11 / 29.33'));
  assert.ok(out.includes('2 / 16'));
  assert.ok(out.includes('"actualIphoneSafari": false'));
});

test('PerformanceReport_MissingMetrics_NotInvented', () => {
  assert.equal(metric({}, 'missing'), null);
  const out = summarizeBaseline({ collectedAt: 'test', measurements: [{ id: 'desktop', status: 'failed', error: 'timeout' }] }, 'https://github.com');
  assert.ok(out.includes('n/a / n/a'));
  assert.ok(out.includes('"status": "failed"'));
});

test('PerformanceReport_EmptyData_Rejected', () => {
  assert.throws(() => summarizeBaseline({ measurements: [] }, 'https://github.com'), /No performance measurements/);
});
