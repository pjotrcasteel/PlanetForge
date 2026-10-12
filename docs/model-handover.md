# PlanetForge — successor-developer handover

**Verified snapshot:** 12 October 2026. **Repository:** https://github.com/pjotrcasteel/PlanetForge. **Live:** https://pjotrcasteel.github.io/PlanetForge/. **Main at handover:** `8a18b66d8a462c717728d0882bb1db3e23d4dda5`. This is a dated snapshot: **re-query GitHub for current status before coding.**

> **Mission:** An evidence-informed, deterministic reconstruction of **Earth's history from its formation and molten/magma-ocean conditions, through crust, water-bearing impacts and outgassing, atmospheric condensation, oceans, evolving geology and prebiotic conditions, stopping at the earliest *plausible* life**. A life-creation algorithm has *not* been scientifically established. The **live browser is still a separate artificial Snowball Climate Experiment**, not a playable Hadean Earth timeline. Do not conflate the two.

## Read in this order

1. `docs/earth-origin-scientific-foundation.md` — history, scientific evidence windows, uncertain alternatives, prerequisites and S0–S6 roadmap; https://github.com/pjotrcasteel/PlanetForge/issues/143 is the running science issue.
2. `docs/hero-terrain-integration-contract.md` — preserve real Hero research, 128 km parent/nested watershed/erosion, region archives, plate parcels and global/local LOD. It is a canonical *part of the planet*, not a test world to replace.
3. `docs/earth-origin-thermal-coupling-contract.md` — global energy equation, interior/environment boundary, latent **and sensible** transport and one epoch per published planet state.
4. `docs/planet-wide-terrain-roadmap.md` and `docs/planet-surface-foundation.md` — existing tectonics, surface and morphology research.
5. This file; then inspect actual source, PR heads, CI and visual artifacts.

## Confirmed live and source state

- Runtime: **.NET 10/C#, Blazor WebAssembly, custom WebGL 2, MSTest, Playwright Chromium, GitHub Actions and Pages**. Solution `PlanetForge.slnx`.
- Main browser `src/PlanetForge.Presentation.Web/Pages/Home.razor` starts the current `FrozenWorldMission` / `PlanetRun` with **SNOWBALL EXPERIMENT** label, not hot Earth. The current playable stage transitions go through Melting, Active Water, First Life, Marine Animals, Land Vegetation and Land Animals. **Those later labels are scaffolds, not simulated life or evidence-based biological events.**
- `PlanetScenarioCatalog.IntendedDefault` = EarthOrigin (not runtime available). `CurrentPlayable` = FrozenClimateExperiment. `PlanetOriginClock` handles absolute geological time; **game years, frame time and a 2 Myr terrain preview are not Earth's 4.54 Gyr history.**
- Planet's elevation, geology and physical sea level use existing `PlanetTerrainRegionProvider`, `ProceduralPlanetElevationSource`, `PlanetExperience`, `PlanetHypsometryCache` and shared orbital/local mesh pipelines.
- Physical ocean became **visible and connected** after PR #142 (bedrock/water depth-buffer correction), with physically connected ocean area validated by #152 and #153. The user supplied actual iPhone Safari images of PF 0.0.38.0, seed `24061984`: plausible blue oceans and coastlines in Melting/Geology ON/OFF. **This does not prove real continents have evolved over geological time.**
- **Open visual flaw:** Snowball Year 0 looks like reddish bare continents and deep blue open oceans. `planetRenderer.js` hardcodes `geologicalPreviewMode = true`, bypassing normal climate/ice material. `FrozenWorldMission.InitializeInheritedSnowballCryosphere` separately imposes 100% sea/land ice and snow fractions, while the water phase's **actual ice mass is independent**. Mass/area bound PR #155 has merged, but actual physically consistent cryosphere/ice renderer is **not** done. See #148. Do not fix with arbitrary white shaders or a fake initial frozen Earth.
- Other open visuals: Hero ridges/morphology not yet realism-approved, off-globe/detached rivers in older screenshots, slow iPhone loading, blurry local LOD. Preserve all existing Hero work. See #135, #162, #163.

