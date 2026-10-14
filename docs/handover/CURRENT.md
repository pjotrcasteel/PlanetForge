# PlanetForge — active continuation state

**Workstream:** `PF-EARTH-20261013-A` · One canonical planetary-formation world · vertical science → state → visualization → validation slices.

## Today’s playable prototype — user scope update, 2026-10-14

The user explicitly chose “Playable prototype but after that the focus remains validated physics.” This supersedes the earlier requirement to finish validating every new geological process before showing a demo. It does not make simplified processes scientifically validated.

New opt-in **main-page `?demo=1`** uses the existing main planet canvas and Play/Step/Restart controls. A single seeded dimensionless lattice owns liquid, frozen crust, advected heat, finite volcanic reservoir, and simplified convergence uplift. Neighbor transfers and freeze/remelt conserve total material; thermal losses, rheology, initial plate/stress hypothesis and relief exaggeration are not a calibrated Earth model. These states must not be injected into the .NET thermodynamic ledger. The original default scientific path remains unchanged.

`wwwroot/planetforge-demo.html` packages those exact same modules for direct offline play; regenerate with `python tools/demo/package-demo.py`. This is a packaging output, not a second independently implemented model. Drag, wheel and touch pinch operate the globe. No atmosphere/ocean/life, resolved subsurface veins, or scientific LOD 0–3 equivalence is claimed.

Validation: three Node tests cover finite nonnegative state, material closure, replay, batch equivalence, seed variation and invalid durations. The actual ES3 vertex/fragment shaders compile/link under Mesa EGL; four chronological seeded state renders produce no GL errors and were visually inspected. Local Razor/C# Compile succeeds. Full local WASM build is blocked by prohibited MSBuild task-host sockets; local browser download returned truncated archives. Two new CI browser cases exercise the main route and packaged file, controls, orbit and restart. Full CI remains a separate gate before live deployment. The preceding scientific candidate `6a8b720` passed full CI **38537484975**.

Next priority: playable human visual review, then replace prototype heat/flow/strength/uplift hypotheses with validated conservative physics, preserving the main-globe experience.

## Current continuation candidate — 2026-10-14

Draft PR #231 now targets main. Its preceding head `c178a3ffa52a31e39847da89d84490f6ce658140` passed full CI **38529393417** (build/.NET/WASM/all browser shards). The new follow-up shares one adaptive sync/async controller, yields on rejected work, adds conservative nested parent-mesh refinement, fixes the skipped two-microsecond trial, and adds an optional crystal-suspension thermal rheology. Local builds have zero warnings/errors; **19 targeted application plus 72 broader domain/infrastructure cases pass in process (91 total)**. Full follow-up CI is a separate gate.

The nested 128-layer seeded one-second case now runs at 0.1-K local tolerance. Long-time onset/front convergence remains unresolved: fine four-hour research requests exhaust their bounded trial budget; the suspension hypothesis delays apparent crust formation relative to the original fully-liquid-only scope. Primary formation's async scheduling changes, but its released physical reference is not switched. No lava velocity, mechanical crust, mountains, new visual acceptance, merge or deployment is claimed.

Read the [dated adaptive/depth/rheology checkpoint](PF-EARTH-20261013-A-20261014-adaptive-async-depth.md) and [science/limits](../earth-origin-liquid-heat-transport.md) before continuing. Next: supported long-time phase-front validation and actual material motion/crust interaction, then main-renderer orbit/local inspection. The PF 0.0.47.0 visual rejection still governs.

## Verified release and human acceptance (2026-10-13)

