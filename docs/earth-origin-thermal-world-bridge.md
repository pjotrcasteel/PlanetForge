# Earth-origin thermal → canonical planet bridge (first vertical slice)

This milestone adds an opt-in, **model-driven** path toward the hot, partly molten
Earth-origin planet. It deliberately does not replace the playable Frozen
Climate Experiment, introduce another tab, or manufacture a time-lapse.

## Energy evolution

`PlanetOriginThermalProcess.Propose` reads rock temperature from conserved
enthalpy and environmental temperature from the *same* coupled energy ledger.
For a supplied planet radius and effective heat-transfer coefficient, interior
heat transfer is proportional to the positive temperature gradient; the
interior is debited and the atmosphere (or bare surface rock) is credited.
The outgoing infrared flux is calculated with the existing Stefan–Boltzmann
model, using explicitly configured effective emissivity and an
effective-emission-to-bulk temperature ratio. Incoming stellar, impact and
radiogenic rates are explicitly sourced. All rates are re-evaluated after each
adaptive accepted/retried step.

There is **no** inferred first rainfall, water generation, atmospheric
composition, prescribed solidification date, or forced magma appearance.
Rock melt fraction comes only from the conserved latent/sensible enthalpy
relationship. If a step overdrafts a store, existing adaptive integration
must refine or reject the *whole* epoch.

### Scientific limitations

The transfer coefficient, emitting-temperature ratio, emissivity, impact
history and atmospheric gas inventory are **scenario inputs**, not validated
early-Earth calibrations. An optically thick steam atmosphere will require
radiative-convective transfer/opacity and composition-aware pressure/EOS.
Bulk melt fraction is **not** a geographic map of crust/magma provinces,
and the preliminary WebGL material response is an illustration of that bulk
thermodynamic state, not a new terrain generator or solidification algorithm.

## Main 3D render boundary

`PlanetExperience.PublishEarthOrigin` accepts only an energy-coupled immutable
`PlanetOriginEpochState`. It publishes a single optional origin snapshot on
the existing `PlanetRenderSnapshot`; the renderer consumes **that epoch's**
rock temperature, bulk melt fraction, atmospheric temperature and H2O masses.
No artificial oceans are created: only water already in
`PlanetWaterReservoir.SurfaceLiquid` feeds the existing sea-level
hypsometry/physical-ocean pipeline. Dry steam worlds remain dry. The legacy
Snowball climate experience remains the default until the Earth-origin
scenario has physically vetted initial conditions and runtime orchestration.

The world-seed reset clears a previously published origin. An older or
duplicate geological revision is rejected. A failed projection restores the
last successful publication.

## Next dependency (not yet delivered)

Build a calibrated hot-world initializer plus bounded high-pressure steam
radiative/transport model; then resolve condensation only after validated
pressure, composition, Gibbs/phase conditions, latent and sensible enthalpy,
and finite rates. Advance this single published world through crust formation,
basins, oceans, weathering, chemistry and eventually life. Terrain revisions
must come from canonical geology, not from the render material or a second
planet state.
