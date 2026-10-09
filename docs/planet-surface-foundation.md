# Planet Surface Foundation

PlanetForge must support one deterministic world from orbital scale down to local terrain where individual vegetation and animals can eventually be rendered and simulated.

## Non-negotiable invariants

1. **One world, many representations.** Zooming never creates a different terrain. Every level samples the same canonical planet surface.
2. **Double-precision canonical coordinates.** Simulation and surface sampling use doubles. Float conversion is restricted to GPU render meshes and small local render frames.
3. **Physical elevation.** Canonical elevation is stored in metres, never as a percentage of planet radius.
4. **Stable render identity.** Every globe render patch is addressed by cube face, quadtree level, X and Y.
5. **Stable simulation identity.** Hydrology and ecology use a fixed-resolution cube-sphere grid independent from render LOD.
6. **Stable world-location identity.** Persistent local locations use renderer-independent octahedral surface addresses, not render tile IDs.
7. **Seam-free refinement.** Adjacent tiles, aligned parent/child samples and aligned local-patch samples resolve to the same canonical world.
8. **Deterministic generation.** A generation version, seed and canonical direction always reproduce the same generated world inputs.
9. **Rendering is downstream.** Hydrology, erosion, biomes, vegetation and entities consume surface data, not WebGL meshes.
10. **Replaceable generators.** Terrain generation is behind `IPlanetElevationSource`; geology/erosion can later compose with procedural elevation without changing coordinates.
11. **Camera detail never changes simulation truth.** Zoom may change the visual representation, but cannot change rivers, biomes, terrain elevation or populations.
12. **Entity identity is not position.** Generated placement can create a stable initial ID, while a moving entity may later change surface address without changing entity identity.

## Spatial architecture

```text
Canonical planet surface
(double precision + elevation metres)
        │
        ├── Stable surface address
        │   octahedral / renderer-independent
        │
        ├── Globe render quadtree
        │      ↓
        │   PlanetSurfaceTileMesh
        │   normalized float GPU coordinates
        │
        ├── Local surface frame
        │      ↓
        │   East / North / Up metres
        │      ↓
        │   PlanetLocalSurfaceMesh
        │   small float GPU coordinates
        │
        └── Fixed simulation grid
               ↓
            hydrology
            erosion
            soil/geology
            climate sampling
            biome/ecology
```

The globe and local representations sample the same `IPlanetElevationSource`. Local terrain is therefore not regenerated as a second world when the camera approaches the surface.

## Iteration 1 — canonical tiled surface

Status: **validated**.

- cube-sphere projection
- six stable cube faces
- quadtree tile IDs
- double-precision surface coordinates
- continuous 3D spherical elevation sampling
- continental, regional, ridge and detail terrain scales
- independent tile meshes
- tile-aware WebGL renderer
- seam/determinism tests
- old monolithic icosphere terrain path removed

## Iteration 2 — adaptive globe LOD

Status: **validated and connected to the browser camera**.

- camera/view model for C# LOD decisions
- recursive quadtree selection
- projected-size refinement
- maximum-detail bounds
- horizon culling
- mixed-LOD radial skirts
- C# terrain mesh cache
- browser GPU tile-buffer cache
- bounded browser tile-buffer eviction
- seed/radius geometry invalidation
- debounced browser camera updates
- sequence-safe asynchronous camera responses
- viewport/FOV-aware selection
- component/renderer cleanup on disposal

Still to improve later:

- queued tile-generation boundary without changing deterministic results
- bounded C# mesh-cache policy
- optional visual morphing during parent/child replacement if skirts alone are not visually sufficient

## Iteration 3 — physical scale, local terrain and travel

Status: **core validated**.

- canonical elevation in metres
- Earth-scale procedural relief magnitudes
- physical planet radius used when creating geometry
- sea level and terrain colour thresholds in metres
- double-precision local tangent frame
- stable East/North/Up basis including polar locations
- world-to-local and local-to-world round trips
- canonical local patch sampler
- local patch cancellation support
- aligned coarse/fine local samples remain identical
- globe-to-local transition based on physical camera altitude
- local patch size derived from altitude, FOV and viewport aspect ratio
- discrete patch-size steps to avoid regenerating geometry for every scroll delta
- local GPU mesh uses metre-scale float coordinates around a double-precision anchor
- local geometry keys allow GPU reuse while camera altitude changes
- separate minimal WebGL pipeline for globe and local rendering
- exponential zoom across orbital-to-local scales
- camera can reach roughly 3 metres above the local surface
- finest current patch is 16 metres across with 32 × 32 cells, roughly 0.5 metre cell spacing
- local camera can orbit obliquely without moving the canonical world anchor
- geodetic `PlanetSurfaceNavigator` moves the anchor across the spherical planet rather than an infinite flat plane
- renderer-independent 30-bit octahedral `PlanetSurfaceAddress` provides stable sub-metre world-location addressing
- travelled local anchors survive camera updates instead of being replaced by render-camera direction

