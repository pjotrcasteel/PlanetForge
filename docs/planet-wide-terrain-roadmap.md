# PlanetForge — from Hero research tiles to one planetary terrain

**Status:** A1 and A2.1 completed and deployed. A2.2 adds IndexedDB archive storage and a conservative spatial index. Unreleased geological realism work remains isolated in draft PR #113.

## Decision

The Hero Terrain Lab is an observer and test environment, **not a second planet generator**. Every point on the sphere must have one stable geological identity regardless of camera, mesh resolution, save/load, seed, or whether a research tile has been inspected before.

A planet-wide geological elevation source already exists in `ProceduralPlanetElevationSource`. The cube-sphere `PlanetSurfaceTileSampler` and local `PlanetLocalSurfacePatchSampler` both sample `IPlanetElevationSource`. `PlanetRegionalGeologyAtlas` already blends physical, geographically anchored evolved-height deltas for overlapping neighbors and nested scales.

The missing integration contract was publication and invalidation. `PlanetTerrainRegionProvider` is the shared, application-registered gateway: it starts with the unchanged canonical source and may atomically publish a complete immutable geological atlas. Publication increments a transient revision. Both orbit and local meshes use the same provider; the orbital GPU mesh cache and local ground cache must invalidate when its revision changes. The rendered snapshot's terrain revision must change too, so water/normal overlays refresh. The original canonical source remains directly available for generating new regional research baselines; otherwise the same erosion history could be applied twice.

Research regions are **not automatically applied to gameplay** just because they were generated. The provider remains canonical-only until a higher-level simulation owner explicitly calls `ReplaceLayers`. It is intentionally not wired to a Hero button or save file in A1.

## Milestones

### A1 — Shared planetary geology provider (completed)

- Single source for globe, adaptive cube-sphere LOD and near-ground terrain.
- Atomic opt-in publication of immutable, nested regional delta layers.
- Correct publication revision propagation and geometry cache invalidation.
- Preserve existing worlds when no overlays have been published; no save schema change.
- Tests for overlapping region compositing, seed isolation, coarse/ground identity, and mesh regeneration.

### A2 — Geographically addressed regional evolution (in progress)

- Durable region identity based on canonical planet addresses, scale, and seed rather than transient Hero screen coordinates.
- Bound reusable region storage/cache with explicit loading and eviction; no global 257² grids.
- Replayable regional erosion snapshots tied to canonical generation and model schema versions.
- A spatial index for region queries (not an O(all regions) iteration per planet vertex).
- Deterministic overlap ownership and neighboring edge agreement independent of load order.

### A2.5 — Moving plate and crust model (planned)

- Fixed geographical region IDs are **Eulerian observation windows**, not tectonic plates.
- Persistent crust parcel IDs represent moving material; plate IDs represent changing kinematic groups. A plate may contain both oceanic and continental crust, and parcels may change plate ownership in a rift or collision.
- Epochs identify time snapshots but do **not** calibrate erosion passes to years.
- Begin with deterministic plate motions as sphere rotations about Euler poles; add rifting, subduction, collisions and mass conservation in separately validated phases.
- A region samples its material at the requested epoch; crust can cross region boundaries without being re-identified.

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


## A2.1 — Fixed region/epoch checkpoint contracts

The first A2 slice keeps geographical identity in `PlanetGeologicalRegionId` (canonical generation version, seed, cube face, quadtree level and tile indices) and **separates** it from `PlanetGeologicalEpoch` (absolute years-before-present). Neither identity is a tectonic plate. `PlanetCrustParcelId` represents future moving material and is not derived from the tile address.

`PlanetGeologicalRegionHistory` couples this address/epoch to validated original and evolved physical snapshots, a planet radius and immutable reconstructed overlay semantics. The archive has a distinct version, stores both grids, retains the existing erosion snapshot schema and generator identity, and validates all metadata plus sediment balance on reload. It does not infer geological years from the simulation's integer erosion iteration count.

`PlanetGeologicalRegionCache` is a bounded, explicitly controlled in-memory LRU holding defensive copies of the archived region states. Eviction never mutates canonical geology. `PlanetGeologicalRegionArchive.Serialize/Deserialize` provides portable JSON for an eventual durable repository; **it is not yet an automatically persisted browser database**. Users of A2 can save/load this JSON in their own storage, but cloud/device persistence and migration are later work.

`PlanetTerrainRegionProvider.PublishHistories` selects one compatible epoch and one generated world and composes loaded, previously evolved geographic overlays in stable order. Incompatible generator versions, world identities, duplicate region IDs or mixed epochs are rejected before replacing the current atlas. Publishing remains **opt-in**, with a single transient revision update and A1 cache invalidation. The method does no erosion, tectonic evolution or streaming.

### Remaining A2 acceptance gates

- Durable storage adapter, resumable region loading and bounded eviction across sessions (not just portable JSON).
- Spatial indexing for large numbers of regions; the current atlas still scans each region per sampled vertex.
- Deterministic edge ownership and neighboring sediment exchange are not implied by overlapping height fades.
- Epoch simulation physics and actual moving plate/crust state belong to A2.5 and later; the epoch labels alone do not move continents.

 
## A2.2 — IndexedDB durability and bounded region lookup

- `IPlanetGeologicalRegionDocumentStore` is the asynchronous checkpoint document contract. `PlanetGeologicalRegionRepository` uses versioned, validated `PlanetGeologicalRegionArchive` JSON and the existing bounded defensive LRU cache. Durability completes **before** a write is inserted into memory. Reload checks that world, fixed region address and geological epoch match the requested key. Deletion removes both the durable document and its cache entry.
- The browser registers `IndexedDbGeologicalRegionStore` in WebAssembly DI and persists each archive with an atomic IndexedDB transaction, isolated from gameplay saves. Reloading the same region and epoch can reconstruct identical geological heightfields with a newly created repository. The archive remains explicitly opt-in; opening PlanetForge does **not** publish research geology or auto-load every saved region.
- **Safari Private Browsing may discard IndexedDB when the private session ends**. Browser storage can also be cleared or become quota-limited. Later import/export and explicit user-visible storage controls are required before long-term work is considered reliably backed up.
- The `PlanetRegionalGeologySpatialIndex` creates immutable, seed-aware conservative buckets around each published region's spherical support cap. Each physical sample now checks the small bucket-specific candidate list instead of scanning every archived region worldwide. The cap includes the entire gnomonic support and perimeter fade, and per-layer source ordering is preserved so weighted overlap blending remains numerically unchanged. The index does not invent shared hydrology.
- C# tests cover persistence across repository/cache recreation, no cache pollution on corrupt archives or failed writes, deletion by epoch, and index equivalence to a complete brute-force globe-wide scan including near-boundary and polar positions. A browser test writes two epochs to IndexedDB, reloads the page and verifies independent restoration/deletion.

### What remains before A2 can be called complete

- IndexedDB quota management, backup/export, and an explicit UI workflow for inspecting/restoring stored regions.
- Demand-driven loading/eviction and streaming near the camera, with revision-aware sampling snapshots across asynchronous transitions (A3).
- Exact edge ownership and inter-region physical sediment conservation (A4).
- Orbital image sharpness: current reddish globe shows broad features but lacks clearly resolved mountain provinces at full-disc distance. Treat this as a separate **orbital LOD, normals and physical-relief quality gate**, not a reason to apply artificial shader-only mountains.
