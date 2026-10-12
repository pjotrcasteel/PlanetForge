# Earth-origin shared render payload — M5d

The canonical Earth-origin snapshot is not merely a shader annotation. While an immutable `PlanetOriginEpochState` is published, all **main-planet consumers of `PlanetRenderSnapshot.Water` and `PlanetRenderSnapshot.Atmosphere`** receive mass and pressure from that *same* accepted geological epoch.

## Projection

- The existing `PlanetOriginSurfaceProjection` remains authoritative for real `AtmosphericVapor`, `SurfaceLiquid`, `SurfaceIce`, noncondensable gas, rock enthalpy and geological clock.
- `WaterPhaseSnapshot` is populated with those three surface-accessible masses and their fractions. Water dissolved in magma or mantle **never** enters this payload or its sea-level calculation.
- `WaterPhaseSnapshot.LiquidWaterStable` remains false, and its boiling point remains the existing zero-value **unknown sentinel**, until a composition/pressure-aware phase solver actually validates this world. SurfaceLiquid already in a research snapshot is an accounting fact, *not* a certified stable ocean.
- `AtmosphereSnapshot.SurfacePressurePascals` is the hydrostatic column pressure from the origin's dry-gas + steam inventory and this planet's actual radius/gravity. `EarthAtmosphereMasses` derives from exactly that sum.
- `PlanetRenderSnapshot.SeaLevelMeters` uses this same water payload and the existing physical terrain hypsometry.
- Leaving Earth-origin mode restores the untouched legacy frozen-climate water/atmosphere payload. Other legacy climate-composition and radiative-forcing fields remain compatibility data; **they are not calibrated Hadean compositions** and must not be consumed by the future Earth-origin atmosphere closure.

## Validation

Application MSTest enforces agreement across `Origin`, `Water`, `Atmosphere` and sea level; tests also verify unknown phase stability, dry/steam-only epochs, and restoration of the legacy experiment. No additional UI or independent simulation state is introduced.

This eliminates a multi-consumer source-of-truth leak before opt-in geological snapshots reach the main orbital/local 3D world. Crust, clouds, liquid stability, geography of ice and steam transport remain separate model responsibilities (#180 and #184).
