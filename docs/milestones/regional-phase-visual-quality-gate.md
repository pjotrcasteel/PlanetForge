# Regional molten-rock visual quality gate (after PF 0.0.40.0)

Status: design/acceptance contract only. **No rendering change is implemented by this document.**

## Evidence and defect
iPhone Safari screenshots for seed 24061984 at T+0, T+1h, T+4h show conspicuous bright rectangular patches on an orange planet. The uniform reference is more homogeneous. The diagnostic overlay overlaps text and obscures the upper globe. The illustration previously shown in discussion is **not** a PlanetForge output.

## Next implementation slice: conservative field reconstruction
- Keep the canonical 384 spherical thermal cells and their enthalpy, sensible/latent heat, area and mass as the **only** physical authority.
- Reconstruct a continuous, display-only spherical temperature/melt field. Account for spherical distance and cubed-sphere seams; no interpolation across an atlas seam as if it were a physical discontinuity.
- Reconstruction must never write back into canonical physics, change energy accounting or synthesize heat/mass. Clamp display values to physically supported local bounds.
- Sample the same reconstructed field in orbital and local LOD paths. Preserve Hero geometry and all existing LOD transition behavior.
- Keep temperature-derived incandescence distinct from actual exposed liquid melt. No painted lava veins presented as physical fractures or newly formed crust.
- Verify atlas rotation/camera invariance and eliminate block edges under a fixed camera. Prefer GPU-friendly filtering or a bounded CPU precomputation; measure iPhone memory and latency.

## Mobile UX
- Place diagnostics outside the main planet viewport or collapse them by default.
- Prevent text collision and clipping at narrow Safari widths; preserve accessible Advance, Reset and comparison controls.

## Acceptance checks
1. Deterministic physics state and conserved energy unchanged for the same seed and elapsed time.
2. Seam and pole continuity, including sampling at all cube-face borders.
3. Fixed seed 24061984, fixed camera/angle and zoom at T+0, T+1h, T+4h: no rectangular thermal blocks; cooling differences are visible and attributable to physical state.
4. Reset exactly restores T+0; uniform reference is visibly and numerically distinguishable.
5. Orbital and local LOD agree on sampled temperature/melt at shared positions; no new blurry transitions, flicker, missing planet or off-globe artifacts.
6. iPhone Safari: diagnostic text does not overlap; measure load and frame timings.
7. Full .NET, WebGL/browser, and deployment checks pass before requesting another visual inspection.

## Later, separate scientific milestones
Crust formation and fracture geometry require their own physical models; do not simulate these by cosmetic cracks. Cryosphere issue #148, tectonics, physical rivers/lakes remain independent gated work. Do not claim they are implemented by this slice.
