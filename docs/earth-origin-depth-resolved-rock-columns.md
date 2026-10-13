# Conserved depth-resolved rock columns

Workstream PF-EARTH-20261013-A. Initial foundation, 2026-10-13.

## Purpose and ownership

The prior thin active shell resolved one enthalpy per geographic cell. It could not distinguish a solidifying radiating top from molten material below. This model partitions that already funded shell into depth control volumes. `PlanetOriginCanonicalRockCells` owns the epoch, exact aggregate phase field and optional `PlanetOriginRockColumnField` together. Their aggregate mass and enthalpy remain the canonical `SurfaceRock` account. No second planetary state, clock, energy reservoir or invented funding is introduced.

Columns have stable Eulerian geographic addresses. Layer index zero is the radiating surface; the final layer touches the interior boundary. An address is not a moving material parcel or plate identity. Initial equal-layer partition preserves each cell's mass and energy with a residual final layer.

## Physical contracts

- Each layer resolves temperature and melt from the established sensible/fusion enthalpy material. Latent heat is retained through freezing and remelting.
- Internal Fourier transfer is `Q = k A dt (T_i - T_j) / distance`, with centre-to-centre distance from the current physical layer thicknesses. The exact same Q is debited from one layer and credited to the other.
- The bottom boundary uses the declared boundary thickness plus half the bottom layer thickness. Positive and negative transfers use the existing matched mantle/surface accounts and separate gross counters.
- Absorbed stellar heat and `epsilon sigma T_surface^4` radiation act only on the top layer. The ledger receives exactly the integrated trial totals, never a separately recomputed mean-temperature radiation estimate.
- For melt mass fraction f, volume is `m ((1-f)/rho_s + f/rho_l)`. Column thickness is total volume divided by geographic area. Initial thickness is retained as a datum for later contraction-derived elevation.
- Thermal solid surface thickness sums contiguous fully solid top layers and stops at the first partly molten layer. It says nothing about mechanical strength, chemical segregation, buoyant crust or plate motion.

## Numerical admission and publication

For every layer, integrated conductive incidence plus the top-layer radiative Jacobian (`4 epsilon sigma A T^3 dt`) must be no greater than half the minimum sensible heat capacity `m cp`. The common finite mantle is checked against the sum of its boundary conductances. This conservative explicit admission bound is not an accuracy proof. Exact-time refinement and energy/replay tests are required.

Nonfinite exchange, negative layer enthalpy, invalid parameters and cancellation reject the immutable trial. No clamping, hidden source or partial publication is allowed. Planar column geometry is restricted to thickness/radius <= 0.001, initially and after evolution. Aggregate lateral exchange is deliberately rejected for columns: it would erase layer ownership. Future lateral transport must resolve consistent per-layer fluxes/advection.

The domain foundation is optional and does not yet change a displayed scenario. Application integration must project the actual top-layer phase and use the shared terrain source for orbit and local LOD 0–3. Density contraction cannot justify invented mountains. Physics and terrain revisions must be committed or rolled back together.

## Scientific scope and provenance

This is a **hypothetical dry/airless thin conductive shell**, not a resolved full-depth convecting magma ocean or a calibrated reconstruction of Earth. Constant reference densities/conductivity and the existing constant-pressure solidus/liquidus are explicit approximations. Test densities 3000 kg/m³ (solid), 2800 kg/m³ (liquid), and k=3 W/(m K) exercise mechanisms; they are not claimed as calibrated Hadean parameters.

