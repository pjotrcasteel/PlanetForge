# Planet Surface Foundation

PlanetForge must support the same deterministic world from orbital scale down to local terrain where individual vegetation and animals can eventually be rendered and simulated.

## Non-negotiable invariants

1. **One world, many representations.** Zooming never creates a different terrain. Every level samples the same canonical planet surface.
2. **Double-precision canonical coordinates.** Simulation and surface sampling use double precision. Float conversion is restricted to GPU render meshes.
3. **Stable tile identity.** Every patch is addressed by cube face, quadtree level, X and Y.
4. **Seam-free sampling.** Adjacent tiles and aligned parent/child samples resolve to the same world-space direction and elevation.
5. **Deterministic generation.** A seed and canonical direction always produce the same elevation.
6. **Rendering is downstream.** Hydrology, erosion, biomes, vegetation and entities consume surface data, not WebGL meshes.
7. **Replaceable generators.** Terrain generation is behind `IPlanetElevationSource`; later geology/erosion can replace or compose with procedural elevation without changing tile addressing.

## Coordinate layers

```text
PlanetVector (double precision unit direction)
    ↓
PlanetSurfacePoint (+ normalized elevation)
    ↓
PlanetSurfaceTile (stable quadtree tile)
    ↓
PlanetSurfaceTileMesh (float GPU representation)
```

Local gameplay will later add a tangent-space frame anchored to a double-precision surface location. Trees, animals, rivers and buildings will use that local frame instead of storing huge planet-scale float coordinates.

## Planned surface iterations

### Iteration 1 — canonical tiled surface

- cube-sphere projection
- six stable cube faces
- quadtree tile IDs
- double-precision surface coordinates
- continuous 3D spherical elevation sampling
- continental, regional, ridge and detail terrain scales
- independent tile meshes
- renderer accepts tile collections
- seam/determinism tests

### Iteration 2 — adaptive LOD

- camera-driven quadtree selection
- parent/child tile replacement
- screen-space geometric error
- tile caching
- asynchronous/queued generation boundary
- crack prevention between unequal neighbor levels
- horizon/back-face culling

### Iteration 3 — local terrain frame

- planet-to-region continuous zoom
- tangent-space local coordinates
- floating local origin
- metre-scale local mesh detail
- deterministic detail layers independent from global mesh resolution
- stable placement coordinates for vegetation, rocks and entities

### Iteration 4 — surface data layers

- elevation and slope
- drainage direction and flow accumulation
- water bodies and river paths
- geology/soil hooks
- climate sampling hooks
- biome/vegetation density hooks

The game loop, climate feedback expansion and terraforming progression resume only after these surface foundations are proven stable enough to build on.
