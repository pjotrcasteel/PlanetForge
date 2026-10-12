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

## Epoch-ledger publication

`PlanetOriginEpochState` wraps `PlanetOriginEnergyLedger`, so its published
revision contains the same clock, interior timeline, environmental sensible
stores, water inventory, relative phase enthalpy and radiative account. A
successful `Advance` publishes one new immutable revision. `AdvanceSubsteps`
accepts caller-supplied physical intervals and publishes them together as one
revision; a failed or cancelled interval publishes none of them.

Substep durations, fluxes and phase proposals are not inferred by the epoch
state. The caller must recompute process inputs at each intermediate state.
The current saturation component is an equilibrium diagnostic, not a process
solver, so there is not yet an automatic saturation-crossing guard or a
condensation timescale. The current epoch API also does not accept the former
mass-only delivery/degassing/escape proposals: they must be restored only with
explicit matter-energy transfer terms that preserve the global ledger.

## Integration sequence

1. Calibrate effective dry atmospheric mass, H2O-vapour specific heat, liquid
   water heat capacity, surface-rock active mass and interior thermal properties,
   with evidence/provenance and uncertainty.
2. Establish stellar luminosity/orbit history and outgoing-radiation boundary;
   constrain albedo, emissivity and the relationship between effective
   emission temperature and atmospheric/ocean/surface temperatures.
3. The ledger now carries mass, sensible and latent heat for the proposed
   vapor/liquid/ice phase transitions, using a well-mixed effective-cp
   approximation. Supply validated rates and test phase reversibility,
   conservation, mixed reservoirs, zero-mass stores and substep convergence.
4. Add pressure-dependent saturation/condensation kinetics, precipitation and
   ice mass/area consistency, then persistent oceans over canonical basins.
5. Couple water delivery, mantle degassing and escape with explicit matter
   enthalpy, then add physically plausible Hadean parameter ensembles and
   evidence bounds. The epoch wrapper alone does not supply these process laws.
6. Finally feed the resulting material states to the **existing** main
   planet, Hero region and LOD paths; visual molten-to-rock-to-water
   transitions must be outputs, never independent animation scripts.

## Scope boundary

The Snowball Climate Experiment is a later optional intervention scenario,
not the hot start of the Earth-origin reconstruction. The eventual Earth-origin
journey stops at scientifically plausible prebiotic-to-first-life conditions,
not scripted animals or land vegetation. A Wednesday demonstration does not
change the above acceptance gates.