- **PF 0.0.46.0** merged via PR #225 as main `9e0b250f751815cd7585dad667380195f82d9622`. Full scientific/WASM/browser CI **38468827454** passed; Pages **38471352704** passed build, deploy and six exact-SHA live smoke checks. The deployed source is real and verified; the **human visual gate is FAILED**, not pending.
- User provided **six real iPhone Safari screenshots** of the deployed badge `PF 0.0.46.0 · 9e0b250` at T+0, T+1, T+6, T+36, T+156, and T+186 seconds. Initial image is a smooth **shiny/golden sphere** with blown-out yellow/white patches; cooling removes highlights, leaving brown blur and nearly black rock with orange blobs. No recognizably molten fluid, material crust breakup, true roughness/relief, volcanoes, or steam. UI controls and clock behave normally; no screenshot evidence of broken page loading.
- **Do not treat the production passing visual tests as human acceptance.** Read [the detailed 0.0.46.0 screenshot assessment](PF-EARTH-20261013-A-20261013-pf046-visual-rejection.md), issue #196 and #184/#180 comments before starting any further visual work. Previous 0.0.45.0 was likewise rejected.

## Scientific cause confirmed in code

- The default `PlanetFormationSimulation` uses the **dry airless thin conducting shell reference**, not a magma-ocean climate/geodynamics model. `PlanetMagmaOceanReference` funds `1.0e19 kg` of surface rock (about 6.5–7 m at 3000 kg/m³ over an Earth-sized sphere) with a 2200 K mass-weighted mean, and a deep 4.0e24-kg interior reservoir. The evolved surface is a 64-layer implicit depth column per geographical cell under Stefan–Boltzmann cooling and very weak conductive exchange with the interior. The latitudinal heat pattern is a funded **seeded initial condition**, not the result of moving magma.
- No physically modeled *lateral magma momentum/advection*, coherent/differentiated crust mass with mechanical stress, evolving steam radiative boundary, or condensation exists in the primary formation path. The 1950–3400 K initial surface and fixed optical exposure can produce bright near-white incandescent blobs. Once the very thin surface freezes in physical seconds, the globe darkens without generating geological features. The renderer cannot legitimately invent those features.
- Existing legacy geological/Hero/hydrological examples **are not primordial terrain** and must not be copied into the magma world. The radiating-face skin phase is massless and is **not** mechanically coherent crust. The main orbit/local projection and release UX are now usable; preserve them.

## PF 0.0.47.0 candidate — finite mantle convection, not yet released

The active development branch `feature/pf-mantle-convective-thermal-boundary` adds a validated, hypothesized Rayleigh/Nusselt boundary `Ra=rho*g*alpha*DeltaT*D³/(eta*kappa)`, `Nu=max(1,0.089*Ra^(1/3))` for unstable convection and `D_eff=D/Nu`. The reference parameters are D=100 km, density 3000 kg/m³, viscosity 0.1 Pa s, thermal diffusivity 1e-6 m²/s, expansion 3e-5/K. It uses only the existing finite mantle enthalpy and bottom layer heat-transfer flux, recalculated at every accepted substep. The main globe still receives its color, phase and terrain from one canonical publication; Details gains mantle T, Nu and basal flux. [Science and limits](../earth-origin-magma-convection-closure.md).

**Gates outstanding:** final PR full scientific + WASM + all browser CI; review and merge; exact-SHA Pages build/deploy/smoke; user Safari inspection ONLY if there is meaningfully changed actual main-planet behavior. The 0.0.46.0 visual rejection remains in effect: this thermal closure is not a fluid-flow field or mechanically resolved crust, and should not be declared visually accepted on a passing test suite.

## 2026-10-14 continuation — PF 0.0.47.0 still visually rejected

The user supplied five iPhone Safari screenshots showing **PF 0.0.47.0 · cc4c19c** at T+0, 2, 7, 37 and 187 physical seconds. The same golden/white blurred patches cool into brown and nearly black material. No lava flow, coherent growing crust, magma veins or mountain growth is visible. The mantle boundary is on main; the older “not yet released” candidate paragraph above is historical. These screenshots establish the visible deployed identity, not a fresh independent Pages smoke verification. Human visual acceptance remains **FAILED**.

