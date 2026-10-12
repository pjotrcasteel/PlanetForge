# PlanetForge — successor-developer handover

**Verified snapshot:** 12 October 2026. **Repository:** https://github.com/pjotrcasteel/PlanetForge. **Live:** https://pjotrcasteel.github.io/PlanetForge/. **Main at handover:** `08aaff9c14325fc75ca39bfa4764455d22315584`. This is a dated snapshot: **re-query GitHub for current status before coding.**

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
| `PlanetOriginEpochState` | Immutable geological age + interior energy + water candidate; **not connected to live game, geography and climate yet** |

Related: `PlanetCryosphereMassBudget` prevents making ice-covered area independent of frozen-water mass; physical ocean hypsometry and connected-area surveys are in Application and shared spherical domain geometry.

**Past merged milestones:** #144 scientific scope; #145 scenario+clock; #146 interior heat; #147 water reservoirs; #149 rock latent melt; #150 thermal timeline; #151 saturation; #154 atomic epoch; #155 ice bound; #156 water-phase latent energy; #157 environmental stores; #158 heat capacity/temperatures; #159 radiation. Hydrology/ocean: #136–#142 and #152–#153. A **merged isolated model is not a completed coupled early-Earth simulator**.

## OPEN PRs — verified 12 October 2026

Exactly **two** PRs remained open, both still draft/unmerged though the **full `Validate PlanetForge` workflow passed** for their specified head SHA. Review again before merging; successful past CI is not forever valid.

- **[#160 — sensible heat carried by steam/liquid transfers](https://github.com/pjotrcasteel/PlanetForge/pull/160)**: head `adb1eb3e3dad73381ab97835c0e89824272b25bf`; successful run **38208102933**. Mass and sensible heat move together, with separately accounted latent heat. Supports only vapor/liquid; **ice's own thermal store not yet modeled**. Well-mixed effective-cp approximation, no precipitation kinetics.
- **[#161 — radiative external-energy time integration](https://github.com/pjotrcasteel/PlanetForge/pull/161)**: head `2ad7f399163ea4799c62c4369cc936b15993f1b4`; successful run **38208298750**. Tracks stellar joules in, infrared joules out, named environmental emitting/absorbing stores, finite timesteps and rollback. **No calibrated early-Sun/atmospheric greenhouse/timeline**.

**Immediate next-model task:** Re-fetch these exact PRs and CI, review source/test correctness, then mark ready and merge sequentially if still valid; check newer `main` deployments. Do *not* blindly merge older stale PRs.

### PR sanitation just completed

Historical **#66, #81, #113, #134 were closed without merging or deleting branches**:

- #134 ocean was superseded by merged #142/#152/#153.
- #81 proposed orbital atlas was stale/conflicted with modern renderer/LOD.
- #113 old Hero ridges had useful research but *still failed visual realism*; preserved in **[#162](https://github.com/pjotrcasteel/PlanetForge/issues/162)**.
- #66 old orbit→local water/navigation remains relevant but implementation was conflicted; preserved in **[#163](https://github.com/pjotrcasteel/PlanetForge/issues/163)**.

Do not discard their unique experiments or resurrect their obsolete UI/version changes. Port selectively onto *fresh current-main branches* when physical tests and visual evidence justify it.

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

1. **Review/merge #160 and #161** after verifying latest head+CI and shared-energy compatibility. Keep their separate assumptions explicit.
2. Design one conservation-tested coupled atmospheric/ocean/interior **enthalpy** formulation with explicit stellar/IR boundaries and mantle↔surface transfers, including water-mass sensible heat. Avoid large physical time steps that jump over changing saturation.
3. Add composition- and pressure-constrained saturation/condensation, adaptive physical rates and mass+energy conservation through rainfall and ocean formation. Calibration needs sourced inputs; no implicit stage triggers.
4. Provide Hadean evidence-constrained initial conditions and uncertainty ensembles for accretion/impact heating, mantle heat/melt, volatile delivery and retention, young Sun, radiative boundary.
5. Extend `PlanetOriginEpochState` to atomically own thermal, atmospheric, water, geology and hydrology state. Publish one canonical epoch consumed by Hero, orbital planet and local LOD; preserve old save schema and current climate experiment.
6. Only after models justify it, show a genuinely evolving molten Earth → cooling crust → steam → rain → basin oceans → geological landscapes → *plausible* prebiotic/first-life conditions. Fix Snowball ice separately (#148). Avoid cosmetic tabs or scripted life.
7. Later revisit Hero ridge realism (#162) and orbit-to-ground rivers/lakes/navigation (#163) on modern main, with mass conservation and iPhone visual validation.

**User expectations:** They say “continue” and delegate concrete implementation, CI/CD and merges. Work independently, preserve already completed Hero work, show real incremental improvement when justified, explain accurately what is merged vs staged, and take time. No Wednesday/demo deadline takes priority over the foundation. The main physical 3D planet and its consistent LOD are paramount.

## Copy-paste resume instruction

> Continue `pjotrcasteel/PlanetForge` from GitHub's CURRENT `main`. Read this handover plus the science, thermal and Hero contracts. Check all open PRs and Actions runs; as of 2026-10-12, #160/#161 were CI-green but unmerged, and older #66/#81/#113/#134 were closed intentionally, their useful research preserved in #162/#163. The true product is a conserved, science-grounded Earth-origin timeline from molten formation to earliest plausible life; the live Snowball experiment is separate. Never rebuild Hero terrain, fake planet-wide geology or claim hidden physics is deployed. Implement narrow tested model steps; check global H2O/energy conservation; use existing canonical geography and LOD. Manage PRs, full CI and Pages validation, and request iPhone screenshots only for meaningful new deployed visuals.

**End of dated handover. Inspect GitHub live state before proceeding.**