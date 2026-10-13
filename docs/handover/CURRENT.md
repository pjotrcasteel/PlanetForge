# PlanetForge — active continuation state

**Workstream:** `PF-EARTH-20261013-A` · One canonical planetary-formation world · vertical science → state → visualization → validation slices.

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

## Immediate continuation priorities

1. **Replace the inadequate physical reference, not its shader.** Define a finite-capacity deep magma-ocean/upper-mantle coupled thermal system with stated depth, material densities, viscosity/composition, buoyancy and heat-transport validity. Conservatively couple energy/mass between deep melt and surface columns. Validate thermal time scales and supported integration step sizes; 186 seconds is not a geological epoch.
2. **Actual evolving molten state and crust:** resolve physically admissible lateral energy/mass transport, then first primary solid mass and mechanical/thermal surface structure (#184). Preserve one canonical publication and zero external energy/mass injection. Avoid faux lava streaks, prescribed fractures, arbitrary noise, and unsourced topography.
3. **Steam/volatile radiative coupling** per #180 must affect physical TOA cooling and available vapour inventory before clouds and rain can exist; do not fake steam via bloom. Human visual acceptance is a gate for each meaningful main-planet slice, orbit and local LOD 0–3, especially PC and iPhone Safari.

**Next acceptance:** distinguish liquid and solid physical materials, visible structurally evolving surface under a declared *physical* time window, deterministic replay and exact energy/mass closure, same shared globe orbit↔local, and human inspection of fixed-seed snapshots. Browser passing alone is insufficient. Do not send a new user test request until meaningful visible work is merged and Pages exact SHA verified.

## Process

Follow [CONTINUE.md](CONTINUE.md), [handover README](README.md), #196 and the scientific contracts; independently check GitHub main/PRs/CI/Pages before resuming. Batch coherent implementation and keep the handover current. No isolated feature previews, secondary planets, copied geographies, or scripted stage switching. The last verified **feature** release remains PF 0.0.46.0; a documentation-only commit does not constitute a new visual release.
