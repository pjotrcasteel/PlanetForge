# Planet Surface Foundation

PlanetForge must support one deterministic world from orbital scale down to local terrain where individual vegetation and animals can eventually be rendered and simulated.

## Non-negotiable invariants

1. **One world, many representations.** Zooming never creates a different terrain. Every level samples the same canonical planet surface.
2. **Double-precision canonical coordinates.** Simulation and surface sampling use doubles. Float conversion is restricted to GPU render meshes and small local render frames.
3. **Physical elevation.** Canonical elevation is stored in metres, never as a percentage of planet radius.
4. **Stable render identity.** Every globe render patch is addressed by cube face, quadtree level, X and Y.
5. **Stable simulation identity.** Hydrology and ecology use a fixed-resolution cube-sphere grid independent from render LOD.
6. **Seam-free refinement.** Adjacent tiles, aligned parent/child samples and aligned local-patch samples resolve to the same canonical world.
7. **Deterministic generation.** A seed and canonical direction always produce the same elevation.
8. **Rendering is downstream.** Hydrology, erosion, biomes, vegetation and entities consume surface data, not WebGL meshes.
9. **Replaceable generators.** Terrain generation is behind `IPlanetElevationSource`; geology/erosion can later compose with procedural elevation without changing coordinates.
10. **Camera detail never changes simulation truth.** Zoom may change the visual representation, but cannot change rivers, biomes, terrain elevation or populations.

## Spatial architecture

```text
Canonical planet surface
(double precision + elevation metres)
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

- queued/background-style tile generation boundary without changing deterministic results
- bounded C# mesh-cache policy
- optional visual morphing during parent/child replacement if skirts alone are not visually sufficient

## Iteration 3 — physical scale and local terrain

Status: **validated; first globe-to-local renderer implemented**.

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
- JavaScript syntax validation in CI

Still to improve later:

- local camera pan/travel across the surface rather than only approaching the point under the orbital camera
- multiple neighboring local patches for continuous travel
- finer local mesh LOD below the current proof-of-foundation resolution
- stable placement identities for vegetation, rocks, buildings and animals
- local water/river geometry rather than only elevation-based colour classification

## Iteration 4 — simulation surface grid and hydrology

Status: **hydrology foundation validated**.

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

Still pending:

- watershed/basin IDs
- river-network extraction from accumulated flow
- lake/water-body entities from filled depressions
- discharge driven by precipitation/runoff rather than cell count alone
- erosion and sediment feedback into terrain
- geology and soil layers
- climate sampling hooks
- biome and vegetation-density layers

## Current foundation gate

Gameplay and deeper terraforming remain intentionally paused while the surface foundation is being hardened. Before this phase is considered complete we want:

1. orbital → regional → local representation to remain one deterministic world,
2. continuous local travel without precision loss,
3. rivers and lakes derived from the global hydrology topology,
4. deterministic local-detail placement suitable for vegetation and animals,
5. composable terrain modification/erosion rather than immutable procedural height only,
6. explicit generation/save versioning so worlds remain reproducible,
7. acceptable browser performance under representative globe and local terrain loads.

Only then do we resume climate-feedback/game-loop work on top of this surface model.
