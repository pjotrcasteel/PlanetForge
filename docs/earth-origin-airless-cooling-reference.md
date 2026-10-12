# Earth-origin — physically bounded airless rock cooling reference

This is the **first-principles vacuum baseline** for a molten-rock planet: a narrow and explicitly gated thermal process for the existing conservative `PlanetOriginEnergyLedger` and `AdvanceCoupledAdaptive`. It is **not** a replacement for the steam atmosphere, ocean, weather or spatial crust models that must be built for the Earth-origin simulation.

## Process

At every adaptive candidate interval, read the **same conserved bulk interior rock enthalpy** and **near-surface rock sensible energy** used by the world ledger. With an externally specified effective boundary thickness `d` and thermal conductivity `k`:

- `P_conduction = (4πR²) (k/d) max(T_interior - T_surface, 0)`.
- This is an **internal** transfer. Interior heat loses the identical joules credited to `SurfaceRock`.
- Stellar heating uses the real cross-section `πR² (1 - albedo) F_star`, credited to `SurfaceRock`.
- In vacuum, with no atmosphere/clouds/ocean/ice, outward surface radiation follows `P_IR = (4πR²) ε σ T_surface^4`, debited from `SurfaceRock`.
- Explicit impact/radiogenic source terms can be supplied by a calibrated scenario. Otherwise they are zero.

All powers are recomputed on each new candidate, including after an adaptive rejection; never interpret an arbitrary simulation tick as a geological year. The source energy ledger and geological clock publish once at the end of an accepted interval.

## Hard limits

1. **Zero gas atmosphere only:** no noncondensable mass, atmospheric H2O vapor, surface liquid or surface ice. A steam-rich molten Earth must **not** use the vacuum thermal law.
2. The bare-rock `SurfaceRock` store must have an effective positive heat capacity. Its initial energy and bulk temperature are supplied explicitly, not created by this process.
3. The one-direction heat ledger currently supports `Interior → SurfaceRock`, so a hotter surface that would conduct energy *back* into the mantle is **rejected**, rather than suppressed with a fake zero flux.
4. Conduction thickness is an **effective boundary** supplied by the scenario. It is not a regional crust map, newly resolved crust thickness, mantle adiabat or high-pressure convection law.
5. Surface emissivity and Bond albedo are explicit bounded inputs, not temperature/color shader values. No cloud, compositional atmospheric opacity or optical-depth limit is implied.
6. Trial energy overdraw triggers the adaptive integrator's bounded rejection/refinement, not invented/clamped heat. If the requested horizon cannot be admitted, the original epoch remains unchanged.

## Verification

- Analytical conduction and Stefan–Boltzmann powers for a known finite reference.
- State-dependent adaptive cooling, decreasing near-surface temperature and enthalpy-derived interior melt, one geological revision and conserved water/energy.
- Strict rejection of dry gas, steam, ocean and ice before a vacuum process can publish.
- Rejection of reverse interior flow, invalid materials, nonfinite values and zero-duration intervals.

## Where it fits

Merge this independent Domain/test-only reference without modifying the current Snowball experiment, the main 3D planet, the LOD 0–3 surface or the UX. Once the [canonical world projection](../docs/earth-origin-canonical-surface-projection.md) is merged, accepted snapshots from this physical model can be displayed there **for test worlds that satisfy the airless limits**.

The next Earth-origin scientific work remains the calibrated high-pressure mixed-gas/steam radiative-convective and volatile transport process in [issue #180](https://github.com/pjotrcasteel/PlanetForge/issues/180). Steam-world and ocean epochs must use *their own* validated pressure/composition/EOS, latent/sensible energy and phase-transport physics, not the convenient vacuum law.
