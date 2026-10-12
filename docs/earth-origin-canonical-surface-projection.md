# Earth-origin canonical surface projection — M5a

The existing Earth-origin geological energy/water ledger can now be projected, opt-in, onto the **main 3D planet**. This milestone creates no new planet generator, tab, timeline animation, water source or climate experiment.

## Authoritative inputs

- `PlanetExperience.PublishOriginEpoch` only accepts a coupled `PlanetOriginEpochState`. It returns a regular `PlanetRenderSnapshot` with optional `Origin` metadata.
- The one epoch owns the source revision, geological clock, enthalpy-derived bulk interior rock temperature and melt fraction, surface-rock/atmospheric bulk temperature, and all six H2O mass reservoirs.
- Only `SurfaceLiquid` fills the **existing canonical terrain hypsometry**; `DissolvedInMagma`, `HydratedMantle`, steam and ice are not oceans. Zero surface liquid yields a dry planet.
- Hydrostatic atmospheric column pressure is based on the coupled dry gas plus atmospheric steam, with current planet gravity and radius. This is only a pressure projection, not a high-pressure nonideal steam equation of state.
- The main renderer uses projected liquid/vapor and surface temperature in origin mode. It never reuses a frozen experiment's snow or ice geography. The global ice account alone cannot establish where any ice lies.
- Bulk mantle melt fraction is explicitly **not** the areal fraction of surface lava or spatial crust thickness. Those require a region-based thermal/solidification model.
- Failed publication restores the previous epoch. Re-seeding, loading a legacy save and resetting Earth detach origin; the legacy climate step cannot advance a bound origin clock.
- `PlanetExperience.AdvanceOriginEpochAdaptive` advances using the **existing** bounded process callback/integrator and then attempts one atomic main-planet publication. Rejected or cancelled integration cannot show an intermediate geothermal state.

## Boundary and limitations

This is a conservative **read-only bridge** from already accepted physics to the real main-planet surface, not a new physics solver. It does not choose a Hadean initial state, heat/radiative outflow law, geothermal transfer coefficient, steam transport or condensation rate. `PlanetOriginSurfaceProjection` does not move mass or energy.

Earth-origin publication demands `PlanetHypsometryCache`; the legacy cosmetic sea-level fallback is rejected. Existing legacy gameplay continues unchanged with `Origin == null`. The origin record is authoritative for the water/rock values consumed by the renderer when present, while legacy climate values remain in the compatibility snapshot until the main gameplay loop switches scenarios.

A true molten-world presentation must wait for spatially resolved crust surface enthalpy, regional molten-area eligibility, bounded atmosphere/steam physics and real adaptive process evolution. Do not use noise-generated magma patches to conceal the missing physics.

## Acceptance

- Partially molten bulk rock is reported only from `PlanetRockEnthalpyModel`.
- Water in dissolved magma never contributes to the ocean's volume.
- Liquid H2O creates sea level by actual mass and existing hypsometry, which remains cached across frames.
- A steam-only, zero-surface-liquid epoch has no ocean, regardless of legacy climate inventory.
- A legacy un-coupled snapshot or a missing hypsometry service cannot replace the current render epoch.
- The original Snowball climate clock cannot advance an origin-bound planet.

## Next

Implement an evidence-bounded thermal/radiative and volatile-rate proposer for `AdvanceCoupledAdaptive`, validate conservation and threshold behavior over real geological intervals, then publish its accepted snapshots through this bridge. Spatial crust formation/steam clouds/ocean placement can follow once the corresponding local models exist.
