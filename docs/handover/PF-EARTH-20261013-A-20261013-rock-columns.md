# PF-EARTH-20261013-A — depth-column development checkpoint

Historical snapshot, 2026-10-13. This records source/verification as known at writing; CURRENT.md and GitHub supersede its release status. Preserve the original and heat-coupling snapshots.

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
- Merged foundation [PR #207](https://github.com/pjotrcasteel/PlanetForge/pull/207), head `adab79a4a41766728f1b1eab4b24ebd3029f16c8`, full CI run `38404431115` succeeded: 701 .NET, 41 browser, four production checks skipped in PR CI. Merge `3444391a10703fe14f9c9c489d3ac30afbee71cc`; Pages verification pending. Open [PR #208](https://github.com/pjotrcasteel/PlanetForge/pull/208), head `e63007ad6ce8b5f835db9ace63174c11c7588cb2`, CI `38405487663` running, adds atomic terrain/world publication. Third branch `feature/physical-column-reference` adds bounded integration and a new opt-in main-globe reference; merge/Pages remain pending. Query current PR status.

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

## New depth-reference increment

- `?visualTest=1&rockColumns=1` is prepared for PF **0.0.42.0**, not yet deployed. Eight funded layers, top-only radiation, k=3 W/(m K) column/bottom conduction, 25 m declared bottom boundary, actual solar forcing, albedo 0.15/emissivity 0.9 and 30 s accuracy cap. Safe admission is recalculated at every substep with exact microsecond remainder and a finite substep budget. Only the final world is published.
- HUD shows surface phase, thermal solid-top thickness and deep melt. A local four-hour reference test produces a solid top while deep melt remains >90%; mass/enthalpy close. This is a thin dry shell experiment, not full magma convection or calibrated Earth timing.
- Column-only optics use actual top temperature for Planck/CIE visible emission for BOTH solid and liquid, fixed exposure, and a declared neutral reflected-material reference. No old geological tint, mottled noise, fake cracks or generated mountains. Direct local WebGL compilation/linking passed for globe and local programs. Legacy optical paths remain unchanged.
- Local .NET: **294 domain + 157 application + 259 infrastructure = 710 passed**, including unsupported-display publication rollback. Full new-reference browser/CI/Pages and iPhone appearance are pending. Five production-only checks will include exact-SHA column advance/reset.
- Display temperatures outside 0–6553.5 K are rejected at application projection so the canonical terrain/world transaction can restore accepted state, rather than failing only after JS publication.

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
2. Complete #208 and the physical-column-reference PR CI, merge in order, verify exact-SHA Pages and inspect the new main-globe reference locally before asking for an iPhone visual check. Capture actual surface/solid-thickness/deep-melt values; never declare user acceptance from automated tests.
3. Add bounded spatial substep re-evaluation with exact elapsed time, cancellation and one final publication. A depth-column reference must state transport parameters and thin-shell/airless limitations, not call conduction convection.
4. Continue from thermal solid material to genuinely persistent crust: composition, differentiation, material motion, strength/rheology and tectonic work before claiming mountain formation. Preserve Hero/erosion/hydrology as consumers of physically generated terrain. Optical research was parked locally and is not implemented or released; do not substitute it for genesis.
5. Continue the ten-stage roadmap [#196](https://github.com/pjotrcasteel/PlanetForge/issues/196), science [#143](https://github.com/pjotrcasteel/PlanetForge/issues/143), and crust [#184](https://github.com/pjotrcasteel/PlanetForge/issues/184). Legacy Snowball cryosphere, Hero morphology, blurry/slow local LOD and river continuity remain tracked risks (#148/#162/#163); this session did not fix them.

Local runtime note: system dotnet was absent. .NET 10.0.100 was installed for verification; running test projects from a `/tmp` checkout solved workspace test-discovery failures. Local WASM MSBuild task-host startup remained environment-limited; full GitHub build/publish/browser gates passed. Do not turn these local issues into product changes or ask the user for credentials: GitHub app Git-data operations supplied branch/commit/PR writes when CLI push had no credential. Production browser networking required the configured HTTP proxy. Use the absolute Pages URL for smoke; local browser test root-relative paths otherwise target the wrong GitHub Pages location.

## CI recovery — 2026-10-13

#207 is fully released as `3444391a10703fe14f9c9c489d3ac30afbee71cc`: PR CI `38404431115` and Pages `38407076968` succeeded. #208's first CI `38405487663` failed one legacy catalogue-startup timeout (40 browser regressions passed); its corrected head `cd137373bb654b693e487d17a41e20238ca812ef` removes intermediate terrain generation during seed/reset initialization, adds atomic explicit reference restart and an exact uniform-height fast path. Replacement full CI is pending.

#209's first commit had corrupt uploaded Razor text, caught at build by CI `38407615380`; corrected head `c4bd0bdc9f47112f1d9a9999f795ece720420261` passed build/.NET/WASM and had all 25 changed files independently compared byte for byte. Local exact-build browser inspection reached T+4h (1036–1049 K surface, 0% surface melt, 0.82 m solid top, 100% deep melt). It exposed reset taking longer than the default assertion wait. The replacement now uses atomic `ResetRegionalRock` without rebuilding intermediate legacy terrain, and browser waits track asynchronous WASM/geometry completion.

Local corrected infrastructure tests: **260 passed**; domain 294 and application 157 previously passed. New regression verifies SetSeed performs zero terrain samples; explicit restart preserves the terrain/world transaction and ordinary publication still rejects a stale clock. Recheck application/full CI after reset changes. The new reference remains unmerged/unreleased; no Safari acceptance is claimed. On future Git-data uploads read large files individually and compare every uploaded blob with local bytes before accepting the CI result. User reports continuing interrupted ChatGPT streams and frustration; do not ask them to repeat development instructions.

## Exact corrected-build inspection

PR #209 head `d130b765488dcdc9250faa4c3c4c24afa44576bb` was built/published by CI run `38411066125`; that exact temporary browser build was downloaded for independent local checks. All three targeted Playwright tests passed (1.4 minutes): legacy catalogue startup, physical column advance through four hours plus reset/uniform comparison, and optical projection. This is not full CI, Pages or Safari acceptance.

At T+0: 2117–2309 K surface, 100% surface/bottom melt, zero solid top. At T+4h: 1036–1049 K surface, 0% surface melt, 0.82 m contiguous solid top and 100% bottom melt. Geometry revision changed; controls remain in the viewport and WebGL reports no errors. The airless emitting globe is smooth/bright initially and very dark when cooled. These captures establish working depth-derived state and presentation, **not beautiful molten convection, mountain morphology or final visual acceptance**.

## Immediate next scientific integration

The existing `PlanetCrustMaterialParcel` / ownership/deformation research starts from a modern seeded oceanic/continental catalogue and imposed kinematic history. Its representative samples and Airy mass response are not an origin genesis or stress/viscosity/energy solver. Preserve that research but do not insert it into the molten timeline as if its initial continents, plate speeds, chemical distinction or mechanical work emerged from thermal columns.

Next foundation: explicit composition/provenance and actual owned material at the solidification boundary; conservative transfers of mass, sensible/fusion enthalpy and specific volume through formation/remelting. Then coherent material/rheology, buoyancy and driving-work accounting, geometry/material transport and tested tectonic-regime hypotheses. One Eulerian thermal cell is not a moving plate. Generated persistent material must feed the shared terrain/Hero/erosion gateways at one accepted world epoch. Surface contraction in this thin-shell case is sub-metre and cannot be amplified into mountain belts.

Full-depth convective magma evolution, pressure/composition-dependent phase fronts and atmospheric feedback remain major scientific gaps. The 30 s cap is an accuracy choice for a short shell experiment, not an Earth formation timescale. Later steam/rain/water, connected downhill routing, climate/ice/erosion and chemistry/life require their own coupled conservative models; the user's lush planet vision is long-term scope beyond the current first-life boundary.

## Recovery mechanics

The latest committed #209 recovery head is `d130b765488dcdc9250faa4c3c4c24afa44576bb`; corrected full CI is `38411066125`. Corrected #208 head is `cd137373bb654b693e487d17a41e20238ca812ef`, full CI `38410587096`. Merge #208 then #209 only after their full checks succeed; both merges should preserve commit ancestry. New #209 source/large-file upload was independently compared byte for byte across all 25 changed files. Store transient command-session IDs and tool-store data cautiously: a new user turn can invalidate them; recover from Git/files instead of treating their disappearance as a product failure.

On the latest corrected source, local tests are 294 domain + 157 application + 260 infrastructure = 711, with application/infrastructure rerun after reset changes. Independent exact-build browser checks passed all three targeted tests. Full new CI results and Pages must be appended to the release checkpoint before completion.

## Quantitative accuracy limits

The displayed 0.82 m solid top is the coarse eight-layer model's contiguous fully solid control volume, not a depth-converged prediction of an actual crust front. Time refinement/conservation tests do not establish spatial depth convergence. Before calibrating crust formation, test 8/16/32/64-layer refinements at matched exact times and tight temporal caps, track solid-front/enthalpy/heat-flux errors, and quantify the staircase diagnostic's layer-thickness uncertainty. Reassess pressure/composition and mantle convection before extrapolating beyond this thin-shell envelope.

The fixed-exposure emission lookup is linear RGBA8: very faint cooled-rock incandescence can fall below quantization. The initial airless sphere is nearly uniform and the cooled reference is dark; neutral reflectance/roughness is uncalibrated. Neither screenshot is accepted as final beautiful planet appearance. Resolve perceptual precision/material calibration without changing physical temperatures or manufacturing geological texture.

## Released outcome — 2026-10-13

#207, #208 and #209 are merged with preserved ancestry. Source release **PF 0.0.42.0**, main `e26d31ac62df9a8399f740536751254bed08719a`. Corrected #209 CI `38411066125` passed 294 domain + 157 application + 260 infrastructure (711 .NET), JS checks, WASM and 43 browser regressions (five production-only skipped). Pages `38413685773` passed build, deploy and **five exact-SHA production browser tests** (1.8 minutes), including column advance/reset. #208 corrected CI `38410587096` passed 707 .NET + 41 browser, Pages `38412299210` succeeded. Earlier failure/recovery descriptions above are historical; no implementation PR remains open at this release checkpoint. A final docs-only checkpoint can advance the live badge SHA.

## Independent depth-resolution experiment (not a calibrated validation)

An isolated .NET harness used the current model, initial magma reference, 384 geographic cells, solar flux 1361 W/m², the same density/k/bottom-boundary/albedo/emissivity assumptions, 4 hours exactly and a tighter **5 s cap**. It produced:

| Layers | Fully solid top, m | Surface range, K | Bottom melt | Relative global energy residual |
| --- | --- | --- | --- | --- |
| 8 | 0.81689 | 1036.64–1050.57 | 100% | 1.47e-13 |
| 16 | 0.40845 | 839.00–853.98 | 100% | 3.76e-14 |
| 32 | 0.20422 | 775.05–797.97 | 100% | -1.13e-14 |
| 64 | 0.10211 | 808.31–832.25 | 100% | -2.90e-14 |

**Important blocker:** the fully solid diagnostic equals one cell and halves with each doubling; surface temperature also changes substantially/non-monotonically. Conservation and timestep convergence do NOT establish depth-spatial accuracy. The 0.82 m HUD value is not a reliable quantitative primary-crust prediction. Formalize this experiment as a regression/research gate, improve/control the radiating boundary/depth mesh and solid-front reconstruction, then re-evaluate pressure/composition and convection assumptions before mechanical genesis. Do not simply choose the layer count that looks attractive. The 64-layer maximum is an implementation bound, not a demonstrated convergence criterion.

The harness lived in scratch (`/tmp/pf-depth-refinement`); this table and protocol preserve its useful result if the local environment disappears. It is an independent model experiment, not extra passing CI tests. User now requests a complete handover to another model; stop new implementation and commit the verified recovery documents.
