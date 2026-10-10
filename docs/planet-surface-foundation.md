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


## PF 0.0.37.7 — Physically qualified mountain/catchment selection

The research Hero formerly picked the steepest point in a coarse 256×128
diagnostic raster and then drilled into the most incised individual pixel.
These two choices could select a smooth crater wall followed by an isolated
raster scar while still reporting thousands of metres of regional relief.

The showcase now evaluates separated candidate highland regions using
real canonical 9×9 samples spanning 128 km. It removes the best-fit plane
to measure nonplanar bedrock structure, rather than mistaking a steep but
uniformly sloping surface for a genuine mountain catchment. Both relief
and RMS nonplanar structure are reported in the Hero diagnostics.

The next 32 / 8 km geographical anchor is chosen from physically sized
neighbourhoods with real slope variation, incision and flow. It prioritizes
coherent terrain morphology over the single deepest incised grid cell.
The hydraulic bank-carving stage also uses upstream square kilometres
for activation and headward stream energy, not raw pixel counts. The
algorithm is deterministic and changes *where* the laboratory inspects
the one canonical planet, not elevations according to zoom.

Future work: objectively assess ridge/watershed alignment across multiple
seeds, physically exchange sediment between regional boundaries, and
use an erosion atlas only when it has seamless global geographical identity.


## PF 0.0.37.8 — Mesoscale canonical mountain ridges and connected river reaches

**Observation:** PF 0.0.37.7's iPhone screenshots showed ~3.6 km total
elevation variation but an almost planar ridge, and the denser grid revealed
disconnected grid-aligned tributary cuts. A large global elevation range does
not guarantee meaningful 8–32 km structural relief.

The same immutable orogenic arcs now produce secondary ~14–25 km and
tertiary ~4.7–8.1 km physical-wavelength folds, in addition to the
55–95 km primary fold trains. Local amplitudes are bounded and gradually
fade with the tectonic envelope; these are approximations of compression
fold structures, **not** erosion and not a screen-space noise shader.
Every raster, globe tile and independent renderer samples the same
canonical spherical location and thus exactly the same bedrock height.

The hydraulic bank carver now excavates a continuous, physical corridor
from each incised cell to its known downstream receiver. Previously a
short local ellipse was stamped independently on each raster vertex,
which formed dotted or stair-stepped cuts. New segment-shaped footprints
follow diagonal receiver links too. Eroded volumes continue to be
measured in cubic metres; no water or sediment is invented. The
numerical hydraulics itself is still a research approximation.

A visible version bump, seeded geographic continuity tests, connected
flow-reach regression tests, and the existing 128/32/8 km browser tests
gate this milestone. Realism, global sediment exchange, seamless erosion
atlases and mobile budgets remain prerequisites for enabling expensive
Hero evolution on the gameplay planet.


## PF 0.0.37.9 — Finite branching bedrock spurs, not infinite stripes

iPhone screenshots of the 128 km Hero (both 129² and 257²) showed the
failure of the previous intermediate-scale structure: a series of evenly
spaced parallel ridges, strongly reminiscent of procedural corrugation.
The culprit was a pair of fixed-frequency cosine wave trains across
every tectonic arc. More vertices merely sampled those stripes better.

The secondary and tertiary periodic waves have been **removed**.
The underlying primary 55–95 km tectonic folds remain. The canonical
bedrock now has finite, seed-stable rock spurs branching away from the
existing tectonic axes; each has a bounded geological footprint, unique
position, strike, length and width. A spur fades smoothly at its ends,
and overlapping branches share a capped +223 m relief budget. The
locations are generated in double-precision physical metres relative
to immutable tectonic arcs, not tied to a grid, viewport or zoom level.

This is a first-pass structurally motivated bedrock hierarchy—not a
mechanical rock folding model, a complete drainage network, or a
guarantee of photorealism. Actual watershed routing, hydraulic
erosion and sediment remain separate processes. Canonical world
coordinates are preserved across all hero and planetary LODs.

**Remaining issue:** 128 km erosion still changes its exported sediment
substantially between the 129² and 257² grids. Numerical resolution
invariance requires a dedicated calibrated physical experiment rather
than a visual tolerance adjustment; it must pass before region evolution
can be applied to the main gameplay planet.

## PF 0.0.37.10 — Physical bank width and stage-specific sediment diagnostics

