# PlanetForge — active continuation state

**Workstream:** `PF-EARTH-20261013-A`  
**Checkpoint:** 2026-10-13, after M2b/M2c release verification. Re-query GitHub before coding.

## Latest verified release

- Repository: https://github.com/pjotrcasteel/PlanetForge
- Source release `main`: `cf72231fcb7259534fd8d1ff78e0d2e9b8c2a0f0` ([PR #205](https://github.com/pjotrcasteel/PlanetForge/pull/205)). Later docs-only main commits must be independently checked; this SHA identifies the verified science/test code.
- Displayed version: **PF 0.0.41.0**. #204/#205 changed thermal accounting, numerical admission and release checks; they did not introduce a new visible stage or change the existing zero-conductivity scenario.
- Full PR CI: #204 run [38395003474](https://github.com/pjotrcasteel/PlanetForge/actions/runs/38395003474), #205 run [38396130480](https://github.com/pjotrcasteel/PlanetForge/actions/runs/38396130480), both **success** (build, .NET, WASM publish, full browser suite).
- Pages: #204 `33d0191` run [38397777732](https://github.com/pjotrcasteel/PlanetForge/actions/runs/38397777732); #205 run [38398882086](https://github.com/pjotrcasteel/PlanetForge/actions/runs/38398882086), both **success**, including exact-SHA deployment and production browser smoke. #205 production smoke now includes the molten scenario.
- Local .NET validation: **285 domain + 155 application + 254 infrastructure = 694 tests passed**. #205 CI independently passed the same tests and **41 browser regressions**; four production-only tests are intentionally skipped in PR CI and run on Pages.
- No open implementation PRs at the release checkpoint. A final docs-only checkpoint PR may follow; inspect GitHub rather than assuming this source SHA is HEAD.

## Current milestone and human acceptance

- **M1 molten visual integrity:** implementation deployed; **iPhone Safari appearance remains pending**. Inspect the main globe at `?visualTest=1&magmaOcean=1`, T+0/T+1h. This is the funded fully molten reference added in #202, not the older regional contrast test or Snowball.
- **M2 heat transport:** optional conservative lateral transport (#197), bidirectional vertical accounting (#204), and finite-capacity vertical stability/convergence tests (#205) are delivered. End-to-end adaptive spatial stepping and a documented coupled visual reference remain unfinished. Do not close the entire heat-transport milestone or claim magma convection.
- **M3 primary crust:** not implemented. Effective solid/melt fractions are not mechanically persistent differentiated crust.
- Last human planet feedback: PF 0.0.40.2 HUD/buttons/advance accepted, but the old regional globe looked like tinted bedrock plus one hotspot. No T+0/T+1h user acceptance of the new PF 0.0.41.0 magma-ocean appearance has been recorded. The user's later screenshot showed the ChatGPT activity indicator, not new planet feedback.
- Developer production Chromium check at `853a379`: T+0 **2117–2309 K**, T+1h **1931–2057 K**, both **100% melt**, accepted step and no page errors. A 390 px Chromium viewport is not a real Safari measurement.

## Delivered this session

- #204: typed environmental→interior heat return, separate gross internal heat counters, matched debits/credits for hot and cold regions, history retained through volatile transfers, direct interval integration avoiding cumulative-export subtraction. Overdraw/nonfinite/cancellation fail without publication.
- #205: vertical conduction must satisfy `conductance * duration / minimumHeatCapacity <= 0.5` for each cell **and the sum seen by the finite common mantle**. Sensible `mass * cp` conservatively bounds the fusion interval. Oversized steps are rejected, never clipped; exact-time timestep refinement converges in tests.
- #205: permanent exact-SHA production smoke now enters the fully molten scenario, advances its canonical heat state, checks WebGL and resets; existing Snowball/regional checks remain.
- Updated [energy contract](../earth-origin-thermal-coupling-contract.md). Historical details are preserved in [the original snapshot](PF-EARTH-20261013-A.md) and [the released heat-coupling snapshot](PF-EARTH-20261013-A-20261013-heat-coupling.md).

## Architectural and scientific boundaries

One canonical coupled world owns clock, mass, enthalpy and accepted revision; orbital/local LOD 0–3 consume projections. Heat returned to the mantle is internal transport, not impact heat, matter delivery or negative space radiation. Preserve Hero geology, hydrology, archives, existing scenario behavior and terrain publication.

The current molten start is an explicit **dry/airless hypothetical active-shell reference**: approximately seven metres at the stated reference density, a separately funded 2200 K shell and 2400 K bulk mantle, with conserved regional initial redistribution. See [magma-ocean reference](../earth-origin-magma-ocean-initial-reference.md). It is not resolved deep magma convection, a calibrated Hadean atmosphere or Earth's unique history. Conductivity remains zero in existing visual references. Radiation, chemistry, volatile rates, clouds/rain, physical new oceans and earliest life remain later work.

## Defects, risks and next steps

1. Obtain T+0/T+1h iPhone Safari screenshots of the molten mode and resolve actual visual/camera/control defects before declaring M1 accepted or changing the default. Keep the old partial-melt contrast at `?regionalRock=1` and Snowball at regular `?visualTest=1`.
2. Add bounded spatial substep selection/re-evaluation with immutable ledger/field candidates, exact elapsed time, cancellation/rollback and one atomic publication. Vertical/lateral stability bounds do not prove full operator-split radiation convergence.
3. Add a separately labelled coupled-conduction reference with explicit transport parameters and convergence measurements; do not inflate conductivity to select an artistic cooling time or call conduction convection.
4. Begin conserved primary-crust columns: source mass/enthalpy, density, composition, thickness and stable material identity; distinguish unsegregated crystals from coherent crust, and account for remelting. Publish actual relief/material through the existing canonical planet/LOD gateway without pre-existing modern continents.
5. Continue the ten-stage roadmap [#196](https://github.com/pjotrcasteel/PlanetForge/issues/196), science [#143](https://github.com/pjotrcasteel/PlanetForge/issues/143), and crust [#184](https://github.com/pjotrcasteel/PlanetForge/issues/184). Legacy Snowball cryosphere, Hero morphology, blurry/slow local LOD and river continuity remain tracked risks (#148/#162/#163); this session did not fix them.

Local runtime note: system dotnet was absent. .NET 10.0.100 was installed for verification; running test projects from a `/tmp` checkout solved workspace test-discovery failures. Local WASM MSBuild task-host startup remained environment-limited; full GitHub build/publish/browser gates passed. Do not turn these local issues into product changes or ask the user for credentials: GitHub app Git-data operations supplied branch/commit/PR writes when CLI push had no credential. Production browser networking required the configured HTTP proxy. Use the absolute Pages URL for smoke; local browser test root-relative paths otherwise target the wrong GitHub Pages location.
