# PlanetForge — successor-developer handover

> **This is a historical long-form handover last verified 12 October 2026.** For up-to-date workstream status, version, PRs, scientific decisions, and the cross-chat continuation prompt, start with [docs/handover/README.md](handover/README.md) and [docs/handover/CURRENT.md](handover/CURRENT.md). Workstream: `PF-EARTH-20261013-A`.


**Verified successor update:** 12 October 2026. **Repository:** https://github.com/pjotrcasteel/PlanetForge. **Live:** https://pjotrcasteel.github.io/PlanetForge/. **Latest validated science-code merge:** `6741f0b9be91af4cbcc71b8b55c269af0928e222` (#171). Full CI and live GitHub Pages boot checks passed (runs 38241322921 and 38242699311); subsequent documentation commits may advance the SHA. **Always re-query the current GitHub state before coding.**

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
| `PlanetOriginEpochState` | Immutable age/revision now owns the **same** coupled interior/environment/water/radiation ledger via `AdvanceCoupled`; not connected to live game geography/climate yet |
| `PlanetOriginEnergyLedger` / `PlanetOriginDuration` | One closed relative energy account and exact microsecond/whole-year timebase; no automatic adaptive thermodynamic rates |
| `PlanetOriginVolatileTransport` | Explicit accreted H2O, magma→steam material enthalpy, escape and conservation with relative latent accounting; no calibrated magma chemistry/steam EOS |
| `PlanetMixedAtmosphereSaturation` | Dry-gas + vapor total and mole-fraction partial pressure; pure-water IAPWS-bounded **diagnostic only**, not condensation kinetics |

Related: `PlanetCryosphereMassBudget` prevents making ice-covered area independent of frozen-water mass; physical ocean hypsometry and connected-area surveys are in Application and shared spherical domain geometry.

**Merged foundations:** #144–#161 as relevant; #160/#161 are **already merged**, followed by M1 surface-ice heat and M2 global energy ledger. Successor science milestones: **#164 (M3 atomic coupled epoch), #166 (M4a physical sub-year time), #168 (M4b volatile material energy), #169 (M4c mixed-atmosphere diagnostic), #170 (M4d condensation safety gate), #171 (M4e adaptive coupled stepping)**. Hydrology/ocean #136–#142 and #152–#153 remain preserved. **Merged isolated models do not mean a coupled playable early-Earth simulation exists.**

## Successor status — verified after M4e, 12 October 2026

**No open pull requests** after #171 merged (verify again whenever resuming). The earlier references in this handover to #160/#161 being draft/unmerged were **historical snapshots**, superseded by the confirmed GitHub merges.

