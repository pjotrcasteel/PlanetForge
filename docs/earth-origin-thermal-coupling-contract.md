# Earth-origin coupled-energy contract

This is an implementation contract for coupling the existing scientifically bounded
pieces into **one** time-evolving Earth-origin world. It follows
[the scientific foundation](earth-origin-scientific-foundation.md),
[the Hero terrain integration contract](hero-terrain-integration-contract.md)
and issues #143 and #148.

## Energy reservoirs and boundaries

| Model | Energy owned | Energy that must not silently be assigned |
| --- | --- | --- |
| `PlanetThermalEnergyBudget` | Planet interior heat due to accretion, giant impacts, radioactive heating and cooling | The entire atmosphere/ocean's heat capacity or every outgoing infrared photon |
| `PlanetEnvironmentalEnergyBudget` | Effective atmosphere, liquid-ocean, near-surface rock and surface-ice sensible energy | Mantle's whole thermal reservoir |
| `PlanetWaterPhaseSensibleTransport` | Sensible heat advected with H2O mass for all six vapor/liquid/ice transitions, plus latent heat in a named store | Rates, eligibility, or temperature-dependent cp |
| `PlanetWaterEnvironmentalState` | Explicit H2O mass plus relative latent enthalpy exchanged with a named environmental store | A calibrated evaporation/precipitation rate, heat transported with water mass, or an absolute thermodynamic enthalpy reference |
| `PlanetEnvironmentTemperatureModel` | Diagnostic bulk T from a mass-dependent effective C = sum(m×cp) and stored E | A cloud/greenhouse/lapse-rate or ocean stratification model, true equilibrium T without phase-composition enthalpy |
| `PlanetRadiationBalanceModel` | Stellar energy crossing top-of-atmosphere in, net effective infrared energy leaving | A heat source/sink to assign automatically to the interior, ocean or atmosphere |
| `PlanetOriginEnergyLedger` | One closed account: interior + environmental sensible + relative latent energy, with impact/radiogenic/stellar in and infrared out | Choosing rates, step size or phase eligibility |

**Resolved in `PlanetOriginEnergyLedger`:** the interior's `SurfaceHeatLossWatts`
is used only as the interior→environment export (`PlanetInteriorHeatExport`),
debited from the interior and credited to a named environmental store. Loss to
space happens only through the environment's radiative boundary. Within one
step, operators run in fixed order (interior, radiation, phase changes); this
first-order splitting requires steps short enough not to cross a phase or
saturation threshold.

**Never double-count heat:** A mantle-to-surface conductive/convective transfer
is an **internal** energy transfer; it decreases mantle energy and increases
environment energy equally (until an independently accounted escape or loss
occurs). Thermal radiation to space is an **external** planetary-system loss.
Stellar absorption and impact delivery are explicitly sourced gains.
`SurfaceHeatLossWatts` in the original interior energy ledger must be classified
before energy is coupled: either exported to the environment with a matching
credit, or lost across an external boundary, never both.

## Joint invariants required for a coupled epoch

1. **H2O mass:** old H2O + delivered H2O − escaped H2O = new H2O.
   Mantle outgassing, evaporation, condensation, freezing and thawing move
   existing H2O; none generate it.
2. **Total energy:** interior + environment + all phase/material enthalpy
   changes only by explicitly accounted stellar/impact/radiogenic gains and
   radiated/escaped losses. Every internal transfer must appear with
   opposite signs in the owning reservoirs.
3. **Material enthalpy:** phase transitions must carry both sensible and latent
   heat, using one declared consistent reference, including advection of
   sensible energy when steam, water or ice moves between reservoirs.
   A mass-dependent heat capacity must not implicitly create or destroy energy
   when a reservoir empties or gains matter.
4. **Valid phases:** effective surface/atmosphere T alone is not sufficient;
   pressure, gravity, saturation, atmospheric composition, latent heat and
   finite kinetic/transport rates determine allowed transfer.
5. **Finite timesteps:** avoid negative source masses or energy, infinite
   powers/heat capacities, or an integration step crossing equilibrium
   without re-evaluation. Failed/cancelled epoch remains unpublished.
6. **Geological time:** use the absolute `PlanetOriginClock` and a physically
   declared timestep. Hero erosion steps, frame times and climate-scenario
   years are **not** geological years by themselves.
7. **Single planet revision:** publish only when thermal, hydrosphere,
   geological elevation, sea-level hypsometry, drainage, sediments and
   orbital/local LOD describe the same world epoch. Preserve regional
   Hero archives and the canonical terrain publication gateway.


## M3 — Atomic coupled geological epoch publication