Still to improve later:

- user-facing local travel controls in the browser
- smooth input-driven recentering while travelling continuously
- local river/water geometry
- local object rendering and culling

## Iteration 4 — simulation surface grid and hydrology

Status: **hydrology topology and semantic feature extraction validated**.

- render-LOD-independent simulation-grid cell IDs
- cross-face cardinal neighbor topology
- reversible cube-face transitions
- stable cell-center directions
- elevation sampling per simulation cell
- physical slope calculation
- deterministic Priority-Flood depression filling
- spill-height handling for closed depressions
- globally acyclic drainage tree
- ocean outlets
- deterministic natural sink for dry worlds
- upstream flow accumulation
- cancellation support
- tests proving drainage reaches an outlet without loops
- tests proving flow accumulation is conserved
- watershed extraction from terminal drainage destinations
- lake extraction from connected depression-fill regions
- potential river-network extraction from accumulated upstream land-cell count
- river segments retain their actual downstream drainage target

The current river threshold is deliberately **not called physical discharge**. It uses contributing land-cell count only; precipitation and runoff still need to turn that topology into hydrological flow rates.

Still pending:

- precipitation/runoff-driven discharge
- erosion and sediment feedback into terrain
- geology and soil layers
- climate sampling hooks
- biome and vegetation-density layers
- renderable river/lake geometry derived from the semantic features

## Iteration 5 — reproducible world and placement identity

Status: **core identity contract validated**.

- explicit `PlanetGenerationVersion`, starting at version 1
- `PlanetWorldIdentity` combines generation version and seed
- deterministic generated placement identity based on world, surface address, layer and slot
- changing seed, generation version, location, layer or slot changes the generated placement ID
- placement identity is separate from future movable entity state

Still pending:

- explicit save-file schema/version
- persisted entity state with immutable entity ID and mutable current surface address
- migration policy when generation algorithms change
- deterministic local placement samplers for vegetation, rocks and other generated objects

## Current foundation gate

Gameplay and deeper terraforming remain intentionally paused while the surface foundation is being hardened. The biggest remaining foundation items are now:

1. browser-facing continuous local travel on top of the validated geodetic anchor system,
2. precipitation/runoff plus composable erosion rather than immutable procedural height only,
3. geology/soil/biome layers feeding deterministic local placement,
4. explicit save schema and movable entity-state identity,
5. representative browser performance hardening across globe, local terrain and generated detail.

Only then do we resume climate-feedback/game-loop work on top of this surface model.


## PF 0.0.37.6 — Canonical intermediate-scale orogenic folds

The existing seeded tectonic arcs now generate spatially coherent bedrock anticlines
and synclines with physically expressed wavelengths of approximately 55–95 km
and bounded elevation amplitude (within ±230 m before land masking). Fold trains
run along the tectonic arc rather than following the render grid or applying
isotropic high-frequency terrain noise. Their slowly curved crests remain
continuous across the 128 / 32 / 8 km laboratory scale changes.

This is a deterministic, uncalibrated structural-geology approximation, not
a plate-tectonic mechanical solver. The same canonical double-precision
spherical direction is used by the globe, local patch and laboratory; the
fold height enters **bedrock before erosion**, and hydrology can then modify
it. Existing coastline, ocean and broad tectonic relief remain governed by
the base geological fields. The raster solver is not allowed to add random
detail solely because the view requested extra mesh resolution.

Acceptance checks include bounded fold height, same-seed replay,
geographic continuity across nearby planetary directions, and exact
coarse/fine canonical bedrock identity at shared vertices. Further steps
must assess multiseed ridgeline quality and calibrated drainage morphology
before the research Hero geology is enabled across the live planet.