| Merged work | Verified baseline | What it actually delivers |
| --- | --- | --- |
| #160 / #161 | Previously CI-green; both merged | Sensible H2O phase transport and external radiative interval accounting, not Hadean calibration |
| M1 / M2 | Merged before successor | Ice sensible-heat store and the single interior + environment + relative latent ledger |
| [#164](https://github.com/pjotrcasteel/PlanetForge/pull/164) (M3) | CI 38219060756; Pages 38220225106 | One atomic coupled geological revision, water + interior + environmental + radiation with supplied substeps |
| [#166](https://github.com/pjotrcasteel/PlanetForge/pull/166) (M4a) | CI 38221125939; Pages 38222017161 | Microsecond/Julian-year physical clock; same seconds used for interior/radiative integration |
| [#168](https://github.com/pjotrcasteel/PlanetForge/pull/168) (M4b) | CI 38230367595; Pages 38231875375 | Conserved explicit delivered H2O, mantle↔magma bookkeeping, magma→steam enthalpy and steam escape, with typed source/loss accounting |
| [#169](https://github.com/pjotrcasteel/PlanetForge/pull/169) (M4c) | CI 38232032740; Pages 38232774859 | Bounded hydrostatic **mixed** atmospheric pressure and mole-fraction steam partial-pressure/saturation potential; unsupported IAPWS regimes stay indeterminate |
| [#170](https://github.com/pjotrcasteel/PlanetForge/pull/170) (M4d) | CI 38239247576; Pages 38240231947 | Opt-in pressure/temperature/time-limited condensation proposals; rejects water-mass overdraw and post-latent-heat undersaturation |
| [#171](https://github.com/pjotrcasteel/PlanetForge/pull/171) (M4e) | CI 38241322921; Pages 38242699311 | Adaptive per-candidate rate re-evaluation, bounded physical interval refinement, guarded saturation boundary crossings and one atomic geological revision |

**All listed CI runs completed successfully**, including .NET tests, WASM publish and Playwright. Pages runs above include live-boot smoke validation. Code SHA before this document update: `6741f0b`.

The domain now supports proposing an explicit accounted sequence such as delivered hydrous material → dissolution into magma → outgassing steam → escape/condensation and conservative heat exchange. **The adaptive candidate scheduler and limited optional guards are implemented, but physical rates and generally valid thermodynamic eligibility are not solved.** In particular, magma reaction enthalpy, high-pressure steam nonideal EOS/fugacity, atmospheric composition, greenhouse/vertical temperature, saturation crossing, precipitation, rainfall routing and early-Sun/impact chronology still require evidence-based models. Source: [mixed-atmosphere assumptions](earth-origin-mixed-atmosphere-diagnostic.md) and [thermal coupling contract](earth-origin-thermal-coupling-contract.md).

### Authoritative next gates

1. Explicit composition and pressure-dependent H2O thermodynamics, high-temperature/nonideal regime detection, mineral/rock enthalpy and physically sourced initial-condition ensembles.
2. Supply evidence-based **physical process rate laws** for the *existing* bounded adaptive candidate integrator. Validate nonlinear within-step threshold detection and convergence against reference thermodynamics (pressure, nonideal EOS, lapse/cloud temperatures, latent/sensible mass advection); do not script rain.
3. Atomic publication connecting *one* Earth-origin epoch to canonical geology, liquid-water inventory, hypsometry/basin connectivity, climate and the existing orbital/local LOD 0–3 surface. Preserve Hero region identity and research archives.
4. Only then visually validate the main planet evolving through melt/cooling/outgassing/rain/basin oceans. Safari visual acceptance must include screenshot evidence; no separate Earth-origin fake world, extra tabs or invented white/blue masks.
5. Revisit unresolved [#148](https://github.com/pjotrcasteel/PlanetForge/issues/148) Snowball cryosphere, [#162](https://github.com/pjotrcasteel/PlanetForge/issues/162) mountain realism, and [#163](https://github.com/pjotrcasteel/PlanetForge/issues/163) local river/lake continuity as independent, canonical-world work.

**Issues #143, #165 and #167 remain useful scientific integration records**, not proof that all their requested physics is already done.

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

`.github/workflows/pages.yml`: push to `main`, build/test/publish to GitHub Pages, replace base `/PlanetForge/`, embed authoritative `wwwroot/js/buildVersion.js` version and short Git SHA, run a deployed Playwright smoke check. User tests iPhone Safari on meaningful **deployed** visuals, not domain-only PRs. Latest successor-verified Pages run at this documentation update: **38242699311** succeeded for `6741f0b`. `docs/**` is not included in the PR validation workflow path filter—documentation changes alone will not auto-trigger PR CI.

## Recommended scientific next steps

Resume at the **authoritative next gates above**, using the current `main` commit and existing domain/terrain contracts. Don't repeat already-merged PRs #160/#161/#164/#166/#168/#169/#170/#171. Adaptive numerical stepping and mixed-gas saturation diagnostics **do not** create rain or calibrated climate by themselves. The live home page is still the deliberately separate Snowball Climate Experiment.

**User expectations:** They say “continue” and delegate concrete implementation, CI/CD and merges. Work independently, preserve already completed Hero work, show real incremental improvement when justified, explain accurately what is merged vs staged, and take time. No Wednesday/demo deadline takes priority over the foundation. The main physical 3D planet and its consistent LOD are paramount.

## Copy-paste resume instruction

> Continue `pjotrcasteel/PlanetForge` from CURRENT GitHub `main`; re-check branches, PRs, Actions and Pages first. #160/#161 and M1/M2/M3/M4a/M4b/M4c/M4d/M4e (through #171) were merged by 12 October 2026; do not rebuild those foundations. Read the Earth-origin science, thermal coupling, mixed-atmosphere and Hero terrain contracts. The playable Snowball experiment is separate from the scientifically bounded Earth-origin reconstruction. Next supply pressure/composition/nonideal thermodynamics and calibrated process/phase kinetics to the already-bounded adaptive integrator, then connect one physical epoch to canonical geology, physical oceans, Hero and global/local LOD 0–3. Do not make scripted rain, arbitrary planets, cosmetic snow/ocean overlays, unsupported first life or extra diagnostic tabs. CI and Pages must pass before merges; ask for iPhone Safari screenshots only when meaningful physical visuals have genuinely changed.

**End of dated handover. Inspect GitHub live state before proceeding.**