The current model-level publication boundary is now implemented by
`PlanetOriginEpochState(PlanetOriginEnergyLedger)` and
`AdvanceCoupled(...)`. Its one revision references the **same** interior
timeline, water inventory and environmental/latent/radiative ledger. The
existing two-argument constructor remains for historic thermal/water state,
but that mode cannot be advanced through the coupled ledger and the coupled
mode rejects the former unaccounted thermal/water step.

A publication can contain multiple explicitly supplied `PlanetOriginEnergyStep`
substeps. Each supplies its own irradiation, impact/radiogenic power, interior
export and phase proposals. The candidate state is advanced in isolation;
any rejected power, water overdraw, reservoir with ghost heat, clock overflow
or cancellation prevents *all* substeps from publishing. One accepted list
increments the geological revision once.

**Unresolved physical prerequisite:** subdividing caller-supplied steps is
not yet an adaptive equilibrium solver. No substep limit, automatic
saturation-crossing detection, precipitation kinetics, greenhouse closure,
pressure/composition calibration or Hadean reference conditions is claimed.
A later process integrator must recompute the radiation and phase rates
at each physical boundary, detect impending thermodynamic thresholds and
refine the step before proposing the next publication. Until then, the
caller owns physically appropriate substep intervals. The existing Snowball
Climate Experiment and main-planet render path remain separate.

**Next:** implement a bounded saturation-aware process scheduler whose
eligible phases and step constraints derive from explicit atmosphere pressure,
composition, temperature and IAPWS validity. Do not invent condensation
at a chosen chronological milestone or equate local LOD snapshots with
this model-only epoch publication.

## M4a — Deterministic sub-year energy and geological integration

Physical process integration now accepts a `PlanetOriginDuration` containing
whole Julian years and an exact microsecond remainder (365.25 days per year).
`PlanetOriginClock` stores an integer-year absolute Solar System age and a
microsecond offset **separately**, so one-second or one-hour steps remain
representable after billions of years without floating-point timestamp drift.
The legacy `YearsBeforePresent` and `PlanetGeologicalEpoch` remain
whole-year geological labels rather than sub-year solver intervals.

`PlanetOriginThermalTimeline.Advance(duration,...)`, the existing
`PlanetThermalEnergyBudget.AdvanceSeconds`, and the coupled
`PlanetOriginEnergyLedger` consume the **same explicit physical seconds** for
impact/radiogenic inputs, interior export, stellar absorption and infrared
emission. `PlanetOriginEpochState.AdvanceCoupled` permits positive sub-year
duration steps and still commits one epoch revision only after all substeps
succeed. Whole-year constructors remain valid for source compatibility.

An interval submitted through `PlanetOriginDuration.FromSeconds` is rounded
once to the nearest microsecond (half away from zero); an unrepresentably short
positive interval is rejected. Longer intervals use whole years and a bounded
remainder. No double-precision absolute geological timestamp is introduced.

**Not yet physics-automatic:** creating sub-year intervals is not adaptive
substepping or a pressure/saturation/greenhouse closure. Process solvers must
still select the duration and provide physically valid, re-evaluated boundary
powers and phase proposals. Region archives, physically calibrated erosion
age and the live Snowball Climate Experiment are unchanged.

Next: pressure/composition-aware atmospheric eligibility and an adaptive
proposal integrator that brackets changing saturation and energy budgets
without crossing a threshold. Only later publish the coupled Earth-origin
state into the canonical planetary terrain/water presentation path.

## Integration sequence

1. Calibrate effective dry atmospheric mass, H2O-vapour specific heat, liquid
   water heat capacity, surface-rock active mass and interior thermal properties,
   with evidence/provenance and uncertainty.
2. Establish stellar luminosity/orbit history and outgoing-radiation boundary;
   constrain albedo, emissivity and the relationship between effective
   emission temperature and atmospheric/ocean/surface temperatures.
3. Add an explicit **mass-and-energy-coupled** atmosphere/ocean exchange
   (including sensible advection). Test phase reversibility, conservation,
   mixed reservoirs, zero-mass stores and timestep subdivision.
4. Add pressure-dependent saturation/condensation kinetics, precipitation and
   ice mass/area consistency, then persistent oceans over canonical basins.
5. Integrate coupled steps into `PlanetOriginEpochState`, under one
   atomic epoch/revision transaction, with physically plausible Hadean
   parameter ensembles and evidence bounds.
6. Finally feed the resulting material states to the **existing** main
   planet, Hero region and LOD paths; visual molten-to-rock-to-water
   transitions must be outputs, never independent animation scripts.

## Scope boundary

The Snowball Climate Experiment is a later optional intervention scenario,
not the hot start of the Earth-origin reconstruction. The eventual Earth-origin
journey stops at scientifically plausible prebiotic-to-first-life conditions,
not scripted animals or land vegetation. A Wednesday demonstration does not
change the above acceptance gates.
