# M5k — regional enthalpy-derived visual inspection

The first opt-in thermal reference successfully cooled a single rock-surface heat store, but its orange-to-brown appearance was only a **uniform shader tint**. The iPhone screenshots correctly revealed that this was not regional solidification.

M5k replaces that limitation *in an optional, clearly labeled reference mode*, not in the legacy Snowball mode or in a claimed reconstruction of Earth's Hadean history.

## Physically conserved world

- The canonical `PlanetOriginEnergyLedger` owns one active `SurfaceRock` reservoir. For this controlled reference only, `SurfaceRockPhaseMaterial` explicitly changes its interpretation to **sensible plus fusion enthalpy**. The zero-reference rock enthalpy is fully funded from initial creation; the older sensible-only 1600 K world is not reinterpreted without funding.
- `PlanetOriginSurfaceRockPhaseField` owns the exact **same** surface mass and joules on 384 area-weighted cubed-sphere cells, not a second amount of matter or energy.
- An explicitly declared, deterministic *hypothetical initial thermal contrast* redistributes **1.2%** of the initial surface energy between cells while preserving the global total. Its footprint is not represented as an actual impact crater, evidence of a Hadean hotspot, a mantle plume, or simulated eruption. It is an uncertain initial boundary condition whose resulting local heat and melt can be studied.
- The active planet carries `PlanetOriginCanonicalRockCells`—one coupled global epoch plus the matching regional material state. Each accepted 300-second step conservatively applies physically computed **cell infrared loss and geometry-dependent absorbed sunlight**; there is **no uncalibrated interior heat export** (`k=0` in this bounded first test). The canonical ledger debits/emits exactly the sum of those regional joules. The source is never mutated until the complete candidate passes energy/mass checks.
- The physical field's **temperature and melt fraction are inverted from conserved cell enthalpy** using the same `PlanetRockEnthalpyModel` as the bulk interior. Cooling releases latent heat during the modeled mushy range, and remelting requires energy. Solid rock is **not** labeled primary crust.
- The planet remains airless and dry. A steam-bearing world is rejected by the reference rate law. Mantle/steam convective evolution, composition-specific phase diagrams, hydrothermal processes and differentiation remain scientific milestones, not current features.

## Existing main 3D planet, not a parallel render

The main `PlanetExperience` publishes the combined regional and canonical epoch atomically. `PlanetRenderSnapshot.SpatialRock` is an **inert, read-only texture projection** whose revision matches `Origin.Revision`. It stores sampled local Kelvin and melt fraction on a 256×128 equirectangular atlas, derived from the existing cubed-sphere physical cells. Geographic cells can have a sharper phase boundary because the physics mesh is intentionally lower resolution than render geometry; there is no extra procedural noise or independent lava simulation.

To accommodate iPhone Safari's WebGL2 implementation, this is uploaded as standard **RGBA8**: two channels contain temperature at 0.1 K display resolution, one channel contains melt fraction at 1/255 resolution. All numerical physics remains double precision in .NET and is never fed back from GPU samples. The existing globe's barren-rock material uses **the projected local melt fraction and local temperature**, rather than one global orange overlay. Physically modeled solidified surface cells lose molten incandescence. GPU pixel colours are a presentation approximation, not a spectral validation or an independent source of thermal energy.

Current base terrain is the existing research elevation field; **rock crystallization alone does not produce topographic crust, plate boundaries or continents**, and no such change is advertised. At local LOD, the same geographic thermal atlas is sampled at the patch anchor. The local patch is far smaller than a physics-grid cell, so this is the physically consistent **constant-cell approximation** rather than a second generated thermal mask. Exact per-vertex resolution across rare physics-cell boundaries will be improved alongside physical-grid refinement. Detailed regional geology and lateral energy transport remain separate scientific work.

## Test on iPhone after full CI and deployment

Open `https://pjotrcasteel.github.io/PlanetForge/?visualTest=1&regionalRock=1`, or enter **HOT ROCK TEST → REGIONAL PHASE TEST** from the usual planet page.

Check the physical localized bright/less bright rock regions, change after **ADVANCE 1 HOUR**, phase range and energy-safe accepted revision, **RESET**, and **UNIFORM REFERENCE** back-navigation. The HUD explicitly says **DECLARED THERMAL CONTRAST · NOT CRUST**. Build version **0.0.40.0**.

CI gates the latest exact-head .NET tests, vanilla WebGL2 scene, temperature+melt projection, existing planet LOD regression and opt-in browser screenshots. The deployed-site smoke must load and actually advance this regional mode, not merely verify that Snowball loads.

## Remaining scientific journey

1. Implement finite-volume cell-to-cell conduction with symmetrical neighbor fluxes and actual spherical edge lengths, without numerical heat creation.
2. Derive spatial mantle convection/volcanic heat exchange, material source and crystallization differentiation from calibrated phase diagrams and densities.
3. Extend the same immutable epoch with real crust mass, thickness, remelting/recycling and terrain elevation response.
4. Couple calibrated H2O–CO2 steam radiative-convective physics and volatile exsolution (#180); only then investigate physically admitted condensation and real water accumulation.
5. Retain uncertainty distributions for unresolved Hadean conditions. Abiogenesis remains hypothesis-based, long after reliable geology, hydrology and geochemistry.

This release is a **conservative phase-field visual proof**, not a self-contained early-Earth simulation. It does not change the core Earth-first roadmap.
