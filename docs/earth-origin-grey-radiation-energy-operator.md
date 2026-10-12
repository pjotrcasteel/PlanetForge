# Earth-origin: atomic rock / atmosphere infrared energy trial (M5f)

This change builds on the separately validated [grey radiative-transfer kernel](earth-origin-grey-radiative-transfer.md), but **does not** yet advance the planet clock, mutate the shared geological epoch, or claim a calibrated steam opacity.

## One accepted physical source of temperatures

`PlanetOriginGreyRadiationEnergyOperator.Propose` reads two occupied, non-zero-capacity material stores through `PlanetOriginEnergyLedger.Temperatures`:

- `SurfaceRock`: its actual zero-reference bulk sensible energy / declared active surface heat capacity
- `Atmosphere`: the actual H2O vapor plus dry gas heat capacity and their conserved sensible energy

No shader temperature, frozen climate estimate, or independent atmosphere state can enter the calculation. The caller supplies **only** a radius, a declared grey infrared optical depth and a physical interval. Optical depth is not inferred from gas mass and therefore is **not** a physically calibrated Hadean atmosphere.

## Signed accounting

For one layer's hemispherically integrated fluxes with physical seconds `dt`:

- Surface loses `P_surface_emitted * dt`, and gains `P_atmosphere_down * dt`.
- The atmosphere gains `P_surface_absorbed * dt` and loses `(P_atmosphere_up + P_atmosphere_down) * dt`.
- Only `(P_surface_transmitted + P_atmosphere_up) * dt` leaves the planet.

These signed changes are evaluated *together* against a new `PlanetEnvironmentalEnergyBudget`. Sequential debit-first bookkeeping would incorrectly reject a cold atmosphere that is warmed by absorption within the same physical candidate. If either final donor budget is negative, or if any joules are nonfinite, the **entire candidate** is rejected. No budget, water inventory, latent energy or clock is modified in-place.

The resulting `PlanetOriginGreyRadiationTrial` reports a new candidate environmental heat budget, the exact flux provenance and the external outgoing joules. The source `PlanetOriginEnergyLedger` remains immutable.

## Deliberate integration boundary

**Do not publish the trial as a geological epoch yet.** To connect to the existing `PlanetOriginEnergyLedger.Advance`, the ledger must atomically:

1. Apply rock/atmosphere internal thermal exchange.
2. Count the derived TOA infrared joules, **once**, in `CumulativeEmittedInfraredEnergyJoules`.
3. Preserve its own `PlanetWaterEnvironmentalState.InitialCombinedEnergyJoules` via a signed external boundary exchange.
4. Add stellar absorption and any other bounded process through the same accepted step, refusing multiple simultaneous radiation providers.
5. Keep the process rates and opacity recalculated after each adaptive refinement, and only publish on completed accepted intervals.

Subsequent integration with mixed-gas steam physics requires validated optical depths, pressure/composition/temperature dependence, and a radiative-convective vertical profile. The current one-layer isothermal model is unsuitable for assigning early-Earth solidification duration or first ocean. Condensation, magma exsolution, crust and local material/terrain coupling remain #180/#184.

## Tests

Tests prove: transparent surface-only emission; heat absorption by the atmosphere; atmospheric back-radiation heating the surface while TOA still loses energy; strict negative-store rejection with unchanged source; missing-matter rejection; exact provenance and signed planetary-energy closure. These are deterministic unit tests and require normal full-browser CI before integration.
