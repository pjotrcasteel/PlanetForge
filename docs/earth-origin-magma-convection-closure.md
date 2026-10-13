# Finite-mantle magma convection: thermal boundary vertical slice

Workstream `PF-EARTH-20261013-A`, PF 0.0.47.0 **candidate**. This is a physical coupling extension of the existing Earth-origin world, not a visually scripted stage.

## State ownership

The already-funded `PlanetOriginThermalTimeline` supplies the mantle temperature and finite energy. The already-funded `PlanetOriginRockColumnField` supplies each geographic column's basal control volume and enthalpy. There is **no new reservoir, no mass injection, no fabricated initial crust, no second clock and no independent renderer physics**. The mantle-to-rock boundary flux enters the existing implicit depth solver; the existing `PlanetOriginEnergyLedger` records matching mantle withdrawals and column deposits. Each accepted substep recalculates the boundary from its own candidate state.

## Explicit reference hypothesis

A finite mantle layer of depth `D` and liquid-like viscosity `eta` is parameterized by the beginning-of-step area-weighted basal temperature `T_b` and canonical mantle temperature `T_m`. Define `DeltaT = max(0, T_m - T_b)` for unstable bottom heating.

- `Ra = rho g alpha DeltaT D^3 / (eta kappa)`, where `kappa` is thermal diffusivity and `eta/rho` the kinematic viscosity
- `Nu = max(1, 0.089 Ra^(1/3))` for `Ra > 1708`; otherwise `Nu=1`
- `D_effective = D / Nu`; each column's basal flux is `k (T_m - T_column_bottom) / (D_effective + half_bottom_cell_width)`
- Unstable convection is an effective heat-transfer *closure*. No velocity, convective cell morphology or advected lateral magma mass is computed. Stable/reversed temperature gradients retain their sign in the basal Fourier flux.

The reference values are `D=100 km`, `rho=3000 kg/m³`, `g=9.81 m/s²`, `alpha=3e-5/K`, `eta=0.1 Pa s`, `kappa=1e-6 m²/s` and inherited `k=3 W/(m K)`. All are **hypotheses**, not measured early-Earth values. The Nusselt relation is a standard form of turbulent parameterization but is not calibrated for this composition, crystal fraction or pressure regime. Do not claim quantified Hadean time scales.

## Vertical integration

- **Domain:** immutable, validated reference parameters and diagnostics; the current geological epoch controls flux; no unaccounted energy and no premature publication
- **Application:** molten formation selects the closure, re-resolves the mantle boundary on every accepted numerical substep; synchronous and asynchronous paths share it
- **Visualization:** the original main globe (orbit and local LOD) continues to display physically sourced temperature, melt and density contraction from that same published epoch; collapsed Details now exposes actual mantle temperature, dimensionless Nu and basal heat flux
- **Validation:** same-seed replay, sensitivity to viscosity, physical boundary-vs-conduction difference, exact mass/energy conservation, cancellation, HUD and browser regressions, full CI and exact-SHA deployed smoke; real iPhone appearance is a separate gate

## Limits and next physical work

The surface is still an approximately 7 m dry, airless, radially conducting shell. Its initial geographical hot areas are seeded enthalpy hypotheses, **not** modelled convection rolls. This slice does not model deep magma momentum, pressure/composition-dependent solidification, structural crust breakup, volatile outgassing/steam radiative-convective transfer, or plausible geological cooling chronology. The poor PF 0.0.46.0 Safari visuals are not magically fixed. Next physically supported vertical slices must tackle layer-consistent lateral energy/material transport and evolving primary crust (#184), alongside volatile/steam boundary (#180). Do not paint lava streams, cracks, clouds or modern continents without canonical physical causes.
