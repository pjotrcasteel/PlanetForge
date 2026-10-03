# Planet Surface Foundation

PlanetForge must support one deterministic world from orbital scale down to local terrain where individual vegetation and animals can eventually be rendered and simulated.

## Non-negotiable invariants

1. **One world, many representations.** Zooming never creates a different terrain. Every level samples the same canonical planet surface.
2. **Double-precision canonical coordinates.** Simulation and surface sampling use doubles. Float conversion is restricted to GPU render meshes and future local render frames.
3. **Physical elevation.** Canonical elevation is stored in metres, never as a percentage of planet radius.
4. **Stable tile identity.** Every render patch is addressed by cube face, quadtree level, X and Y.
5. **Stable simulation-grid identity.** Hydrology and ecology use a fixed-resolution cube-sphere grid independent from render LOD.
6. **Seam-free sampling.** Adjacent tiles and aligned parent/child samples resolve to the same world-space direction and elevation.
7. **Deterministic generation.** A seed and canonical direction always produce the same elevation.
8. **Rendering is downstream.** Hydrology, erosion, biomes, vegetation and entities consume surface data, not WebGL meshes.
9. **Replaceable generators.** Terrain generation is behind `IPlanetElevationSource`; geology/erosion can later replace or compose with procedural elevation without changing coordinates.

## Coordinate layers

```text
PlanetVector
(double precision direction/world vector)
        ↓
PlanetSurfacePoint
(direction + elevation metres)
        ↓
PlanetSurfaceTile
(stable render-quadtree tile)
        ↓
PlanetSurfaceTileMesh
(float GPU representation only)

PlanetLocalFrame
(double precision world anchor + East/North/Up)
        ↓
PlanetLocalPosition
(small metre-scale coordinates for local entities)
```

Rendering and simulation intentionally use different spatial partitions:

```text
Canonical planet surface
        ├── Render quadtree → adaptive visual LOD
        └── Simulation grid → rivers / erosion / soil / biome / ecology
```

Changing camera zoom therefore cannot change a river, biome boundary or population simply because a different render tile was selected.

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

## Iteration 2 — adaptive LOD foundation

Status: **core validated; runtime streaming integration still pending**.

Implemented:

- camera/view model for LOD decisions
- recursive quadtree selection
- projected-size refinement
- maximum-detail bounds
- horizon culling
- tile mesh cache
- cache invalidation by seed and physical radius
- radial skirts for mixed-LOD crack protection
- renderer supports separate surface/skirt ranges
- renderer refreshes when selected tile IDs change

Still pending:

- browser camera state feeding the C# LOD selector
- queued/asynchronous tile generation
- bounded cache eviction
- smooth parent/child handoff under rapid camera motion

## Iteration 3 — physical and local coordinates

Status: **core validated; detailed local rendering still pending**.

Implemented:

- metre-scale canonical elevation
- Earth-scale procedural relief limits
- physical radius used when creating render geometry
- sea-level rendering moved to metres
- terrain colour thresholds moved to metres
- double-precision local tangent frame
- stable East/North/Up basis including polar locations
- world-to-local and local-to-world metre-scale round trips

Still pending:

- continuous camera transition into a local tangent-space renderer
- floating local render origin
- deterministic sub-tile detail layers
- stable placement IDs for vegetation, rocks, buildings and animals

## Iteration 4 — simulation surface grid

Status: **in progress**.

Implemented in the current iteration:

- render-LOD-independent grid cell IDs
- cross-face cardinal neighbor topology
- reversible cube-face transitions
- stable cell-center directions
- elevation sampling per simulation cell
- physical slope calculation
- steepest-downhill drainage target

Still pending:

- flow accumulation
- depression/lake handling
- watersheds
- river extraction
- erosion feedback
- geology and soil layers
- climate sampling hooks
- biome/vegetation density layers

## Gate before gameplay resumes

The game loop, expanded climate feedback and terraforming progression remain paused until:

1. camera-driven runtime LOD is stable,
2. local coordinate/render transition is proven,
3. drainage/watershed data survives cube-face boundaries,
4. surface data has deterministic save/version semantics,
5. browser performance remains acceptable under representative terrain loads.
