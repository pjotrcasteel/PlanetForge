# PF-PERF-01 — runtime baseline (instrumentation, not optimizations)

## Scope and provenance

Baseline: main `94d65a7` after snapshot import and green GitHub Pages smoke. This first slice introduces **opt-in CPU wall-time telemetry**. It does not change physics, output resolution, quality, camera, simulation rates or shader code.

## Capture a real browser session

1. Open `https://pjotrcasteel.github.io/PlanetForge/?pfPerf=1&seed=24061984` on a device. Start capture from page load; wait until the 3D globe is visible.
2. Exercise molten, stagnant-lid, plate/modern eras, orbit and zoom for at least 2 minutes (and a 10-minute session for memory/thermal behavior).
3. In the browser's JavaScript console run `window.__planetForgePerf.report()` and copy the JSON into a local text file. On iPhone use Safari Web Inspector from a paired Mac. **No metrics are uploaded**.
4. Reload without `pfPerf=1` for a control run. `getPerformanceReport()` returns `null` and no arrays are allocated.
5. Record hardware, iOS/Safari version, orientation, viewport, seed, commit SHA, battery/thermal mode and visual screenshots *alongside* the metrics. Don't compare runs on different devices as if controlled.

## Available measurements

| JSON key | Meaning |
| --- | --- |
| `rendererInitializeCpuMs` | JS renderer setup on current page, excludes .NET/WASM/download before initialize |
| `initialDrawSubmissionMs` | initialize → first GPU draw **submitted**, not a proven displayed frame |
| `visibleFrameIntervalMs` | RAF delta while evolution is visible; includes frame stalls |
| `frameCallbackCpuMs` | CPU wall-time spent inside RAF callback (includes any GPU sync stalls) |
| `evolutionStepCpuMs` | Synchronous JS simulation and GPU command submission |
| `sceneDrawCpuSubmissionMs` | Command submission for planet scene, not GPU execution time |
| `bloomCpuSubmissionMs` | Bloom framebuffer copy and shader submission, not GPU execution time |
| `seaLevelCalculationCpuMs` | Hypsometry including its currently required GPU readback |
| `plateConsolidationCpuMs` | Full plate connectivity calculation including its GPU readback |
| `evolutionGpuReadbackCpuWaitMs` | CPU wait/return around **existing** RGBA float GPU readback |
| `lodRequestRoundTripMs` / `lodCommitCpuMs` | Main-planet tile request and geometry publication |
| `readbacks.evolutionFloatReadback` | Number and total bytes of model readbacks, not GPU storage allocated |

Duration `count`, `averageMs`, `maxMs` cover the session; `p50Ms`, `p95Ms`, `p99Ms` use a bounded latest-512-sample window. These are CPU wall-time measurements, **not** GPU execution timings. Don't assume browser memory APIs are available on iPhone Safari. Histogram collection is constant-memory; metrics are disabled without the URL flag. Current reporting does not include full Blazor/WASM startup or live device memory — measure those in the next slice.

## Next acceptance gate

Collect real iPhone Safari portrait and landscape runs, a desktop control, and p50/p95 frame deltas across molten, lid and plate stages. Prioritize reducing synchronous 8 MiB float readbacks and full-resolution bloom *only if confirmed by measurements*. Keep deterministic seeds, physical energy/mass balances and visible image quality unchanged. See [issue #239](https://github.com/pjotrcasteel/PlanetForge/issues/239).

This file intentionally includes **no invented performance numbers**: none of these tests represent direct iPhone measurements.
