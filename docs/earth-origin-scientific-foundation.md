# PlanetForge — Scientific foundation of the Earth-origin scenario

> Architectural decision, 2026-10-11. Scope: cosmological ancestry → Solar System → Earth formation → earliest plausible life. Evidence-led reconstruction, **not** a guaranteed origin-of-life mechanism.

## Why this decision exists

The existing home screen calls its starting world `FROZEN WORLD`, yet its visible surface is dry bedrock. This is because `FrozenWorldMission.StartCore` explicitly resets the Earth reference planet, moves its orbit outward, advances 50 climate years and initializes an inherited snowball cryosphere. It is a climate/terraforming **experiment**. It is not the age-zero Earth-formation scenario.

The UI currently proceeds from its early-water stage to First Life and then arbitrarily through marine animals, vegetation and land animals. Those later steps are scaffolds, not actual chemistry/evolution. The canonical default simulation must not imply that users have watched evidence-based transitions when the model merely increments a year counter.

## What evidence permits us to show

| Approximate epoch | Scientific constraint | Simulation interpretation | Certainty |
| --- | --- | --- | --- |
| 13.8 Ga onward | Cosmological expansion, primordial hydrogen and helium, stars synthesizing heavier elements | Compressed prerequisite history / initial elemental feedstock, not realtime full-universe N-body physics | High-level established history |
| ~4.6 Ga | Protoplanetary disk and formation of the young Solar System | Stellar luminosity history, solids, volatile-bearing material, impact/accretion conditions | Strong evidence, detailed trajectory inferred |
| ~4.54 Ga and early Hadean | Earth's accretion and major impacts create heat; Moon-forming impact and magma ocean are leading models | Mass/energy accretion, molten mantle, volatile atmosphere, episodic heating, core/crust differentiation | Established qualitative processes; specific rates and histories uncertain |
| Hadean, potentially by ~4.4 Ga | Ancient zircon chemistry is consistent with early liquid water and crust | Mantle degassing and/or retained/delivered water, vapour condensation, rain, basins and surface oceans as constraints permit | Indirect geochemical evidence; globally uniform ocean timing uncertain |
| Late Hadean/early Archean | Thermal and chemical gradients, ocean/crust interactions; impact flux decreasing | Water/rock chemistry, ocean/atmosphere feedback, erosion, hydrothermal conditions and prebiotic environments | Multiple hypotheses; early tectonic regime remains contested |
| ~3.8–3.5 Ga or earlier, uncertain | Possible early biosignatures and widely studied microbial evidence | Stop at evidence-calibrated plausible earliest-life conditions; no forced certainty about abiogenesis | Evidence and dating disputed; mechanism unknown |

These are context and calibration *windows*, not hardcoded per-frame story beats. The science does not support one uniquely reconstructed Earth timeline. The Earth baseline is a plausible ensemble calibrated against available observations.

## Where Earth's water came from (mass accounting, not visual tricks)

Water molecules existed in the material from which the Solar System formed. Earth's water could have arrived during planetary accretion and through water-rich planetesimals, particularly asteroidal material; some was retained in magma, mantle and hydrated minerals. Impacts may also have added water. The early atmosphere contained steam from retained volatiles and volcanic degassing. As pressure/temperature changed, vapour condensed into rainfall and liquid oceans in actual low terrain.

**Degassing moves water from the interior to the atmosphere; it does not create new water mass.** The model must account for transport, phase changes and possible escape to space. Presently PlanetForge has a globally selected initial `EarthWaterReference` hydrosphere mass with temperature-dependent phase fractions; that shortcut is legitimate for a *climate experiment* but not evidence of how the inventory accumulated in a forming Earth.

Water reservoirs for the Earth scenario:

- Water-bearing accreting solids and impactors (external net incoming mass).
- Hydrated silicates / mantle dissolved volatiles (internal bound reservoir).
- Melt-associated dissolved water (internal reservoir).
- Atmospheric H2O vapour (steam).
- Liquid surface ocean, lakes and groundwater (liquid reservoir).
- Cryosphere (ice and snow, only if thermodynamics supports freezing).
- Escaped-to-space accounting (external net loss).

Every transfer conserves total H2O equivalent, subject only to explicit delivery/escape/chemical terms. Atmospheric condensation must neither flood a dry world nor require a fictional ice age. Sea level uses the physical liquid mass and published basin hypsometry from the existing hydrology foundation, not a renderer-selected mask.

## Architecture — one world state, many models

1. **Scenario provenance:** `ScenarioId`, deterministic seed, initial conditions, reconstruction/model revision, reference evidence and explicit uncertainty. Earth baseline is default; existing frozen mission becomes a named selectable `FrozenClimateExperiment`. Future dry/ice/ocean archetypes vary boundary conditions, not copy/paste physics.
2. **Geological clock:** canonical physical time (years since Solar System formation and years before present where appropriate) distinct from player-controlled simulation steps and render interpolation. Do not conflate `YEAR 150` of the frozen experiment with 150 years after Earth's formation.
3. **Heat and rock:** accretion energy, impact events, radioactive/primordial interior heat, cooling, mantle melt fraction, crustal solidification and differentiation. Physical crust/tectonic regime gates what the renderer may show.
4. **Atmospheric and volatile inventory:** pressure, volatile species, degassing, impact delivery, condensation, re-evaporation, greenhouse effects and escape. Store *masses*, not only visual fractions.
5. **Canonical spatial surface:** geology, erosion, water volume, sea level, drainage and terrain material state share published world epoch and revision, and are consumed by both orbital and local LOD meshes.
6. **Origins of life:** represent geochemical energy and substrate availability, hydrothermal/wet–dry and other candidate niches as hypotheses with explicit uncertainties. Do not force life merely because time advanced or an ocean exists.

