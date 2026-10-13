# Earth origin — first spatial sensible-rock energy field (M5i)

## What the October iPhone screenshots proved

Version 0.0.39.0 actually integrates a **one-zone** physically conserved rock temperature, and its uniform orange→brown shader is only a visual mapping from that temperature. It does **not** model spatially emerging molten pools, flowing magma, solid crust, continents, volcanic edifices or any new terrain. The interior cooling is much slower because its declared mass/heat content is vast compared to the active surface store. A surface-only cooling trend must not be labeled **crust formation**.

## A conservative spatial foundation, not another GPU trick

The new `PlanetOriginSurfaceThermalField` partitions exactly **the existing canonical `SurfaceRock` sensible-energy and active-rock mass**, never creating a second initial energy source.

- Physical cell identities come from the existing **cubed-sphere grid**, independent of GPU LOD.
- Cell areas use `PlanetSurfaceCellArea` spherical solid angles and the planet's actual radius, not equal-area assumed rectangles.
- Mass and joules are assigned proportional to area, with a residual-safe final cell so neither depends on tessellation count.
- Each cell temperature is `sensibleJoules / (materialMass * specificHeat)`, never noise or a shader heuristic.
- `Propose` accepts finite, nonnegative **per-square-metre** conductive, absorbed stellar and outgoing IR rates, combines them over the exact physical duration, rejects energy overdraft and publishes **nothing**.
- The result reports global conduction, absorbed starlight and outgoing infrared joules separately, enabling the already established coupled ledger to account for each source exactly once.
- Re-running the same deterministic rate law on the same field yields the same candidate; a rejected or cancelled trial cannot mutate the input.

## What is **not** yet physically justified

This is only sensible rock energy, and deliberately does **not** pretend solid fraction from a sensible-only heat budget. The present `PlanetRockEnthalpyModel` includes latent heat for the bulk mantle, but the near-surface `SurfaceRock` reservoir does **not** yet own compatible fusion enthalpy. Calculating a "crust fraction" by thresholding `TemperatureKelvin` would violate energy conservation through solidification.

Next physical integration needs, in order:

1. Upgrade the canonical active surface material from sensible heat to composition-dependent **sensible + latent enthalpy**, with mass and stored energy accounted for **once** across regional cells.
2. Introduce conservative mantle↔surface exchange (including reverse flow where physically required), finite surface radiation and physically constrained lateral transfer. Only accepted spatial trials may update the canonical `PlanetOriginEpochState` and clock.
3. Track solidified **rock** distinctly from chemically differentiated, mechanically persistent **primary crust**. Crystallization alone does not imply continental crust, elevation or buoyant segregation.
4. Calibrate local material properties, pressure, atmosphere/steam radiative transfer and the allowable integrated step from published reference physics (#180, #184).
5. Project accepted geographically resolved local thermal/material states to the **same** main globe and LOD 0–3. GPU never authors cell states, lava patches or sediment.

This milestone intentionally does **not** attach the field to the live renderer, because no validated latent/material coupling exists yet. The current opt-in airless reference remains explicitly an airless single-temperature test, not Hadean Earth's actual trajectory.

## iPhone readability

The existing thermal reference's HUD is also shrunk into a compact instrument strip: it no longer repeats a long confirmation or grows into the sphere; elapsed time is shown as unwrapped `T+60h` instead of incorrectly becoming `12:00` after 60 hours. The scientific disclaimer stays visible. The normal Snowball experiment and geology view are unchanged.

## Tests

- Reconstruct initial surface mass, heat and temperature from the canonical ledger without a duplicated state.
- Check sum of cell spherical areas approaches `4πR²`.
- Validate physical localized solar facing/away forcing, exact-source/sink joule closure and deterministic behavior.
- Reject overdraft, malformed energy rates, excess physics-grid resolution, empty material and cancellation.
- Retain existing full WebGL/iPhone visual regression checks for the HUD change.

No aesthetic-first lava world or separate diagnostic tab is included.