## Completed domain-physics building blocks (on main)

`src/PlanetForge.Domain/Origins/`:

| Existing model | Scope / **not yet** |
| --- | --- |
| `PlanetScenarioCatalog`, `PlanetOriginClock` | Distinct scenarios, absolute age; not yet the default runtime |
| `PlanetThermalEnergyBudget`, `PlanetRockEnthalpyModel`, `PlanetOriginThermalTimeline` | Impact/radiogenic/source/loss energy accounting, effective melt fraction and geological clock; not calibrated Hadean magma dynamics |
| `PlanetWaterInventory` | Explicit delivered H2O, hydrated mantle, dissolved magma, atmospheric vapour, liquid, ice, groundwater and escaped mass; outgassing does **not** create water |
| `PlanetWaterVaporSaturation` | IAPWS liquid–vapour equilibrium pressure from 273.16–647.096 K; bounded diagnostic, not atmospheric transport |
| `PlanetWaterPhaseTransition` | Proposed phase transfers plus latent heat, but not actual rates/eligibility |
| `PlanetEnvironmentalEnergyBudget` / `PlanetWaterEnvironmentalState` | Named atmosphere/ocean/near-surface sensible energy and relative phase enthalpy; not mantle energy |
| `PlanetEnvironmentTemperatureModel` | Effective heat capacity = Σ(m×cp) and bulk diagnostic Kelvin; no greenhouse/lapse-rate model |
| `PlanetRadiationBalanceModel` | Solar absorption at πR² and Stefan–Boltzmann emission at 4πR²; emission temperature is not automatically the ground T |
| `PlanetOriginEpochState` | On `main`, advances interior energy and water; the in-progress branch integrates the global energy ledger under one revision. Neither is connected to live game, geography and climate yet. |

Related: `PlanetCryosphereMassBudget` prevents making ice-covered area independent of frozen-water mass; physical ocean hypsometry and connected-area surveys are in Application and shared spherical domain geometry.

**Past merged milestones:** #144 scientific scope; #145 scenario+clock; #146 interior heat; #147 water reservoirs; #149 rock latent melt; #150 thermal timeline; #151 saturation; #154 atomic epoch; #155 ice bound; #156 water-phase latent energy; #157 environmental stores; #158 heat capacity/temperatures; #159 radiation; plus the previously open thermal-coupling PRs #160/#161 and the atomic Earth-origin epoch world state integration now merged on `main`. Hydrology/ocean: #136–#142 and #152–#153. A **merged isolated model is not a completed coupled early-Earth simulator**.

## PRs — verified 12 October 2026

- No PRs are currently open. PRs #160 and #161 are merged; GitHub reports merge commits `54f5767` and `97fcae1`, respectively.
- The current implementation branch is `feature/earth-origin-epoch-energy-ledger`, based on `main` at `8a18b66`. Its changes are unmerged and have only had the targeted domain test suite run so far.

### Merged PRs and follow-up