The bank-carving kernel no longer adds a fraction of cell spacing to physical
channel width or forces a minimum width of 1.35 cells. That floor made channels
physically wider on coarser grids. Its 12-cell radius cap also truncated banks
on fine grids; support now follows physical width and includes the receiver reach.
A channel narrower than the grid can remain unresolved instead of excavating
extra neighbouring rock to make it visible. Resolving subgrid channel volume
requires a separate finite-volume treatment; this change does not claim that.

A controlled straight channel with 80 km² contributing area and 80 m incision
previously cut 29.80 m at a point 1 km from its centre on a 1 km grid. The physical
kernel correctly leaves that point outside its support. Regression tests compare
the same physical bank point at 1000/500/250/125 m spacing for an 800 km² channel,
and integrate an 80 km² channel at 125/62.5/31.25 m spacing against its analytic
Gaussian cross-section (1% quadrature tolerance). These tests hold the channel
and contributing area fixed, isolating the bank stage from routing changes.

Hero diagnostics now separate hydraulic export from additional bank export.
For seed 24061984 at the existing selected 128 km anchor (0.42242678262485306,
0.4821837720791227, -0.7674988099435489), six iterations produce:

| Grid | Hydraulic export km³ | Bank export km³ | Total export km³ |
| --- | ---: | ---: | ---: |
| 129² | 795.34 | 28.16 | 823.50 |
| 257² | 548.72 | 9.60 | 558.32 |

**The full resolution-invariance gate is still failing.** Most of the discrepancy
comes from the hydraulic stage, not the corrected bank width. An analytic 128 km
parabolic catchment, z = 1000 + 0.02y + 0.0000005x² in metres, also produces
908.08 versus 784.64 km³ hydraulic export over six iterations. Next work must
address discharge/erosion footprints and sediment transport under refinement,
including quadrature at regional boundaries. Do not tune a visual tolerance or
claim these numerical iterations represent a calibrated geological duration.
Bedrock realism, multi-seed validation and mobile performance remain separate
gates before applying evolved Hero terrain to the gameplay planet.

## PF 0.0.37.11 — Unit-width hydraulic forcing and physical settling distance

The hydraulic solve previously used total contributing area at a raster vertex
to set erosion depth over the entire cell. On a smooth hillslope, halving cell
width divides an equally supplied flow strip into two strips: each receives
half the contributing area, but discharge per metre of contour is unchanged.
Using the strip's total area for an areal erosion depth suppressed erosion on
the refined grid. Transport capacity also scaled with cell area, and a fixed
25% settling fraction was applied once per routing step regardless of length.

The revised research model uses specific contributing area A / cell width,
expressed in kilometres, for hydraulic forcing and activation. Transport
capacity scales with flow width rather than cell area. Excess-load settling
uses 1 - 0.75^(reach length / 1000 m × floodplain factor), weighted across the
actual primary/secondary receiver lengths. For fixed slope and capacity, the
uncapped attenuation composes over subdivisions of the same physical reach.
The 1000 m reference keeps coefficients anchored to the original research grid;
this is an explicit empirical approximation, not calibrated hydraulic time.
The incision/deposition safety caps and sediment mass accounting remain.