An isolated correction branch `fix/pf-phase-consistent-crust-projection` fixes a display inconsistency: the CPU atlas and both globe/local fragment shaders previously interpolated temperature and melt independently. Interpolation could imply liquid below solidus or crystals above liquidus. The atlas now exports its actual material bounds and resolves equilibrium melt after temperature reconstruction; both GPU footprints repeat that material law after filtering. Older archived atlases without bounds retain their original supplied phase. This does not change canonical enthalpy, mass, clock or terrain, nor implement magma flow or crust mechanics.

Local validation: application test project Release build, zero warnings/errors; all five spatial projection test methods passed through an in-process runner (normal VSTest/MSBuild worker sockets are denied in this environment); JavaScript syntax and diff whitespace passed. Full browser/WASM CI and visual acceptance remain unverified. No new demo release or deployment is claimed. Do not ask the user to retest this small correction as if it delivers their requested geological demo.

Next substantial slice must replace the independently cooling thin shell with explicitly funded lateral melt/material dynamics and a defensible physical time window; couple persistent solid material and its relief into the existing canonical publication, then validate main-globe morphology before requesting another device check. A fixed shader or a thermal closure alone cannot produce the requested flows and mountains.

## 2026-10-14 transport continuation candidate

- PR #228's full build, .NET, WASM and browser CI **38491579292 passed**. Its material correction is included in `feature/pf-layer-material-transport`; it has not been separately released.
- Added physically overlapping depth-layer Fourier exchange across spherical neighbors, with equal/opposite joules, per-layer stability admission and a bounded integration path. Added explicit fully molten face transactions carrying owned mass plus donor-specific enthalpy. Canonical composition and the existing main-globe publication now accept their actual mass-derived relief together with the same epoch and material atlas.
- [Transport contract and limitations](../earth-origin-layer-material-transport.md). These APIs do **not** supply a pressure/velocity field, mechanically coherent crust, magma veins or a complete mechanical-energy account. Default formation remains the seven-metre reference; no synthetic flow field or visual stage has been introduced.
- Local Release domain/application/infrastructure builds passed with no warnings/errors. A broader in-process run passed all 30 column/formation regression cases, including canonical mass-to-terrain publication, unequal-depth contacts and solid-recipient rejection; full candidate CI is a separate gate. Standard local VSTest sockets are unavailable; do not call the in-process invocation a VSTest run.
- No new production or human visual acceptance is claimed. Preserve the user's PF 0.0.47.0 rejection above. The next substantial primary slice needs a physically sourced momentum/pressure mechanism and work accounting, an appropriate finite magma-ocean reference and physical clock, followed by actual surface/crust morphology and device inspection.

## 2026-10-14 pressure/work continuation candidate

- Transport PR #229 (`37c3f0e558f5e201f4e58a07bcf57cb86da9c27e`) passed full build, .NET, WASM and all browser CI **38517993944**. It is included in the subsequent `feature/pf-viscous-melt-work` branch, together with #228; no merge/deployment is claimed.
- Added physically generated low-Re hydrostatic/viscous currents, parabolic depth transport, head/mass timestep admission and a finite mechanical work account. Available work is **excess potential above the resting geoid**, funded from existing rock heat; flat surfaces cost no work and remain exactly at rest. Accepted potential loss is matched to layer heat and the canonical ledger, without a second clock advance.
- [Complete science/limits](../earth-origin-viscous-melt-work.md) and [dated checkpoint](PF-EARTH-20261013-A-20261014-viscous-work.md). The model requires equal fixed solid/liquid density and excludes mixed/solid columns explicitly; it is not a deep turbulent magma ocean, a calibrated crust-closure model or a replacement for the current unequal-density seven-metre reference.
- **71 relevant regression cases passed locally in process**, including legacy energy/water/phase/clock behavior and actual pressure-generated mass-to-main-terrain publication. Release builds passed with zero warnings/errors. Full new-candidate CI is pending separately; no fresh production/device visual acceptance is claimed.
- The immediate next substantial primary slice must replace the inadequate physical magma reference and support appropriate time/resolution plus density/phase/mechanical work coupling. Do not turn the test displacement into artificial orbital lava animation or present this limited reference as the completed demo.

