# Solidification numerical recovery — 2026-10-13

Workstream **PF-EARTH-20261013-A**. Successor: read CURRENT.md and independently query GitHub; this is an immutable experiment snapshot, not an assertion about future production state.

## Release status at handoff

#219 and #220 are merged with preserved ancestry; source main `12fdc68825f518e7719e10147471b76fb238652b`. #220 full CI 38444603756 passed 729 .NET and 48 browser regressions, with five production-only tests skipped. New exact-SHA Pages 38446290792 was pending behind the previous model-only Pages run at handoff. Do not call PF 0.0.44.0 live before checking deployment/production gates. Real-device acceptance remains open.

## Implemented

PR #219 adds opt-in quadratic mass-depth grading, a massless conductive/radiative face heat balance and a conservative backward-Euler phase-enthalpy solve with damped tridiagonal Newton iterations. Shared face fluxes supply matched layer debits/credits; bottom exchange still closes against the finite explicit mantle. Beginning-of-step physical widths are frozen for each first-order step. Failure/cancellation/substep-budget exhaustion leaves the accepted world unchanged. New mesh parameters do not change the selected eight-layer legacy reference.

Compensated immutable updates retain small transfers in layers, environmental reservoirs and mantle. The original 128-layer explicit experiment crossed the existing surface-account tolerance; layer/environment compensation repaired that. A subsequent very-long explicit run exposed mantle accumulation drift; mantle compensation repaired it too. No conservation threshold was loosened.

PR #220 initializes optical boundary forcing and adds the explicit main-planet candidate `?visualTest=1&resolvedColumns=1`, displayed PF 0.0.44.0. FACE temperature comes from accepted forcing; TOP CELL MELT belongs to owned material. Fully-solid control-volume depth and linearly reconstructed solidus depth are distinct diagnostics. The existing shared atlas/terrain gateway supplies orbit/local temperature, phase and genuine density contraction. Default chronology and legacy references remain preserved.

## Committed regression evidence

Local and #219 full CI: **309 domain + 160 application + 260 infrastructure = 729 .NET tests**. CI run 38444166022: WASM publish and **47 browser regressions passed**, five production-only tests skipped in PR validation. #219 merged as `dfa1499c0c13b19afdc377084547a1ebd9f57b91`.

Matched initially uniform 2200 K, six geographic cells, four hours exactly, 5 s cap, k=3 W/(m K), solid/liquid densities 3000/2800 kg/m³, 25 m bottom boundary, material cp=850 J/(kg K), fusion latent heat=400 kJ/kg and solidus/liquidus=1300/1900 K. Domain regression uses solar flux 1361 W/m², zero Bond albedo and emissivity 0.9:

| Layers | Reconstructed solidus, m | Radiating face, K | First unresolved cell width, m |
| --- | ---: | ---: | ---: |
| 32 | 0.06517757 | 827.82226 | 0.04522375 |
| 64 | 0.06481704 | 828.50570 | 0.02079330 |
| 128 | 0.06507348 | 828.49750 | 0.01083677 |

Regression screens require fine-grid reconstructed-front agreement below 1 mm and face agreement below 1 K; the finest grid also requires at least three fully solid cells and unresolved width below 12 mm. This is not an independent absolute front-error estimate or calibrated crust thickness. The prior uniform-grid failure fixture remains.

Against an independent 32-layer explicit 0.25 s reference at one hour, implicit 120/30/5 s caps produce top-cell temperature errors 3.17948/0.81737/0.14393 K. Both integrators retain exact elapsed microseconds. No claim of higher temporal order is made.

## Independent experiments beyond CI fixtures

A console harness used the same material/transport, Bond albedo **0.15** and all six day/night columns. All 64-to-128-layer front differences remain below 1 mm; radiating face differences remain below 1 K. Integrated emitted heat per area at 32/64/128 layers is 570647941.776 / 576901501.154 / 578470886.491 J/m²; the finest relative difference is approximately 0.27%. Funded solar reheating at 1e6 W/m² for 1000 s remelts the previously solid subsolar top completely, with the closed global ledger retained.

The actual 384-cell, spatially heterogeneous initial reference at a 30 s cap was separately compared at 64/128 depth layers for four hours. Maximum reconstructed-front difference across all cells: **0.000513838664 m**; maximum radiating-face temperature difference: **0.521613530 K**. The 64-layer native .NET run took approximately **5.3 s** for four hours; this is not a browser/Safari performance result.

The previously failing 128-layer explicit four-hour run was repeated after all compensation fixes. Relative global energy residual: **0** at reported double precision; relative surface-account residual: **3.83367866e-16**; face temperature differs from the implicit 5 s run by **0.02925837 K**. This is independent scratch-harness evidence, not an additional CI fixture. Existing committed fixtures preserve the smaller explicit/implicit comparison.

An independent exact #220 build artifact (`400ed5a5c0d2a79972a2aa0f2b8b107e75371858`) passed the new 1440×900 PC Chromium test in **1.8 minutes**, including startup, four hourly advances, a temperature-map switch, geometry/WebGL checks and reset. Controls fit. T+0: face 1920–2051 K, fully molten owned top/depth. T+4h: face 811–835 K, solid top 0.06–0.08 m, reconstructed solidus 0.061–0.070 m, fully molten bottom. Production SHA substitution happens only on deployment; the temporary artifact badge contains a placeholder SHA.

## Remaining limitations and next visual gate

This is a hypothetical dry/airless thin conducting shell, not full-depth convecting magma, chemical differentiation, mechanically coherent moving crust, tectonic mountains or calibrated Earth cooling. The interpolated solidus has mesh/model uncertainty; the finest unresolved control volume remains about one centimetre wide. Convergence agreements do not establish absolute physical accuracy or Earth's true chronology. No pressure/composition-dependent phase laws, lateral material advection or new archive format are claimed.

The physical globe remains a smooth thermal reference. Do not manufacture geological relief or convection texture to make it prettier. Browser simulation advances are substantially slower than native .NET; worker execution and optics/material calibration remain useful future work. Real PC interaction and iPhone Safari appearance are not accepted from Chromium automation.

Merge #220 only after its full CI, then require exact-SHA Pages build/deploy/production smoke and a resolved-reference live check before offering it as live. Fixed seed 24061984; T+0h/1h/4h, same camera and zoom; physical then temperature/melt maps. Check PC controls/loading/orbit/local consistency, with iPhone as a secondary quick check. Physically sourced composition/material genesis, coherence/rheology and conservative transport precede actual deformation/mountain generation.