The unit-width approach follows the distinction between contributing area and
specific catchment area in [GRASS r.watershed](https://grass.osgeo.org/grass85/manuals/r.watershed.html),
and between area rates and width-integrated transport in
[Harmon et al. (2019), r.sim.terrain](https://gmd.copernicus.org/articles/12/2837/2019/).
PlanetForge is not implementing those models or adopting their calibration.
The square-grid flow width is still approximated by cell spacing, so orientation
and unresolved channel geometry require further work.

Regression benchmarks sample identical 128 km surfaces at 129² and 257²:

| Six iterations, hydraulic export | 129² km³ | 257² km³ | Difference relative to coarse |
| --- | ---: | ---: | ---: |
| Plane z = 1000 + 0.02y | 864.93 | 858.20 | 0.78% |
| Oblique plane, plus 0.013x | 865.18 | 858.85 | 0.73% |
| Parabolic catchment, plus 0.0000005(x - 64000)² | 907.79 | 894.38 | 1.48% |
| Seed 24061984, existing Hero anchor | 794.92 | 742.61 | 6.58% |

Here x and y range from 0 to 128000 metres. The old parabolic benchmark exported
908.08 versus 784.64 km³ (13.59% difference), and the previous Hero hydraulic
stage exported 795.34 versus 548.72 km³ (31.01%). Bank export remains separate:
the revised Hero bank stage exports 28.23 versus 12.19 km³.

Additional 65²/129²/257² tests use erodibility 0.005 to keep every cell below the
12 m numerical incision cap. They require decreasing refinement error and
within 5% bulk erosion agreement at the finest pair. This prevents saturation
from concealing a failed discretization. The normal six-step benchmark requires
within 3% export agreement and explicit sediment conservation. Equal unit-width
runoff is tested separately below the cap.

**Remaining limits:** concentrated boundary flows and deposition are not fully
converged. For example, the uncapped oblique benchmark exports 1.896 versus
2.102 km³ despite bulk erosion differing by only 2.8%. The Hero difference also
still exceeds the analytic benchmark tolerance. Therefore the global evolution
gate is not passed. Physical boundary quadrature, concentrated flow widths,
regional exchange, multi-seed tests and mobile appearance remain future work.
The broad bedrock morphology has not been redesigned by this solver change.

Regional geology snapshot version advances to 2 so old numerical histories
cannot silently resume under different evolution equations. Version 1 research
snapshots must be regenerated from canonical bedrock. Gameplay save schemas are
unchanged because the new solver remains in the terrain research workflow.


## PF 0.0.37.12 — irregular regional bedrock and continuous incision onset

The 128 km Hero screenshots still showed broad smooth bands, while refinement
made erosion steps more visible. Subdivision of the five steepest 1 km edges
at the existing seed 24061984 Hero anchor found continuous bedrock, not height
jumps: the steepest sampled edge changed about 131 m over 1 km. This does not
establish continuity everywhere on the planet, including plate transitions.

Canonical broad tectonic folds now use a nonperiodic field aligned with each
arc instead of an endlessly repeating cosine. A separate spherical bedrock
field supplies irregular structure at 32, 16, 8, 4 and 2 km lattice scales,
with respective amplitude budgets of 320, 160, 80, 40 and 20 metres. A smooth
64 km field warps coordinates by at most 8 km on each axis. Softened ridge
profiles avoid absolute-value cusps. The total contribution is bounded by
620 metres before the land and mountain-envelope masks. This is a procedural
structural prior, not a claim that erosion or calibrated tectonics produced
these features. It is sampled by the canonical elevation source for globe,
regional and local geometry, with no camera-dependent displacement.

Hydraulic incision formerly switched from zero to a finite cut at specific
contributing area 6 km. It now begins continuously at 5 km, reaching full
strength at 12 km with a cubic smoothstep. Activation multiplies the capped
incision so the safety cap cannot erase the transition. Unit-width forcing,
sediment mass accounting and the existing refinement regressions remain.
Deposition activation and bank carving still have thresholds; this change
does not claim to eliminate every numerical terrace or pass the full erosion
convergence gate.

Generation identity advances from 16 to 17 because canonical terrain changes.
Regional research snapshots advance from schema 2 to 3; versions 1 and 2 must
be regenerated. Tests cover metre-scale continuity and nonplanar structure
within 128, 32 and 8 km windows for three seeds, aligned canonical LOD samples,
and continuity across both the old and new hydraulic activation boundaries.
The gameplay save format is unchanged. The Hero realism gate remains open.


## PF 0.0.37.13 — continuous bank and floodplain activation

The 8 km research preview still exposes numerical shelves in eroded rock.
Two discrete erosion-stage eligibility checks remained after continuous
hydraulic incision onset was introduced in 0.0.37.12. Bank widening jumped
into existence at 0.8 km² of physical drainage and 4 m of prior cutting;
floodplain deposition switched on at specific contributing area 8 km.

Bank-width and headward retreat now use smooth activation across 0.8–2.4 km²
and 0–8 m of actual pre-existing incision. Above those transition ranges,
the physical channel-width law and bounded headward limits are unchanged.
Floodplain deposition now ramps from specific contributing area 8–12 km,
**after** the depositional safety cap, without creating or losing sediment.
The changes affect only the experimental physical solver, not the camera,
GPU geometry, gameplay save schema or canonical bedrock generator.

Regression tests probe both sides of each bank threshold and each deposition
transition, alongside sediment conservation. The research snapshot schema
advances to 4; existing schema 1–3 snapshots must be regenerated. The generator
identity stays 17 because canonical bedrock is unchanged. The full erosion
convergence and visual realism gates remain open: capped high-energy flows,
receiver switching and boundary exchange may still create visible terraces.


## PF 0.0.37.14 — continuous plate contacts and compact mobile Hero controls

The iPhone 128 km Hero captures for seed 24061984 exposed a long, unnaturally
straight dark scar. A verifiable geological discontinuity existed in the
canonical plate kernel: at a Voronoi contact the two nearest plates swap
primary/secondary roles, which previously changed the ordered ridge-noise
seed and instantly switched mixed continental/coastal uplift factors. The
same contact could therefore have different physical bedrock heights on its
two sides, independent of erosion or shader effects.

Tectonic relative motion and texture now use an unordered canonical plate
pair. Continental and oceanic-side relief smoothly transition across signed
nearest-plate separation in the existing warped tectonic field. Three seeded
all-pairs regression suites cover all 18 plate identities, all four crust
classifications, exact contact symmetry and near-contact continuity.

The phone Hero toolbar uses two-column layouts for region/compare fields and
actions, zoom controls stay paired, and detailed sediment metrics remain
available behind a collapsed diagnostics control below a short live summary.
No vertical exaggeration or shader height trick was added. Generation identity
advances to 18 and regional research snapshots to schema 5 because canonical
heights changed; the gameplay save schema remains unchanged.

Real-world tectonic calibration, hydraulic grid convergence, visual geology
quality, and neighbouring-region sediment exchange remain open research gates.

 
## PF 0.0.37.15 — inherited erosion continuity across regional refinement

The iPhone seed-24061984 captures show broad, nearly planar rock shelves
bounded by unnatural sharp channel walls at 32 and especially 8 km. Increasing
the research grid from 129² to 257² vertices samples virtually the same
shelves because the problem is upstream of WebGL: the 8 km initial bedrock
inherits physical parent cuts through a bilinear delta atlas at 1 km and
125 m parent spacing. Bilinear parent interpolation is height-continuous but
has abrupt slope breaks across every parent cell. Adding more 8 km vertices
only samples those facets more densely.

Parent erosion overlays now use separable, slope-continuous, shape-preserving
cubic Hermite interpolation. This reconstruction preserves every exact parent
height at registered vertices, creates no new extrema between parent samples,
is independent of GPU mesh density, and retains the original smooth perimeter
support blending. It does not add arbitrary noise or exaggerate relief.

Within the experimental Hero solve, the conservative lateral-relaxation step
now transfers existing erosion cuts across numerical cut/no-cut boundaries.
Previously these edges were entirely protected, leaving one-cell vertical
walls before valley widening. The existing riverbank solver then reasserts
actual hydrologically routed valley geometry. The relaxation is conservative
in signed physical metres, so these transfers add no new sediment export.
The optional protected-edge mode remains available in the relaxation API.

The canonical world generator and gameplay save formats are unchanged
(generation identity 18). The research snapshot schema remains 5 because
the serialized physical state and hydraulic equations have not changed;
this milestone changes the interpretation of already-evolved *overlay*
deltas in nested previews and their final Hero bank reconstruction. Expect
the 128/32/8 km research previews to differ with the same seeds.

Regression coverage requires nodal identity, slope continuity at inherited
grid boundaries, no spurious ridges or trench overshoots, existing local
mass accounting, and full mobile WebGL captures. These are numerical terrain
quality improvements, not yet a validated geological erosion timescale.
The 8 km realism and resolution-convergence gates remain open.

 
## PF 0.0.37.16 — nested inspection targets connected catchments

The first PF 0.0.37.15 mobile captures confirm fewer inherited polygonal
facets at 32 km, but the 8 km region still lands on a large smooth shelf with
one circular bowl and narrow steep trenches. The physical heightfield was not
invented by the renderer. Selection from 32 → 8 km was the next root cause:
the parent focus rewarded high RMS nonplanar rock relief far more heavily than
the presence of an actual connected fluvial network, so a sharp crater rim
or single scar could win despite poorly resolved surrounding drainage.

The focus selector now builds an O(n²) summed-area field of physical runoff
and existing incision. For each candidate, its quarter-scale 8/32 km
neighborhood must contain a minimum count of actually routed channel cells
and reach at least two cardinal directions (including channels on the centre axes). A branch-spread score then
distinguishes branching catchments from one isolated cliff, while a
multi-kilometre best-fit plane residual retains geological ridge diversity.
The support threshold uses drainage area in km², not raw runoff-cell counts,
and the channel-support score is normalized by the chosen physical window.

Synthetic regressions place an unusually rugged dry crater and a connected
two-tributary channel system in the same regional world. Only the connected
catchment should be selected for local detail. An entirely dry region retains
a stable central fallback. Generation identity remains 18; canonical
elevation, hydraulic sediment volumes, gameplay save schema, and research
snapshot schema 5 are unchanged. Only the nested research-camera geography
changes. Browser regressions continue validating 128/32/8 km physical solves.

This does not establish that the underlying 8 km erosion physics is now
realistic. Remaining gates: high-energy incision caps, raster routing
convergence, sediment transfer across regional boundaries, and convincing
multiseed mountain/river morphology.

### Visual audit gate

The first 0.0.37.16 candidate passed 275 C# and 13 browser checks, but
inspecting its 8 km *close-camera* CI screenshots still showed broad smooth
rock and isolated cuts. It was not merged on test success alone. The local
focus now also requires routed channel support within the central third of
the quarter-scale window, and the evaluation weights this camera-visible
continuity separately from channels near the outer edge. The browser suite
captures both full-region 32/8 km morphology and the close valley camera so
we can audit the same framing used in the iPhone screenshots. Visual quality
remains a manual release criterion until objective channel-morphology metrics
are calibrated against independent examples.

### Bedrock-first site ranking

A second full-region visual audit still showed an 8 km tile dominated by
almost planar rock and a few very steep artificial channels, despite passing
the spatial channel-support gates. This exposed a more direct error: the
nonplanar-terrain score sampled the *eroded* field, where artificial numerical
cliffs appear more interesting than the actual pre-erosion mountain range.
The site-ranking plane fit and rock relief now use `OriginalElevationMeters`,
which already includes legitimately inherited parent strata. `CumulativeCutMeters`
and physically accumulated runoff remain distinct qualifications, not proof
of original rocky terrain. An adversarial regression with a huge numerical
scar on flat rock and a separate connected ridge catchment protects the
ranking against this confusion. Full-frame browser captures remain required
before deployment; test success alone is not geological acceptance.

### Centreline continuity check

The new bedrock-first adversarial test revealed a qualification bug: a straight
channel passing directly through the selected centreline is not necessarily
present in two diagonal quadrants. The focus selector now counts four
cardinal reaches excluding only the centrepoint. Two-direction continuity
suffices for a through-channel, with three/four directions providing a small
branching bonus. This preserves physically plausible straight tributaries
without allowing runoff quantity to outweigh pre-existing rock structure.

### Consistent geological residuals

A focused test caught a subtle bug in the bedrock-first revision: the first
5×5 sampling loop fitted its plane to inherited `OriginalElevationMeters`, but
the second loop still computed residuals from `EvolvedElevationMeters`. The
mismatch makes simulated erosion itself appear as enormous rock ruggedness.
Both passes now use the identical original geological field, while hydraulic
cut/runoff remain separate qualification inputs.

 
### PF 0.0.37.16 — canonical 0.69–5.5 km mountain relief

The visual gate on bedrock-first site selection remained failed: full-frame
8 km tiles still showed mostly smooth hill flanks and isolated deep hydraulic
trenches. The geological generator produced bounded finite ranges and
32–2 km rock structure, but did not have a distinctive physical mountain
network at the 1 km scale. Refining 129² to 257² exposed that absence;
extra vertices cannot create source morphology.

The canonical spherical elevation source now contains another bounded,
warped crest-and-pass field at 5.5, 2.75, 1.375 and 0.6875 km. It uses
nonperiodic continuous three-dimensional world-metre coordinates and the
existing tectonic mountain envelope. Its ridges are physical source rock,
not water, shader displacement, grid-cell noise or an LOD-specific layer.
The field is deliberately amplitude-limited to approximately -221 to
+281 metres **before** tectonic land masking, so it cannot erase global
land/water separation or create enormous local spikes. Its four octave
wavelengths describe potential subregional uplift and lithological rock
structure, not calibrated erosion or river flow.

Changing canonical heights advances generation identity 18 -> 19 and
research snapshot schema 5 -> 6. Domain/gameplay save schema itself
remains unchanged. Regression checks cover 8 km local crest structure,
one-metre continuity, seed determinism, exact shared source-rock vertices
between 17² and 33² meshes, plus full 128/32/8 km browser visual tests.
Do not promote this research branch merely for passing build and tests:
the physical 8 km before/after images must have recognizable intersecting
ridges, realistic scale and no isolated industrial-looking trenches. Rivers,
lakes, oceans and climate remain separate physical models, to integrate
after credible exposed geology.