Lebrun et al. (2013), [Thermal evolution of an early magma ocean in interaction with the atmosphere](https://doi.org/10.1002/jgre.20068), distinguishes surface boundary cooling from convecting mantle evolution and atmospheric feedback. Its phase-density mixture relation motivates preserving solid/liquid specific volumes; this implementation does not reproduce that paper's convection, pressure-dependent phase front or atmosphere. Elkins-Tanton (2012), [Magma Oceans in the Inner Solar System](https://doi.org/10.1146/annurev-earth-042711-105503), frames crystallization/differentiation as distinct processes. A thermally solid lid here is not evidence that either full differentiation or plate tectonics has been simulated.

Still required: composition and pressure dependence, crystallization/segregation and material transport, mechanical coherence/rheology, physically driven deformation/tectonics, volatile exchange and atmospheric radiation, water, climate, chemistry and life. The user's long-term lush-planet objective requires those interacting causes, not stage timers or shader-generated terrain.

## Validation

Seven domain regressions cover funded partition, surface-only radiation, internal diffusion, a solid top above molten layers, volume contraction, funded remelting, numerical/cancellation rejection, and exact-time convergence/replay. Existing application and infrastructure suites also pass locally (701 total tests). Full PR CI and deployment are separate pending gates; consult CURRENT.md and GitHub for their current result.

## Shared physical terrain publication (follow-up increment)

`PlanetOriginRockColumnElevationSource` projects current thickness minus each column's initial thickness, in metres with no height amplification. Spherical inverse-distance reconstruction uses all cell centres, is seed-independent, and does not add modern continents or procedural relief. This read-only reconstruction does not transport mass or energy. The display atlas now resolves the top layer rather than the aggregate mean.

The shared `PlanetTerrainRegionProvider` implements `IPlanetOriginSurfacePublication`. It serializes terrain replacement and a synchronous world/snapshot callback under the same re-entrant publication gate; snapshot reads use that gate too. On callback failure, the exact old terrain source/revision and the experience's accepted epoch, columns, material atlas, climate and local caches are restored. Allocated revision numbers are never reused, including failed attempts, so geometry cached during a failed trial cannot masquerade as a later successful revision.

Entering column mode retains the previously published legacy/geology elevation; leaving restores that exact source with a fresh revision. Independent geology replacement is rejected while columns own the surface. Seed/reset/archive restoration clears both origin physics and terrain ownership. Existing non-column references retain their established terrain path. Column geometry must match the selected world's radius.

This gateway does not claim a new visual scenario is available yet. Its regression tests cover top-vs-mean phase projection, centre/datum/contraction and cube-seam sampling, orbital/local agreement, failure rollback, non-reused failed revisions, restored legacy elevation, monotonic epoch rejection and a blocked concurrent reader.

## Bounded reference integration and material display

`PlanetOriginRockColumnIntegrator` re-evaluates the layer conductance/radiative-Jacobian bound after each immutable substep, uses 80% of that admission bound and the caller's separate accuracy cap, floors to microsecond ticks, and integrates the exact final remainder. Budget exhaustion, cancellation, unsupported lateral mixing and sub-microsecond requirements fail without publishing any intermediate world. This initial integrator accepts positive sub-year requests and constant declared external boundary conditions; it is not a geological long-time/adaptive-error integrator.

`PlanetRockColumnReference` selects eight depth layers, the explicit 3000/2800 kg/m³ density hypothesis, conductivity 3 W/(m K) within the column and across a 25 m declared mantle boundary, solar forcing from the selected world, albedo 0.15 and emissivity 0.9. A 30 s accuracy cap is independent of the changing stability bound. The clock is an elapsed thin-shell cooling experiment, not a calibrated Earth formation duration. Four hours produce thermally solid surface material above a still-molten bottom in regression tests. This does not imply deep magma oceans solidify top-down: the model does not include the pressure-dependent convecting mantle's crystallization front.

Opt-in main globe URL: `?visualTest=1&rockColumns=1`. Existing molten, partial-melt, uniform and Snowball references remain selectable and unchanged. The compact HUD shows actual radiating surface phase, contiguous solid-top thickness and bottom melt. All geometry comes from the canonical column source; no synthetic cracks, mountains, plates, vegetation or ancient continents are introduced.

The column material uses a read-only Planck spectral radiance integral over 380–780 nm (5 nm trapezoidal quadrature) with analytic CIE 1931 observer fits from Wyman, Sloan & Shirley (2013), [Simple Analytic Approximations to the CIE XYZ Color Matching Functions](https://jcgt.org/published/0002/02/01/). Planck law follows the [CIE definition](https://cie.co.at/eilvterm/17-24-005); fixed gray emissivity 0.9 and one display exposure anchored at 2200 K are declared optical hypotheses. RGB gamut clipping, exponential display compression, sRGB conversion and a 1024-entry RGBA8 lookup are display approximations, not calorimetric simulation. Both hot solid and liquid emit at their projected temperature; melt only changes the neutral reflected-material roughness/appearance. Reflected-light coefficients are an uncalibrated visual reference, not measured primordial composition or a coupled spectral albedo solver. No per-frame radiance normalization, inherited geological colour map or noise-driven magma motion is used in this column material. The reference sun direction is also used for its reflected display, transformed to the local frame for ground views.

The existing 0.1 K spatial texture envelope is 0–6553.5 K. Application projection rejects an out-of-envelope candidate before final publication, allowing the terrain/world transaction to roll back; it never silently clips physical heat. Direct WebGL compilation/linking validates both shader programs locally. Full Blazor/browser, production exact-SHA smoke and user Safari acceptance remain distinct required gates.