## Immediate continuation priorities

1. **Replace the inadequate physical reference, not its shader.** Define a finite-capacity deep magma-ocean/upper-mantle coupled thermal system with stated depth, material densities, viscosity/composition, buoyancy and heat-transport validity. Conservatively couple energy/mass between deep melt and surface columns. Validate thermal time scales and supported integration step sizes; 186 seconds is not a geological epoch.
2. **Actual evolving molten state and crust:** resolve physically admissible lateral energy/mass transport, then first primary solid mass and mechanical/thermal surface structure (#184). Preserve one canonical publication and zero external energy/mass injection. Avoid faux lava streaks, prescribed fractures, arbitrary noise, and unsourced topography.
3. **Steam/volatile radiative coupling** per #180 must affect physical TOA cooling and available vapour inventory before clouds and rain can exist; do not fake steam via bloom. Human visual acceptance is a gate for each meaningful main-planet slice, orbit and local LOD 0–3, especially PC and iPhone Safari.

**Next acceptance:** distinguish liquid and solid physical materials, visible structurally evolving surface under a declared *physical* time window, deterministic replay and exact energy/mass closure, same shared globe orbit↔local, and human inspection of fixed-seed snapshots. Browser passing alone is insufficient. Do not send a new user test request until meaningful visible work is merged and Pages exact SHA verified.

## Process

Follow [CONTINUE.md](CONTINUE.md), [handover README](README.md), #196 and the scientific contracts; independently check GitHub main/PRs/CI/Pages before resuming. Batch coherent implementation and keep the handover current. No isolated feature previews, secondary planets, copied geographies, or scripted stage switching. The last verified **feature** release remains PF 0.0.46.0; a documentation-only commit does not constitute a new visual release.


## Upper magma inventory checkpoint — 2026-10-14

Pressure-flow candidate 5ee6685 passed build, .NET tests, WASM publish and all three browser regression shards. Added a research-only upper-magma factory that reallocates mantle mass and initial enthalpy into a declared thin upper shell. A one-kilometre reference uses quartic depth grading to resolve its initially molten radiating face. Eight targeted application cases pass via the local in-process runner; build has zero warnings/errors. See `docs/earth-origin-upper-magma-inventory.md`.

The released main globe is unchanged. Internal convective heat transport, depth/time convergence, crust mechanics and visual acceptance remain outstanding. Do not describe this checkpoint as a completed demo or enable the deeper reference merely to change brightness.


## Connected-liquid transport and numerical continuation — 2026-10-14

PR #231 now targets main so its next source push receives the normal CI gate. Added opt-in connected-liquid turbulent heat transport, a seed-preserving funded upper-shell factory, shared-reference temperature arithmetic for stiff implicit solves, and bounded thermal step-doubling error control. Accepted liquid heat exchanges conserve the existing layer/ledger accounts. No resolved turbulent velocity or mechanical crust is implied.

Local builds pass with zero warnings/errors. Fourteen targeted application cases and 72 broader domain/infrastructure cases passed through the in-process runner (86 total); local VSTest sockets remain unavailable. Evidence is recorded in `docs/earth-origin-liquid-heat-transport.md`. Uniform 32/64/128-layer one-second checks converge. Seeded coarse steps prematurely crystallized material; error-controlled results supersede that observation. Exploratory four-hour seeded runs develop solid surface layers next to liquid material, but first-solid onset is not converged and the stringent 128-layer seeded transient hits the supported microsecond boundary. Do not promote those runs to visual or geological acceptance.

Next: crust-front/onset convergence and supported initial-transient resolution, asynchronous adaptive integration preserving sync/replay semantics, then actual main-renderer orbit/LOD inspection. Released primary reference and live version remain unchanged. No merge/deploy/device-test request is claimed.

## PF 0.0.48.0 candidate — directly material-driven demo visuals (2026-10-14)

The existing draft PR #231 now uploads the *actual evolving molten mass* and active-rift field alongside the thermal/crust/uplift/flux atlas to the main-page WebGL prototype. Its fragment shader shows incandescent liquid only where the seeded demo simulation owns liquid; cooling by itself does not re-label empty cells as lava. Basalt follows frozen mass, fissure light follows a simulated active rift intersecting recently frozen margins, and mesh relief still follows simulated uplift/crust. This remains an explicitly **dimensionless playable geological hypothesis**, not the calibrated .NET formation physics; it does not have an atmosphere, steam or surface water. The released main scientific reference remains unmodified. Target release identity is PF 0.0.48.0, pending all CI, merge, GitHub Pages exact-SHA smoke and actual device inspection. Do not claim it is live until verified.

The candidate now also charges remelting locally to the heat carried by the existing molten inventory (with an explicitly dimensionless latent-cost proxy), rather than allowing hot rift-supplied magma to melt the majority of the newly formed crust at no thermal cost. Reusable transfer buffers reduce frame-step allocations. The shader uses the actual ratio of molten to frozen mass for exposure while basaltic relief remains sourced from uplift/frozen inventory. Independent fixed-seed replay, mass-closure and persistent-crust regressions are part of the CI gate. This remains **mass-conserving but not a closed physical energy model**; there is no claim of calibrated geological time or full mechanical validity.

Playback-step profiling also found an avoidable main-thread stall at 0.1-unit integration increments. The dimensionless prototype now takes at most one unit per internal tick (with its donor mass-flux cap unchanged); exact-seed fast/refined crust-coverage comparison and batch-equivalence are regression tested. This accelerates iteration substantially, but is **not** a substitute for actual iPhone frame-time/performance acceptance or scientific time convergence.

### PF 0.0.48 demo release gate — browser regression correction (2026-10-14)

Fixed a pre-existing version-pinned browser expectation in the main planet/overlay coherence spec to expect the 0.0.48.0 imports, and eliminated an archive-import test startup race by waiting for completion of the initial empty IndexedDB catalogue render. The initial stable release snapshot passed .NET, WASM, browser shard 1 and its playable formation-demo tests; browser shard 2 failed only the outdated import-version assertion, and browser shard 3 failed one archive-import status race. Re-run all CI for the updated snapshot before merging; do not label this release deployed or visually accepted until GitHub Pages exact-SHA smoke and device review.


## M0 visual-foundation implementation — 2026-10-14 draft

PF 0.0.49 candidate: material-front contrast now samples neighbors in *simulation texels* rather than screen-space derivatives, preserving coherent boundaries across camera zoom levels. Displaced-mesh normals are blended with spherical normals; reduced yellow magma saturation and bounded optical exposure preserve readable solid/melt regions. Glowing rims continue to require finite owned melt and solid/liquid interfaces. The offline prototype's outdated embedded renderer was regenerated to match the main page exactly. Browser regression now checks progression at T+0, T+30, and T+60 with a fixed camera before orbit and restart.

**Not yet accepted:** full branch CI, Pages deployment and human Safari screenshots. This changes optical presentation of the dimensionless playable hypothesis, not its mass/energy model; it must not be described as validated magma-ocean physics.


## M0 critical packaging parity fix — 2026-10-14

During candidate visual verification, the offline demo was found to embed a **128x64** older world while the main-page canonical playable model initializes **256x128**. Its renderer had also drifted before the previous correction. The complete package is now regenerated byte-for-byte from `tools/demo/package-demo.py` using both current source modules. A fast CI step reruns the generator and rejects any working-tree diff, preventing future offline/main-page source drift. No claim of actual physics calibration is made; CI/Pages/Safari gates remain outstanding.
