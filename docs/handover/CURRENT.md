# PlanetForge — active continuation state

**Workstream:** `PF-EARTH-20261013-A`  
**Checkpoint:** 2026-10-13, depth-resolved rock-column foundation; visual rejection incorporated. Re-query GitHub before coding.

## Latest verified release

- Repository: https://github.com/pjotrcasteel/PlanetForge
- Verified production main: `033f3068c12a87719cbf99657dd9946c7b011149` (#206), Pages run [38399640047](https://github.com/pjotrcasteel/PlanetForge/actions/runs/38399640047) succeeded; exact live badge independently checked. Source release: `cf72231fcb7259534fd8d1ff78e0d2e9b8c2a0f0` ([PR #205](https://github.com/pjotrcasteel/PlanetForge/pull/205)). Later docs-only main commits must be independently checked; this SHA identifies the verified science/test code.
- Displayed version: **PF 0.0.41.0**. #204/#205 changed thermal accounting, numerical admission and release checks; they did not introduce a new visible stage or change the existing zero-conductivity scenario.
- Full PR CI: #204 run [38395003474](https://github.com/pjotrcasteel/PlanetForge/actions/runs/38395003474), #205 run [38396130480](https://github.com/pjotrcasteel/PlanetForge/actions/runs/38396130480), both **success** (build, .NET, WASM publish, full browser suite).
- Pages: #204 `33d0191` run [38397777732](https://github.com/pjotrcasteel/PlanetForge/actions/runs/38397777732); #205 run [38398882086](https://github.com/pjotrcasteel/PlanetForge/actions/runs/38398882086), both **success**, including exact-SHA deployment and production browser smoke. #205 production smoke now includes the molten scenario.
- Local .NET validation: **285 domain + 155 application + 254 infrastructure = 694 tests passed**. #205 CI independently passed the same tests and **41 browser regressions**; four production-only tests are intentionally skipped in PR CI and run on Pages.
- Open foundation [PR #207](https://github.com/pjotrcasteel/PlanetForge/pull/207), head `adab79a4a41766728f1b1eab4b24ebd3029f16c8`, full CI run `38404431115` in progress. Follow-up branch `feature/column-terrain-publication` adds atomic terrain/world publication; merge/Pages remain pending. Query current PR status.

## Current milestone and human acceptance

- **M1 molten visual integrity:** implementation deployed; **iPhone visual appearance REJECTED** on PF 0.0.41.0 / `033f306`. This is the funded fully molten reference added in #202, not the older regional contrast test or Snowball.
- **M2 heat transport:** optional conservative lateral transport (#197), bidirectional vertical accounting (#204), and finite-capacity vertical stability/convergence tests (#205) are delivered. End-to-end adaptive spatial stepping and a documented coupled visual reference remain unfinished. Do not close the entire heat-transport milestone or claim magma convection.
- **M3 primary crust:** depth-resolved conserved thermal columns are in progress. Layer enthalpy, top-only radiation, internal conduction, density-derived thickness, solid surface thickness and remelting are implemented in this branch. Mechanical coherence, composition/differentiation, moving material, tectonic stresses and generated mountains remain unimplemented.
- Latest human feedback: “Looks horrible still”; screenshots of `033f306` at T+0/1/2/3/11h show an orange mottled globe and inherited rocky relief reappearing during cooling. Values: 2117–2309 K / 100% melt initially; 1931–2057 / 100% at 1h; 1840–1894 / 90–99% at 2h; 1773–1820 / 79–87% at 3h; 1450–1471 / 25–28% at 11h. Controls fit; appearance is not accepted.
- User clarified the objective: real formation from underlying layers, through crust, tectonic deformation/mountains in the existing world-builder/Hero engine, then water/atmosphere/climate and eventually a lush planet. Cosmetic colour changes cannot satisfy this. Later life/ecosystems expand the long-term vision; no physical shortcut or promise that all stages already exist.
- ChatGPT screenshots also show interrupted streaming; this is distinct from application defects. Preserve work in GitHub and do not require the user to repeat instructions.
- Developer production Chromium check at `853a379`: T+0 **2117–2309 K**, T+1h **1931–2057 K**, both **100% melt**, accepted step and no page errors. A 390 px Chromium viewport is not a real Safari measurement.

## Active column increment and validation

- The same `SurfaceRock` mass/enthalpy is partitioned into immutable depth layers inside `PlanetOriginCanonicalRockCells`; no parallel ledger, clock or funding. Radiation/stellar forcing affect the top; equal/opposite Fourier fluxes connect depth layers and the finite mantle.
- Phase/density determine actual column volume and thermal solid surface thickness. A solid top can coexist with molten material below and remelt under funded heat. Fully solid material is NOT yet mechanically coherent crust.
- Explicit steps bound conduction plus the top radiative Jacobian against minimum sensible capacity; donor overdraw, unsupported aggregate lateral mixing, nonfinite states and cancellation reject without changing the original state.
- Local checks: **292 domain + 155 application + 254 infrastructure = 701 passed**. Seven new tests cover partition/conservation, surface-only cooling, internal diffusion, solid lid above melt, contraction, funded remelting, rejection/cancellation and exact-time convergence/replay. Full GitHub/browser checks are pending; the deployed application has not changed.
- Follow-up: shared canonical terrain now derives physical column contraction without inherited relief; top-phase atlas uses the radiating layer. World/terrain/snapshot publication is serialized and rolls back on failure, with unique geometry revisions even for failed trials. Clearing/reset restores prior terrain and independent geology cannot overwrite active origin columns.
- Follow-up local validation: **292 domain + 156 application + 258 infrastructure = 706 tests passed**. Full CI/browser/Pages and a new visual scenario are still pending.
- Contract: [depth-resolved rock columns](../earth-origin-depth-resolved-rock-columns.md). No user visual check is useful for this domain-only increment.

## Delivered this session

- #204: typed environmental→interior heat return, separate gross internal heat counters, matched debits/credits for hot and cold regions, history retained through volatile transfers, direct interval integration avoiding cumulative-export subtraction. Overdraw/nonfinite/cancellation fail without publication.
- #205: vertical conduction must satisfy `conductance * duration / minimumHeatCapacity <= 0.5` for each cell **and the sum seen by the finite common mantle**. Sensible `mass * cp` conservatively bounds the fusion interval. Oversized steps are rejected, never clipped; exact-time timestep refinement converges in tests.
- #205: permanent exact-SHA production smoke now enters the fully molten scenario, advances its canonical heat state, checks WebGL and resets; existing Snowball/regional checks remain.
- Updated [energy contract](../earth-origin-thermal-coupling-contract.md). Historical details are preserved in [the original snapshot](PF-EARTH-20261013-A.md) and [the released heat-coupling snapshot](PF-EARTH-20261013-A-20261013-heat-coupling.md).

## Architectural and scientific boundaries

One canonical coupled world owns clock, mass, enthalpy and accepted revision; orbital/local LOD 0–3 consume projections. Heat returned to the mantle is internal transport, not impact heat, matter delivery or negative space radiation. Preserve Hero geology, hydrology, archives, existing scenario behavior and terrain publication.

The current molten start is an explicit **dry/airless hypothetical active-shell reference**: approximately seven metres at the stated reference density, a separately funded 2200 K shell and 2400 K bulk mantle, with conserved regional initial redistribution. See [magma-ocean reference](../earth-origin-magma-ocean-initial-reference.md). It is not resolved deep magma convection, a calibrated Hadean atmosphere or Earth's unique history. Conductivity remains zero in existing visual references. Radiation, chemistry, volatile rates, clouds/rain, physical new oceans and earliest life remain later work.

## Defects, risks and next steps

1. Complete full PR CI for the column foundation; merge only after all gates pass, then verify exact-SHA Pages. Update this file with actual results.
2. Complete the shared physical terrain publication PR/gates. Add an explicitly labelled depth-column reference on the main globe using bounded spatial substeps; expose surface and subsurface distinctions without extra diagnostic tabs. No pre-existing modern relief or exaggerated contraction.
3. Add bounded spatial substep re-evaluation with exact elapsed time, cancellation and one final publication. A depth-column reference must state transport parameters and thin-shell/airless limitations, not call conduction convection.
4. Continue from thermal solid material to genuinely persistent crust: composition, differentiation, material motion, strength/rheology and tectonic work before claiming mountain formation. Preserve Hero/erosion/hydrology as consumers of physically generated terrain. Optical research was parked locally and is not implemented or released; do not substitute it for genesis.
5. Continue the ten-stage roadmap [#196](https://github.com/pjotrcasteel/PlanetForge/issues/196), science [#143](https://github.com/pjotrcasteel/PlanetForge/issues/143), and crust [#184](https://github.com/pjotrcasteel/PlanetForge/issues/184). Legacy Snowball cryosphere, Hero morphology, blurry/slow local LOD and river continuity remain tracked risks (#148/#162/#163); this session did not fix them.

Local runtime note: system dotnet was absent. .NET 10.0.100 was installed for verification; running test projects from a `/tmp` checkout solved workspace test-discovery failures. Local WASM MSBuild task-host startup remained environment-limited; full GitHub build/publish/browser gates passed. Do not turn these local issues into product changes or ask the user for credentials: GitHub app Git-data operations supplied branch/commit/PR writes when CLI push had no credential. Production browser networking required the configured HTTP proxy. Use the absolute Pages URL for smoke; local browser test root-relative paths otherwise target the wrong GitHub Pages location.

## 2026-10-13 CI recovery update

Foundation #207 is merged as `3444391a10703fe14f9c9c489d3ac30afbee71cc`; full CI `38404431115` and Pages `38407076968` succeeded, including exact-SHA production checks. No new visible version in this domain foundation.

#208 initial CI `38405487663` failed one legacy camera-catalogue startup timeout (40 browser tests passed). Investigation found that origin clearing during SetSeed/reset unnecessarily generated an intermediate legacy globe. The corrected state-only initialization path removes that work; an explicit atomic `ResetRegionalRock` restarts the physical reference without an intermediate legacy source, and uniform column elevations have an exact constant fast path. A new regression asserts SetSeed performs zero terrain samples. Full replacement CI remains pending; do not merge before it passes.

#209 prepares the bounded depth-column reference (PF 0.0.42.0), physical surface/deep melt and thickness HUD, top-temperature optical emission and additional production smoke. It remains unmerged/unreleased. Initial Razor upload corruption was caught by CI and corrected; all 25 changed files were then independently compared byte for byte. Local browser inspection reached T+4h: surface 1036–1049 K, melt 0%, solid top 0.82 m, bottom melt 100%. Reset needed the above performance fix and asynchronous completion waits. Current user still sees interrupted ChatGPT streaming; this is not evidence that CI/release has finished.

Next: finish #208 corrected CI, propagate the fix into #209 with ancestry preserved, validate both new-reference browser tests and legacy catalogue startup on the exact build, merge in order, verify Pages and commit a complete dated release handover before asking for a meaningful iPhone check. Mechanical differentiation/coherent crust, mantle convection, tectonics/mountains, new water/climate/chemistry/life remain unfinished.
