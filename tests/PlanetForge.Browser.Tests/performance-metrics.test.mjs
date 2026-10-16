import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
    configurePerformanceMetrics, performanceMarkStart, recordPerformanceDuration,
    recordPerformanceSample, recordPerformanceReadback, snapshotPerformanceMetrics,
} from '../../src/PlanetForge.Presentation.Web/wwwroot/js/performanceMetrics.js';

test('PerformanceMetrics_Disabled_CollectsNothing', () => {
    configurePerformanceMetrics(false);
    assert.equal(performanceMarkStart(), null);
    recordPerformanceSample('rafMs', 42);
    recordPerformanceReadback(2048);
    assert.equal(snapshotPerformanceMetrics(), null);
});

test('PerformanceMetrics_Enabled_ReportsDurationsAndReadbackBytes', () => {
    configurePerformanceMetrics(true);
    for (const value of [10, 20, 30, 40, 50]) recordPerformanceSample('rafMs', value);
    recordPerformanceReadback(8 * 1024 * 1024);
    recordPerformanceReadback(8 * 1024 * 1024);
    const report = snapshotPerformanceMetrics();
    assert.equal(report.schemaVersion, 1);
    assert.equal(report.durations.rafMs.count, 5);
    assert.equal(report.durations.rafMs.p50Ms, 30);
    assert.equal(report.durations.rafMs.p95Ms, 50);
    assert.equal(report.durations.rafMs.averageMs, 30);
    assert.equal(report.readbacks.evolutionFloatReadback.count, 2);
    assert.equal(report.readbacks.evolutionFloatReadback.bytes, 16 * 1024 * 1024);
});

test('PerformanceMetrics_Samples_AreBoundedAndNonFiniteValuesIgnored', () => {
    configurePerformanceMetrics(true);
    recordPerformanceSample('renderCpuMs', NaN);
    recordPerformanceSample('renderCpuMs', Infinity);
    recordPerformanceSample('renderCpuMs', -1);
    for (let i = 0; i < 600; i++) recordPerformanceSample('renderCpuMs', i);
    const entry = snapshotPerformanceMetrics().durations.renderCpuMs;
    assert.equal(entry.count, 600);
    assert.equal(entry.sampleCount, 512);
    assert.equal(entry.maxMs, 599);
    assert.equal(entry.p50Ms, 343);
    assert.equal(entry.p95Ms, 574);
});

test('PerformanceMetrics_Reset_ProtectsSnapshotAndClearsPreviousSession', () => {
    configurePerformanceMetrics(true);
    const start = performanceMarkStart();
    recordPerformanceDuration('setupCpuMs', start);
    assert.ok(snapshotPerformanceMetrics().durations.setupCpuMs.count === 1);
    const copy = snapshotPerformanceMetrics();
    copy.durations.setupCpuMs.count = 100;
    assert.equal(snapshotPerformanceMetrics().durations.setupCpuMs.count, 1);
    configurePerformanceMetrics(true);
    assert.deepEqual(snapshotPerformanceMetrics().durations, {});
    configurePerformanceMetrics(false);
    assert.equal(snapshotPerformanceMetrics(), null);
});
