# Immutable molten-main release checkpoint — 2026-10-13

# PlanetForge — active continuation state

**Workstream:** PF-EARTH-20261013-A. **Strategy:** vertical scientific/rendering slices; one continuous canonical formation world.

## Current merged release

- Base main `317b5b9ce3584f5a0cb3f17217fe44ec96bcce98` is documentation over source `12fdc68825f518e7719e10147471b76fb238652b`, PF **0.0.44.0**. Pages **38446290792** completed successfully; build/deploy/exact-SHA production checks passed. Real-device visual acceptance remains open.
- **PF 0.0.45.0, merged PRs #222/#223:** primary page starts directly with funded molten depth columns and **never starts the unrelated FrozenWorldMission**. Play/pause, short physical steps and same-seed restart replace stage/reference buttons. Scientific details are collapsed. Explicit old references/legacy remain available for regression/research, not primary navigation.
- `PlanetFormationSimulation` advances the existing `PlanetExperience.PublishedRegionalRock`; same canonical energy/mass/depth/clock feeds the shared main/local atlas and real contraction terrain source. No scripted oceans/life or second timeline.
- Shared optics: Planck/CIE log radiance in a filterable RGBA16F lookup, fixed camera exposure, angular molten emissivity normalized to the physical grey epsilon, roughness/Fresnel/GGX reflection, and normalized optical bloom. Bloom is camera optics, **not atmosphere**. No authored lava/convection/fracture map or invented terrain. Display interpolation uses compact eight-neighbour Shepard support; only geometry stencils are cached.
- Declared initial hypothesis: 2200 K funded shell with **25% conserved geographic enthalpy redistribution**, retaining every initial owned top cell above liquidus. This is hypothetical initial heat contrast, not simulated impact/convection. Early cap 0.25 s retains fast freezing; subsequent caps 1/5 s. Optical temperature and owned top melt remain distinct.

## Cooling kernel follow-on

The merged follow-on reuses one immutable specific-enthalpy model per column, resolves old phase once, and carries accepted physical thickness from its flux validation. Native 30-second/384-column cooling publication allocates **437 MB versus 1.686 GB** at the frozen vertical-slice candidate (74% less), with bit-identical final enthalpy SHA-256, identical clock and energy residual. The reproducible tool is `tools/benchmarks/formation-cooling`; this is not browser/Safari timing. Full follow-on CI **38453951519** passed at `74db38c8b5145a5c0e0b2b85005c520ea77bfe2e`. Elapsed playback/accuracy policy also uses the same absolute clock across Julian-year boundaries, rather than wrapping to zero. The visual slice passed full CI **38453949832** at `b8a39ec77a52919f078befb576aabeddc7c22ada`.

## Validation and release gates

- Local scientific suites: 309 domain, 161 application, 263 infrastructure (733 total with the absolute-clock regression). New real-publication integration tests cover dry/molten first render, short-step phase/atlas evolution, exact clock, energy/mass conservation, funded hot depth, cancellation, replay and restart.
- Both full CI runs passed all three browser shards: **52 regressions passed, six production-only tests skipped**. Independent local follow-on: six browser tests passed; plain main URL without `visualTest` starts molten and advances without legacy buttons/errors. Local PC T+0/30/120 s captures inspected; T+120 face 1297–1415 K, top melt 6–28%. Do not confuse local captures with production or human acceptance.
- Release source `4bcdbc613b0b01741a843a0e76cbc7f04f0e73f8` is merged. **LIVE VERIFIED:** Pages **38455427294** completed successfully: build, deploy and all six exact-SHA production smoke tests. Visible badge independently checked as `PF 0.0.45.0 · 4bcdbc6`. Legacy tests use explicit `legacy=1`; the new primary path has dedicated PC/portrait/LOD regressions and production smoke.
- **Next action:** collect human visual feedback and address solver/UI responsiveness before claiming the molten milestone accepted. Request human inspection at the default main URL with `seed=24061984`, same camera: T+0, 5, 30, 120 s; Play/pause/restart and orbit/local. PC primary, Safari secondary.

## Accepted development direction

Every major milestone couples physical process → canonical state → main-planet effect → scientific/rendering tests → meaningful visual acceptance. Numerical work alone cannot complete a visual milestone. Batch coherent features and continue independent work during CI. Preserve one ongoing simulation; no user-facing state-selection buttons or fake era timers.

Following molten appearance: persistent material/crust; coupled volatiles/steam/atmosphere; actual crust mechanics/volcanoes/mountains; snow/ice; condensation/rain/connected lakes/rivers/oceans; climate-shaped environments; soil/ecology/life. These are directional slices with prerequisites, not implemented promises. Reuse Hero/hydrology/climate and the shared terrain gateways.

## Remaining physical/visual limits

The current default remains a **dry, airless ~7 m conducting shell**, not calibrated whole-Earth formation or deep convective magma. The initial sphere is physically smooth; fine lava currents, differentiated/coherent crust, crust transport and mountains are absent. Optical material parameters are explicit hypotheses, not calibrated spectral data. Do not fake these processes to make the globe attractive. Real PC/Safari appearance is a human gate. Cold-start control readiness is open: an automated click before renderer import completed was ignored; controls should stay disabled until full renderer initialization, with an explicit cold-load regression. Plain JavaScript module cache invalidation also needs an upgrade-path audit. Large synchronous early WASM batches remain a responsiveness risk: local headless Chromium took about 11 seconds for a 30-second early step, ~5.4 seconds for later 30-second steps. Native allocation savings do not establish browser FPS. Next performance work should yield/offload the funded solver while retaining atomic publication, cancellation, deterministic enthalpies and one clock.

Depth-refinement/conservation evidence remains in [solidification snapshot](PF-EARTH-20261013-A-20261013-solidification.md). Earlier releases, failures and architecture survive in [archived checkpoint history](PF-EARTH-20261013-A-20261013-pre-vertical-history.md) and the earlier dated handovers. Shader/detail texture must never substitute for funded physical heat, material or topography.
