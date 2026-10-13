# Planetary Evolution — visible integration tracker

Baseline: PF 0.0.40.0 / main commit 88efe805bc43b5d81d693556117d6a1e0631e17c.
This is an integration roadmap, **not evidence that stages are implemented or deployed**.

## Rules for closing a stage
- The physical process is driven by one canonical, deterministic planetary state.
- Conservation and boundedness tests pass where applicable; uncertainty and simplifying assumptions are documented.
- Its output is visible on the **existing main 3D globe**, not only in a debug tab, mockup, or disconnected demonstration.
- Hero terrain and LOD 0–3 remain intact; iPhone Safari load time, blur, flicker, and geometry regressions are checked.
- Main branch merged, full CI green, Pages production deployment verified at an exact commit/version.
- Request fixed-seed/fixed-angle screenshots only when a genuinely new visible change is deployed.

## Eight observable stages

| Stage | Observable proof | Integration gate | Current evidence |
| --- | --- | --- | --- |
| 1. Molten surface | Local incandescence follows regional enthalpy, no atlas rectangles; cools under identical camera | Conservative spherical reconstruction + phase-aware material | **Partial:** regional cells drive visible blocks at PF 0.0.40.0 |
| 2. Crust solidification | Stable, evolving dark solid surfaces; reheating/remelting responds to energy | Explicit crust thickness/composition and melt/solid mass transfer, not darkening alone | Not demonstrated |
| 3. Volatile outgassing | Steam/gas plumes and atmospheric inventory evolve with release | Interior volatile budget, fluxes, mass balance | Not demonstrated as coupled epoch |
| 4. Clouds and rainfall | Clouds appear from saturation and precipitation reaches surface | Coupled vapor/condensate, heat and pressure | Not demonstrated as coupled epoch |
| 5. Lakes and oceans | Water accumulates in real terrain depressions and coastlines change | Surface water mass, basin filling, evaporation and runoff | Not demonstrated as coupled epoch |
| 6. Tectonics and mountains | Visible uplift, volcanic chains and rifts evolve over geological time | Deformation and elevation written to canonical geometry | Not demonstrated as coupled epoch |
| 7. Snow, ice and rivers | Orographic snow, ice accumulation, downhill river networks and erosion | Cryosphere budget, drainage connectivity, sediment and terrain feedback | Not demonstrated as coupled epoch |
| 8. Habitability and life | Spatial chemical/habitat niches emerge; competing biogenesis hypotheses remain explicit | Geochemistry, energy and nutrient budgets; no deterministic claim of life's origin | Research |

**These stages are observational gates, not an exclusive chronological sequence.** Tectonics, volcanism and outgassing can start early and overlap. Rivers need terrain, precipitation and a water budget; snow requires appropriate climate and elevation.

## Next delivery slices
1. **M5l — regional phase rendering:** implement continuous, bounded spherical interpolation of canonical thermal cells, with seam/pole and same-angle iPhone regression tests. See regional-phase-visual-quality-gate.md.
2. **M5m — solid surface state:** canonical crust formation, latent heat and remelting, independent from shader-only darkening.
3. **M5n — coupled evolution scenario:** one deterministic seed and epoch timeline driving the main planet through thermal, crust and volatile states; add state snapshots and conservation assertions.
4. **M5o — visible atmosphere and water:** add mass-conserving outgassing, condensation, rainfall and terrain basin filling in small separately verifiable slices.
5. **M5p+ — tectonics/hydrology/cryosphere:** connect actual uplift, drainage and ice, while preserving earlier Hero/LOD work.

## Immediate iPhone acceptance for M5l
Seed 24061984. Capture T+0, T+1h, T+4h at identical angle/zoom, plus reset and uniform reference. Check block boundaries, camera stability, UI overlap, loading, blur and flicker. Do not request this until a verified main-branch production deployment actually changes rendering.

## Explicit non-goals for M5l
No painted lava cracks, simulated crust masquerading as physical crust, new diagnostic tabs, disconnected water/ice visuals, or premature life gameplay.
