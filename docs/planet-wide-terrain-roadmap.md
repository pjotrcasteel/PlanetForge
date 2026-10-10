# PlanetForge — from Hero research tiles to one planetary terrain

**Status:** Architecture milestone A1 in progress. PF 0.0.37.15 remains the live release; the unmerged geological mountain/valley work in draft PR #113 remains separate.

## Decision

The Hero Terrain Lab is an observer and test environment, **not a second planet generator**. Every point on the sphere must have one stable geological identity regardless of camera, mesh resolution, save/load, seed, or whether a research tile has been inspected before.

A planet-wide geological elevation source already exists in `ProceduralPlanetElevationSource`. The cube-sphere `PlanetSurfaceTileSampler` and local `PlanetLocalSurfacePatchSampler` both sample `IPlanetElevationSource`. `PlanetRegionalGeologyAtlas` already blends physical, geographically anchored evolved-height deltas for overlapping neighbors and nested scales.

The missing integration contract was publication and invalidation. `PlanetTerrainRegionProvider` is the shared, application-registered gateway: it starts with the unchanged canonical source and may atomically publish a complete immutable geological atlas. Publication increments a transient revision. Both orbit and local meshes use the same provider; the orbital GPU mesh cache and local ground cache must invalidate when its revision changes. The rendered snapshot's terrain revision must change too, so water/normal overlays refresh. The original canonical source remains directly available for generating new regional research baselines; otherwise the same erosion history could be applied twice.

Research regions are **not automatically applied to gameplay** just because they were generated. The provider remains canonical-only until a higher-level simulation owner explicitly calls `ReplaceLayers`. It is intentionally not wired to a Hero button or save file in A1.

## Milestones

### A1 — Shared planetary geology provider (current)

- Single source for globe, adaptive cube-sphere LOD and near-ground terrain.
- Atomic opt-in publication of immutable, nested regional delta layers.
- Correct publication revision propagation and geometry cache invalidation.
- Preserve existing worlds when no overlays have been published; no save schema change.
- Tests for overlapping region compositing, seed isolation, coarse/ground identity, and mesh regeneration.

### A2 — Geographically addressed regional evolution

- Durable region identity based on canonical planet addresses, scale, and seed rather than transient Hero screen coordinates.
- Bound reusable region storage/cache with explicit loading and eviction; no global 257² grids.
- Replayable regional erosion snapshots tied to canonical generation and model schema versions.
- A spatial index for region queries (not an O(all regions) iteration per planet vertex).
- Deterministic overlap ownership and neighboring edge agreement independent of load order.

### A3 — Streaming and geographic LOD

- Demand-driven regional geology and tile streaming across orbit-to-ground travel.
- Cross-LOD morphing/stitching while retaining identical shared-source boundary positions.
- Mobile frame-time/memory budgets and bounded CPU sampling.
- Hero becomes a selectable region inspector on the real planet, not the proprietor of simulation state.

### A4 — Connected global hydrology and sediment

- Planet-wide drainage connectivity and correct ocean outlets.
- Cross-region runoff, sediment import/export, lakes and rivers tied to real physical terrain.
- Closed accounting for transport across simulation-region boundaries.
- Only then promote regional erosion histories to normal gameplay planet generation.

### A5 — Additional Hero inspectors

- Independent inspectable landmark types: mountain systems, river basins, lakes, volcanic provinces, coastlines.
- Use the same shared region provider, never separate source elevation or decorations masquerading as geology.

### A6 — Landform realism and world acceptance

- Multiseed, 128/32/8 km full-region comparisons against geological references.
- Remove numerical terraces and isolated trenches, maintain physical mass budgets.
- Check stable landmark identity across all zoom levels and repeated visits.
- Gate releases on visible realism and mobile performance in addition to tests.

## Out-of-scope for A1

A published atlas is a height composition, **not** global erosion physics. Seamless height blending does not prove sediment flux conservation. Region publication is in-memory and opt-in; no implicit persistence, background generation, multi-user synchronization, or full-world dense mesh. Until A4 and the visual quality gate pass, the gameplay planet continues to use the original canonical terrain by default.
