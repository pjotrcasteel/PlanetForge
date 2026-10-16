# Automated performance collection — PF-PERF-01

The published PlanetForge site is profiled automatically in [this GitHub Actions workflow](https://github.com/pjotrcasteel/PlanetForge/actions/workflows/performance-baseline.yml). The job runs daily (GitHub cron best-effort), on manual dispatch, and once when the workflow is first added to main.

**How to read the results:** [GitHub issue #239](https://github.com/pjotrcasteel/PlanetForge/issues/239) has one **latest benchmark** comment maintained by the GitHub Actions bot. The comment includes a table and machine-readable JSON. Its run link contains the **planetforge-performance-baseline** artifact with full per-stage CPU-wall percentiles and readback counts/bytes. The artifact is retained for 90 days, subject to repository settings. ChatGPT can inspect the issue, run logs, and artifacts via the connected GitHub integration.

## How it runs

- Uses the **published** https://pjotrcasteel.github.io/PlanetForge/ app, `?pfPerf=1&seed=24061984`.
- Creates fresh independent Chromium/SwiftShader sessions for 960×600 desktop and 390×844 touch-enabled mobile **emulation**.
- Waits until renderer playback is ready, records **actual startup-to-ready wall time**, pauses/restarts the physics with the exact same seed in each profile, clears the warmup profiler, then resumes the *real* continuously updating simulation for **90 seconds**.
- Saves chronological 15-second observations with current geological regime, cumulative frame count and cumulative percentiles. These snapshots indicate which eras were observed but do **not** claim phase-isolated timings.
- Reads `window.__planetForgePerf.report()`, records the deployed build badge, viewport, browser version and timing counters. If a profile fails, its error is recorded, the workflow fails, and the issue comment exposes the failure.
- Replaces only the bot's own latest-report comment, leaving the issue conversation and prior raw artifacts intact.
- Does not record real visitor sessions, identifiers, exact device model, IP, location, browsing activity or any user-entered data; it does not deploy a public telemetry ingestion endpoint.

## Interpretation

These are repeatable **hosted Linux Chromium with software WebGL** measurements. A phone-sized viewport is **NOT** a real iPhone, Safari, WebKit, Metal or production GPU and cannot establish mobile smoothness. The first-boot latency includes WASM startup, but `rendererInitializeCpuMs` does not. Shader command-submission timers are not independent GPU timings. Results will fluctuate with GitHub runner load; use repeated observations and phase-aware samples before treating a variation as a regression. **p95 with fewer than 40 samples is explicitly low confidence**; with 16 samples the p95 is just the single worst observed stall. Every summary reports actual sample counts and the worst frame, and raw artifacts preserve the 15-second snapshots.

For real iPhone Safari, keep the explicit opt-in `?pfPerf=1` profile and paired Safari Web Inspector until a secure, privacy-reviewed, consent-based mobile ingest endpoint is deployed. A static GitHub Pages site cannot receive telemetry uploads on its own. Do not add a GitHub token or write-enabled API key to a public browser.

## Follow-up

Add stage-aware profiling across molten, lid and reconstructed Earth history once the nightly baseline is stable; investigate the 8 MiB sync field readback and full-res bloom next. See issue #239 and the [runtime metric reference](PF-PERF-01-runtime-baseline.md).