The UI presents the same physical simulation at different camera distances. It is not a simulation authority and must not directly spawn continents, ice, rainfall, life, or ocean coverage.

## Hard distinctions

- **Scientific process**: governed by a model with identified assumptions, units, invariants and an evidence trail.
- **Inferred historical reconstruction**: one plausible Earth trajectory consistent with observations, not the single known true past.
- **Unresolved hypothesis**: competing early tectonic regimes, timing of oceans, emergence of life, etc.; selectable future variants without pretending one is settled.
- **Presentation approximation**: camera, LOD, frame interpolation and rendering shaders that cannot invent simulation state or violate mass budgets.

### Excluded from the baseline

- Today's intentionally generated `FrozenWorldMission` is not the birth of Earth.
- Cryogenian Snowball Earth (~720–635 Ma), long **after** evidence of early life; it remains an optional much later experiment.
- Marine animals, land plants and animals are outside the first-life endpoint (retain existing prototype code if harmless but do not assert scientific simulation).
- Exhaustive cosmological calculations and astrophysical N-body integrations are not required; early cosmic history can be collapsed into validated starting-condition generators.

## Implementation sequencing

| Order | Milestone | Acceptance before advancing |
| --- | --- | --- |
| S0 | Scientific basis and world/scenario/epoch contracts | Earth baseline and Frozen Climate Experiment clearly distinguished; evidence ledger and absolute chronology defined; no save migration regression |
| S1 | Accretion, impact heat and magma-ocean cooling | energy and mass budgets; solid crust appears from predicted thermal state; no frozen label or ice absent physical conditions |
| S2 | Water delivery, mantle/atmosphere stores and volatile outgassing | conserved reservoir ledger with defined external fluxes; vapour appears through material accounting, not visual stage changes |
| S3 | Condensation, early atmosphere, ocean hypsometry | pressure–temperature phase transitions; credible oceans emerge naturally; water stored in physical basins; compare to plausible Hadean observations |
| S4 | Geological regime and geochemical evolution | mass-conserving crust motion and erosion, tested uncertain tectonic alternatives and thermal coupling, stable seeded epochs |
| S5 | Prebiotic environments to earliest life boundary | ingredients and energy sources modelled with hypothesis status and sensitivity analysis; no magic guaranteed microbes |
| S6 | Real-device chronicle and presentation | plausible orbital and LOD 0–3 visuals, iPhone Safari performance and explicit epistemic labels, no extra diagnostic tabs required |

Current water/terrain work (#135, #138, #141, #142) is still valuable as reusable physical components, but **successful oceans on an artificial Frozen Climate Experiment do not mean the Earth-formation scenario is complete**. Wednesday's demo is not a software deadline.

## Reproducible acceptance gates

- Default Earth starts with epoch-appropriate physical parameters and honest status; cannot claim the planet is frozen just because it looks barren.
- Crust does not solidify without a thermal explanation; ice does not appear without water and freezing conditions.
- Water reservoir accounting remains conservative through impacts (in), outgassing, rain, phase transitions, hydration and space loss (out).
- Ocean level and surface flooding follow liquid-water mass and current canonical basin volumes; seeded revisions invalidate physically dependent calculations.
- Any timestep touching geological epochs must be reproducible given seed, model and configuration; no visual clock-only biological transitions.
- Plausible planetary trajectories must agree with broad Earth-age and earliest-water bounds without pretending uncertain details are known.
- Save formats/versioning and the existing climate experiment must continue to work through migration.
- Real-device visual acceptance includes no floating rivers or fictitious contiguous water, consistent LOD and acceptable interaction times.

## Selected sources and evidence anchors

- NASA: [Big Bang and age](https://science.nasa.gov/mission/hubble/science/science-behind-the-discoveries/hubble-big-bang/)
- NASA: [Solar System formation](https://science.nasa.gov/astrobiology/learning-resources/alp/how-did-our-solar-system-form/)
- NASA: [Early Earth](https://science.nasa.gov/astrobiology/learning-resources/alp/earth-right-after-it-formed/)
- NASA: [Moon formation and impact models](https://science.nasa.gov/moon/formation/)
- NASA: [Ocean water sources](https://science.nasa.gov/solar-system/ocean-worlds/)
- NASA Earth Observatory: [Old zircons and possible early oceans](https://science.nasa.gov/earth/earth-observatory/ancient-crystals-suggest-earlier-ocean/)
- Nature 2026: [Competing Hadean tectonic regimes](https://www.nature.com/articles/s41586-025-10066-2)
- NASA: [Uncertainty in life's emergence](https://science.nasa.gov/astrobiology/learning-resources/alp/how-did-life-first-emerge-on-earth/)

Companion issue: #143.