- **[#160 — sensible heat carried by steam/liquid transfers](https://github.com/pjotrcasteel/PlanetForge/pull/160)**: merged into `main`. Mass and sensible heat move together, with separately accounted latent heat; the well-mixed effective-cp approximation has no precipitation kinetics.
- **[#161 — radiative external-energy time integration](https://github.com/pjotrcasteel/PlanetForge/pull/161)**: merged into `main`. Tracks stellar joules in, infrared joules out, named environmental emitting/absorbing stores, finite timesteps and rollback. There is no calibrated early-Sun/atmospheric greenhouse/timeline.
- `PlanetOriginEpochState` (merged earlier as #154) originally advanced interior energy and conserved water independently.
- **M1 (done): ice sensible-heat store.** `PlanetEnvironmentHeatReservoir.SurfaceIce` holds the sensible energy of frozen water; its diagnostic temperature uses ice mass × `IceSpecificHeatJoulesPerKilogramKelvin` (default 2,100 J/kg/K). `PlanetVaporLiquidSensibleTransport` was renamed `PlanetWaterPhaseSensibleTransport` and now covers melt, freeze, sublimation and deposition. `PlanetRadiativeEnvironmentState` no longer rebuilds the budget from three hard-coded stores (which would have silently dropped ice heat).

- **M2 (done): `PlanetOriginEnergyLedger`.** One residual: `initial + impact + radiogenic + stellar − infrared − stored = 0`, where stored = interior + environmental sensible + relative latent. Interior heat reaches space only via the environment. Rejects heat into stores without matter, over-emission and cancellation without publishing. Fixed operator order per step (interior → radiation → phase changes).

**M3 (in progress on the branch above):** `PlanetOriginEpochState` now wraps the global energy ledger and publishes one revision across clock, interior, environment, water, phase enthalpy and radiation. Explicit solver-supplied substeps can be applied atomically as one revision. The state does **not** choose adaptive durations or detect saturation crossings: current saturation is only a pure-water equilibrium diagnostic, with no coupled pressure/temperature rate solver. The old mass-only `PlanetOriginWaterChange` epoch API was removed rather than silently bypassing the combined energy ledger; explicit delivery/outgassing/escape must be restored with well-defined material-energy transfers.

**M3 status (end of session):** local validation green — Domain 185/185, Application 125/125, Infrastructure 251/251 (Release; Domain needed `-p:NuGetAudit=false` because a private feed was unreachable for the NU1900 audit). The changes were left **uncommitted** on `feature/earth-origin-epoch-energy-ledger` because git staging/commit was denied in the non-interactive session. Resume: commit the four changed files (not `prompt/`), push, open PR to `main`, wait for the full Validate workflow including Playwright, merge.

**Next model task:** complete M3 integration (commit/PR/CI/merge), then add a process-level step evaluator that can recompute fluxes from intermediate immutable ledger snapshots. Use it to implement saturation-aware bounded condensation only after the evaluator includes the relevant atmospheric composition, temperature and explicit phase enthalpy; do not treat an equilibrium diagnostic as kinetics. Keep Earth-origin state separate from the still-playable Snowball experiment.

## Hero-region/geology preservation — strict contract

Preserve `PlanetHeroSiteSelector`, `PlanetHeroRegionBuilder`, `PlanetHeroRockResistance`, regional watershed/erosion/sediment models, `PlanetGeologicalRegionHistory/Archive/Repository`, conservative `PlanetCrustParcelCatalog/OwnershipHistory/AccumulatedDeformation` and canonical `PlanetTerrainRegionProvider`. Hero research is geographically anchored seed/epoch-dependent **physical geology**, not a cosmetic viewport or separate planet. All orbital/LOD 0–3 views must see the same evolved bedrock and water. Erosion iterations have no physical-year meaning until rate-law calibration. Old pillar/rounded ridges, local water continuity, sediment conservation and tectonic uncertainty all still require further improvement.

## Invariants no successor may weaken

- **H2O:** `new mass = old + documented deliveries - documented escape`. Interior→steam→ocean→ice are transfers, not creation.
- **Global energy:** `new total = old (interior + environmental + complete material/phase enthalpy) + external stellar/impact/radiogenic input - external infrared/escape loss`. Interior→surface heat transfer **debits interior and credits environment**, it is not already IR emission. No double counting `SurfaceHeatLossWatts` and space radiation.
- **Phase changes:** coupled *mass + sensible + latent enthalpy*, consistent reference, physically named reservoirs, nonnegative balances. IAPWS validity, pressure, composition, radiative transfer and finite kinetics **before** automatic rain.
- **Geological state:** monotonic absolute clock, revisioned immutable snapshots, deterministic seeds, atomic failed/cancelled steps; LOD/geology/sea-level/sediments/ice in the same published epoch.
- **Scientific uncertainty:** Hadean tectonics, water timing, exact thermal history and abiogenesis are not uniquely known. Simulate plausible evidence-bounded hypotheses/ensembles; do not assert fiction as fact.
- **Rendering:** derived from state, not independent stage timers, white paint, fake blue curves, manually assigned seas or separate Hero implementations.

## CI and release operations

```bash
dotnet restore PlanetForge.slnx
dotnet build PlanetForge.slnx -c Release --no-restore
dotnet test PlanetForge.slnx -c Release --no-build
dotnet run --project src/PlanetForge.Presentation.Web/PlanetForge.Presentation.Web.csproj
# Browser tests from tests/PlanetForge.Browser.Tests:
npm install --no-audit --no-fund
npx playwright install --with-deps chromium
npm test
```

`.github/workflows/csharp.yml`: PRs to `main` changing `src/**`, `tests/**`, solution/config/workflows; restore, build, JS syntax, .NET tests, WebAssembly publish, Chromium Playwright visual tests, artifacts `generator-visual-regression` / `planetforge-debug-web`. **Do not merge at the .NET green stage if Playwright is still running.** Inspect screenshot artifacts for actual visual work: an earlier trivial ocean pixel-count test passed while screenshot oceans were fragmented.

`.github/workflows/pages.yml`: push to `main`, build/test/publish to GitHub Pages, replace base `/PlanetForge/`, embed authoritative `wwwroot/js/buildVersion.js` version and short Git SHA, run a deployed Playwright smoke check. User tests iPhone Safari on meaningful **deployed** visuals, not domain-only PRs. Last verified Pages main run at snapshot: **38208066033** succeeded for `08aaff9c`. `docs/**` is not included in the PR validation workflow path filter—documentation changes alone will not auto-trigger PR CI.

## Recommended scientific next steps

1. Complete and validate the epoch-owned global ledger integration; review any resulting PR and full CI before merge.
2. Build an evaluator for atomic, solver-recomputed substeps, then couple saturation to an explicitly bounded atmosphere and phase enthalpy model. Add calibrated kinetics only with sourced assumptions; do not imply that substeps alone prevent crossing saturation.
3. Restore explicit water delivery, mantle degassing and escape inside the unified epoch only with sensible/latent energy terms and a closed mass+energy ledger.
4. Provide Hadean evidence-constrained initial conditions and uncertainty ensembles for accretion/impact heating, mantle heat/melt, volatile delivery and retention, young Sun, radiative boundary.
5. Extend the canonical epoch to geology and hydrology only when they can publish atomically with the same state; preserve the existing Hero terrain work, old save schema and current climate experiment.
6. Only after models justify it, show a genuinely evolving molten Earth → cooling crust → steam → rain → basin oceans → geological landscapes → *plausible* prebiotic/first-life conditions. Fix Snowball ice separately (#148). Avoid cosmetic tabs or scripted life.
7. Later revisit Hero ridge realism (#162) and orbit-to-ground rivers/lakes/navigation (#163) on modern main, with mass conservation and iPhone visual validation.

**User expectations:** They say “continue” and delegate concrete implementation, CI/CD and merges. Work independently, preserve already completed Hero work, show real incremental improvement when justified, explain accurately what is merged vs staged, and take time. No Wednesday/demo deadline takes priority over the foundation. The main physical 3D planet and its consistent LOD are paramount.

## Copy-paste resume instruction

> Continue `pjotrcasteel/PlanetForge` from GitHub's current `main` and re-check live PRs and Actions. PRs #160/#161 are merged. The unmerged `feature/earth-origin-epoch-energy-ledger` branch wraps the global energy ledger in one immutable epoch revision and supports explicit atomic substeps; targeted domain tests passed, but full validation and review remain. Finish that milestone, then implement process-recomputed saturation substeps and correctly energy-coupled volatile delivery/degassing/escape. The true product is a conserved, science-grounded Earth-origin timeline from molten formation to earliest plausible life; the live Snowball experiment is separate. Never rebuild Hero terrain, fake planet-wide geology or claim hidden physics is deployed. Check global H2O/energy conservation; use existing canonical geography and LOD. Request iPhone screenshots only for meaningful new deployed visuals.

**End of dated handover. Inspect GitHub live state before proceeding.**