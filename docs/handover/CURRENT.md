# PlanetForge — active continuation state

**Workstream:** `PF-EARTH-20261013-A`  
**Checkpoint:** 2026-10-13, PF 0.0.42.0 depth-column reference released. Always re-query GitHub.

## Latest verified release

- Source/main release: **`e26d31ac62df9a8399f740536751254bed08719a`**, [PR #209](https://github.com/pjotrcasteel/PlanetForge/pull/209), displayed **PF 0.0.42.0**. This docs checkpoint may advance the badge SHA; verify current main and Pages independently.
- Full corrected PR CI [38411066125](https://github.com/pjotrcasteel/PlanetForge/actions/runs/38411066125): build, JS validation, **294 domain + 157 application + 260 infrastructure = 711 .NET tests**, WASM publish, **43 browser regressions passed**. Five production-only tests skipped in PR CI run on Pages.
- Pages [38413685773](https://github.com/pjotrcasteel/PlanetForge/actions/runs/38413685773): build/deploy and **five exact-SHA production checks succeeded**, including new column advance/reset.
- #207 (`3444391`, CI `38404431115`, Pages `38407076968`) and corrected #208 (`6231961`, CI `38410587096`, Pages `38412299210`) also succeeded and are merged. No open implementation PRs at this checkpoint; a docs-only recovery PR follows. Recheck current GitHub state.
- Main-globe reference: https://pjotrcasteel.github.io/PlanetForge/?visualTest=1&rockColumns=1 . Prior magma (`magmaOcean=1`), partial melt (`regionalRock=1`), uniform and Snowball references remain preserved; this is opt-in, not the default playable chronology.

## Milestone / human acceptance

- **M1 visual integrity:** PF 0.0.41.0 / `033f306` was **REJECTED** in the user's iPhone screenshots at T+0/1/2/3/11h: orange mottled sphere and inherited rocky relief returning. PF 0.0.42.0 appearance/performance is **not yet accepted on iPhone Safari**. Do not confuse desktop Chromium with Safari acceptance.
- **M2 heat transport:** previous lateral/vertical conservation and admission bounds remain. Columns add depth conduction, top-only radiation and bounded exact-time re-evaluation. Full-depth magma convection, layer-consistent lateral advection, long-time error-controlled integration and calibrated atmospheric transport remain unfinished.
- **M3 primary crust:** thermal depth-column foundation and its shared terrain bridge are released. A thermally solid top is **not** differentiated, mechanically coherent, persistent moving crust. Tectonic stresses/material transport, mountains and volcanic genesis are not implemented by this release.
- User requires real layers → crust/material → tectonic deformation through the existing terrain/Hero engine → water/atmosphere/climate → eventually a lush living planet. No cosmetic stage changes, prebuilt primordial continents or fake terrain. The lush ecosystem goal extends beyond the ten-stage earliest-life roadmap; do not rush animals/plants into it.
- User also reports interrupted ChatGPT streaming and long waits. Preserve work in GitHub; the cause of client delivery failures is undiagnosed and is distinct from PlanetForge defects. Do not ask the user to repeat development instructions or promise work after the active session ends.

## Delivered contracts

The **same funded SurfaceRock** mass/enthalpy is partitioned into immutable Eulerian layers inside `PlanetOriginCanonicalRockCells`, with one ledger, clock and accepted world. Radiation/stellar heat act on the top; internal Fourier heat has matched debits/credits; the bottom exchanges with the finite mantle. Phase/density determine actual volume and thermal solid surface thickness; funded heat can remelt it. No extra heat/material account.

The globe/local material atlas resolves the top layer. Actual thickness minus initial thickness drives the shared terrain source, without inherited modern relief or height amplification. `IPlanetOriginSurfacePublication` serializes world/terrain/snapshot reads/publication; projection failures restore accepted state and geometry revision identifiers are never reused. Leaving restores the prior terrain; independent geology cannot overwrite active columns. Explicit reference restart is atomic, and initialization no longer builds an unused intermediate globe.

Eight layers, densities 3000/2800 kg/m³, conductivity 3 W/(m K), declared 25 m bottom boundary, albedo 0.15/emissivity 0.9 and 30 s accuracy cap define a **hypothetical dry/airless thin-shell experiment**, not calibrated Earth cooling. Admission re-evaluates conduction/radiative Jacobian on every immutable substep, floors to microsecond ticks and integrates the exact remainder; no partial publication or clipping. Column-only optical emission uses Planck/CIE projection at actual top Kelvin for solid and liquid, fixed exposure and declared neutral reflection.

Read [column science/publication contract](../earth-origin-depth-resolved-rock-columns.md), [Hero integration](../hero-terrain-integration-contract.md), [dated depth checkpoint](PF-EARTH-20261013-A-20261013-rock-columns.md), [released heat snapshot](PF-EARTH-20261013-A-20261013-heat-coupling.md) and [original history](PF-EARTH-20261013-A.md).

## Verification / defects / risks

- Independent exact PR build: all three targeted browser tests passed (catalogue startup, four-hour physical freezing + reset/switch, temperature-driven emission). Both WebGL programs compile/link. T+0: **2117–2309 K, 100% top/deep melt, 0 m solid top**. T+4h: **1036–1049 K, 0% top melt, 0.82 m solid top, 100% deep melt**. Controls fit; orbit and ground use the same physical source; no WebGL errors.
- These are model diagnostics, not accepted beautiful convection/mountain visuals. The airless emitting sphere is nearly uniform initially and dark when cooled. The linear RGBA8 emission lookup can lose faint incandescence; reflection/roughness is uncalibrated. Physical temperatures must not be changed to fix appearance.
- **Measured accuracy blocker:** independent 4 h / 5 s cap depth refinement gives solid-top thickness **0.81689 / 0.40845 / 0.20422 / 0.10211 m** at 8/16/32/64 layers; surface ranges **1037–1051 / 839–854 / 775–798 / 808–832 K**. The front diagnostic equals one cell and is not converged. Conservation remains within 1.5e-13 relative global energy. This scratch experiment is not a CI test; its protocol/results are preserved in the dated snapshot. Do not interpret 0.82 m as a quantitative primary-crust prediction. Time convergence is tested, depth-spatial accuracy is not established. Constant-pressure phase material, fixed composition and planar thin-shell geometry (depth/radius <= 0.001) are explicit restrictions.
- Epoch-level aggregate rock temperature is a bulk enthalpy diagnostic. Future boundary models must consume the owned top-layer phase, retain depth state, and not silently downgrade through epoch-only stepping. Core/full-mantle structure, pressure-dependent crystallization, chemical differentiation and atmospheric/wet physics remain unresolved.
- First #208 CI failed a legacy catalogue-startup timeout; first #209 upload contained corrupted Razor text and was caught before release; corrected #209 then exposed too-short reset waits. All were investigated/fixed; corrected full CI and production gates passed. Large uploads must be read individually and all blobs compared to local bytes before accepting source validation.
- Legacy Hero morphology, local LOD blur/performance, cryosphere and river continuity risks remain (#148/#162/#163). No new save/archive schema or physical column persistence is claimed.

## Exact next steps

1. Verify this docs checkpoint/main/Pages; inspect any new user Safari feedback at T+0, T+1h, T+4h, reset and orbit/local navigation. Appearance acceptance remains open; do not switch the default prematurely.
2. Formalize the measured 8/16/32/64 refinement experiment as a scientific regression gate; resolve the strongly layer-dependent solid-front/surface temperature before geological calibration. Improve/control the radiating boundary, depth mesh and front reconstruction; layer-count choice alone is not a physics fix. Improve surface optical precision/calibration independently of physical state; no cloud/noise geology substitute.
3. Extend conserved primary material genesis: composition/provenance, owned material identity, mass/sensible/fusion enthalpy and volume through formation/remelting, then rheology/coherence, buoyancy and mechanical-work accounting. Preserve depth ownership in every coupled process and snapshot.
4. Couple conservative moving material and uncertain tectonic regimes to the existing terrain/Hero/erosion gateways. Existing modern crust research catalogues and imposed plate kinematics are not primordial genesis. Only actual deformation may create mountains; sub-metre contraction cannot be exaggerated into them.
5. Continue roadmap [#196](https://github.com/pjotrcasteel/PlanetForge/issues/196), science #143 and crust #184; later atmosphere/volatiles/rain/water/climate/chemistry/life must close the same canonical accounts. Keep CURRENT.md in substantial PRs and preserve dated decisions.

User requests continuation with another model; new implementation is paused for this verified handoff. No background execution is promised.

Local runtime recovery: use .NET 10.0.100 under `/tmp/pf-dotnet-stable` and a `/tmp` checkout for tests when workspace discovery fails. Local WASM task-host failures were environmental; GitHub full builds/publish succeeded. GitHub app Git-data writes avoid absent CLI push credentials. Production Playwright requires the configured HTTP proxy and absolute Pages URLs. User turns can invalidate transient tool-store/process IDs; recover from Git/files. Do not misclassify this as a product failure.


## Depth-refinement numerical gate — implementation in progress

Workstream `PF-EARTH-20261013-A` now includes an explicit per-column resolution sample (fully solid control-volume depth, first unresolved control-volume extent, radiating-cell Kelvin, and count of fully solid layers), plus a provisional four-grid depth/temperature screening helper. The archived independent 8/16/32/64-layer 4-hour observations are a regression fixture that **must fail this screening**; synthetic sufficiently resolved data demonstrate the positive case. Existing four-hour application test now cross-checks that the new sample corresponds to the same published canonical columns.

This prevents mistaking the prior 0.81689 m eight-cell display for a depth-converged crust. **No physics fix or new release acceptance is claimed by this diagnostic-only change.** The measured case still has only one fully solid control volume, and its inferred front moves with mesh spacing. Next: executable independent spatial-refinement benchmark using matching initial state/clock/forcing with measured error budget, followed by radiating-boundary/front reconstruction and genuine convergence improvements. Do not expose `MeetsScreeningCriteria` as proof of scientific validation. Verify PR CI, merge, Pages and exact SHA before marking deployed.
