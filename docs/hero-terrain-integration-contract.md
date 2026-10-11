# Hero research is a physical region of the main planet

The Earth-origin roadmap (#143) **extends the existing terrain architecture**. It does not replace Hero research, nor create a separate generation algorithm for the new chronology.

## Existing investments preserved

| Existing component | Role in the eventual Earth-origin simulation |
| --- | --- |
| `ProceduralPlanetElevationSource` | Reference physical bedrock, where research has not published evolved geology |
| `PlanetHeroSiteSelector`, `PlanetHeroRegionBuilder` | Select and simulate a physically anchored research catchment; preserve the 128 km parent and finer nested hydrology work |
| `PlanetHeroRockResistance`, `PlanetHillslopeMassWasting`, `PlanetValleyBankCarver` | Reusable physical material, gravity-driven transport, and channel-geometry algorithms |
| `PlanetRegionalGeologyEvolution`, `PlanetRegionalWatershed` | Local erosion, water routing, deposition, sediment export and mass-balance validation |
| `PlanetGeologicalRegionHistory`, `PlanetGeologicalRegionArchive`, `PlanetGeologicalRegionRepository` | Stable region identity and immutable epoch snapshots; migration-safe replay |
| `PlanetCrustParcelCatalog`, `PlanetCrustOwnershipHistory`, `PlanetCrustAccumulatedDeformation` | Moving geological material, plate ownership and conservative deformation; not a substitute for tested planet-wide tectonic history |
| `PlanetTerrainRegionProvider`, `PlanetRegionalGeologyAtlas` | **Single canonical publication gateway** for the orbital planet and local ground mesh |
| `PlanetSurfaceMeshCache`, `PlanetLocalSurfacePatchSampler` | Renderer and LOD consumers of the same published terrain revisions |
| `PlanetHydrologyCacheKey`, `PlanetHypsometryCache` | Invalidate drainage/basin volume calculations when geology or erosion changes |

## Non-negotiable integration rules

1. A Hero site is a **geographically anchored, seed-stable research window**. It is not a separate procedural world, and a cube-sphere tile is not a tectonic plate.
2. Reuse the published physical elevation source for global, regional and close-up LOD. Every layer that changes elevation must declare which geological epoch, world seed and model version produced it.
3. A Hero erosion iteration is **not** a unit of geological time. A numerical solve receives a physical timestep only when the underlying rate laws have been calibrated and the mass/energy budgets tested.
4. A research preview can compare before/after geology but must not mutate the live planet implicitly. Publish validated, compatible immutable histories atomically through `PlanetTerrainRegionProvider`, with cache invalidation.
5. Erosion must conserve or explicitly export sediment; river flow must be connected across regions before global promotion. No blue decoration or unrelated water lines.
6. The origin chronology's absolute age and scenario identity belong to the simulation, not to camera state, draw mode or the Hero preview UI.
7. Existing Hero tests for channel selection, nested parent overlays, rock resistance, hillslope mass wasting and geological archives must stay in CI. Do not delete tests or completed features to accelerate an Earth-origin prototype.
8. Rendering must be evidence-aware: a hot molten surface, solid rock, vapour, ice, rainfall and oceans are **derived** from physical state. Research visualizations may show hypotheses, clearly labelled as such.

## Integration order

- Keep established Hero algorithms and snapshots operational through the scenario/clock milestone.
- Introduce a geological-epoch-driven world state and scientifically sourced thermal/water inventories.
- Apply rates to existing rock resistance, hillslope, drainage and sediment algorithms only after physical-rate calibration.
- Compare local Hero catchments, neighbors and orbital LOD at the **same epoch** before publishing to gameplay.
- Improve visual realism on the main planet only after its physical state can support the appearance.

Read together with `docs/planet-wide-terrain-roadmap.md` and `docs/earth-origin-scientific-foundation.md`.
