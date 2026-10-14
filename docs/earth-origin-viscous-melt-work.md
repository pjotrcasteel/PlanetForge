# Funded creeping melt flow and mechanical work

Workstream `PF-EARTH-20261013-A`, candidate 2026-10-14. This is a physically generated thin-film reference, not a full-depth magma-ocean model or a visually accepted release.

## Physical closure and sources

The new reference reduces Newtonian momentum to hydrostatic/viscous balance for a shallow, slowly moving, constant-density melt layer. With no slip at the basal surface and negligible traction at its top, velocity is parabolic through depth. Integrating it gives the familiar thickness-cubed gravity flux. Relevant primary sources:

- [Saville et al. (2022), Predicting Safe Regions Within Lava Flows Over Topography](https://doi.org/10.1029/2022JB024167): shallow Newtonian lava, constant density/viscosity, parabolic velocity, gravity-driven redistribution of flow thickness.
- [Hyman et al. (2022), Toward Next-Generation Lava Flow Forecasting](https://doi.org/10.1029/2022JB024998): lubrication scaling and the limits introduced by inertia and thermal/rheological stratification.
- [Dragoni et al. (2005), A model for the shape of lava flow fronts](https://doi.org/10.1029/2004JB003523): small-Reynolds-number momentum reduction and shallow-flow boundary conditions.

These sources do not calibrate this implementation's test viscosity, initial displacement, planetary magma depth or Hadean timeline. The numerical tests use explicitly declared reference inputs. The implementation retains existing spherical shared-face addresses and conservative material transactions.

For shared wet depth H, shared edge length w, center arc distance L, density rho, viscosity mu, gravity g and free-surface head difference dEta, the face mass rate is:

`m_dot = rho² g w H³ dEta / (3 mu L)`.

Its mean velocity is `abs(m_dot)/(rho w H)`. Flow goes from greater to smaller hydrostatic head; no velocity is seeded, sampled from noise or set by animation time. The shared depth is the physical overlap above the higher basal surface and below the lower free surface. This is a two-point graph/finite-volume approximation on the cubed sphere, with frozen beginning-of-step face geometry. Spatial convergence and nonorthogonal-mesh corrections remain separate unverified requirements; passing energy/temporal tests is not a calibrated flux-accuracy claim.

## Depth-resolved mass and heat

The parabolic velocity is integrated over each actual overlapping donor/recipient layer interval. The integrated profile is `F(z)=H z²/2-z³/6`, normalized by `H³/3`. Each resulting transaction transports existing donor mass and its specific enthalpy. Original-donor admission remains in effect; material received during the same trial cannot fund a further withdrawal.

Thermal transport advances first; the flow proposal uses that candidate's actual material state. The bounded integrator includes both head relaxation and per-layer donor limits, and recomputes them after each accepted substep. The fixed face mobility K gives the conservative admission `dt sum(K)/(rho A) <= 0.25`; each original donor layer also permits at most one quarter of its mass per batch. This is explicit first-order operator splitting, not a demonstrated high-order or long-time solver.

## Available gravitational work

Every column keeps the mass m0 associated with its original resting geoid. This datum is immutable through thermal evolution, lateral conduction and material transfer. In the incompressible reference:

`eta_i = (m_i - m0_i)/(rho A_i)`

`Phi_available = sum[g (m_i - m0_i)²/(2 rho A_i)]`.

This is excess potential above the fixed-mass resting configuration. The constant resting-film/gravity offset is not a releasable source and is not purchased from heat. A flat surface therefore has exactly zero available work. Using the mass difference directly also preserves exact numerical rest; subtracting differently summed floating-point volumes was manufacturing tiny currents.

Before initial evolution, `FundViscousMeltMechanics` debits this available potential from the **existing** surface-rock thermal account and credits a finite `PlanetOriginMechanicalEnergyBudget`. The total initial energy, mass and physical clock are preserved. Funding must leave the reference initially molten; unsupported input is rejected. It cannot be repeated after evolution.

For accepted material mass increments dm, the stable quadratic work difference is:

`Q_dissipated = -g sum[eta_i dm_i + dm_i²/(2 rho A_i)]`.

The calculation uses the actually accepted material masses, including their finite-precision representation. An admitted step must not increase available potential. The identical Q is debited from the finite mechanical budget and added to owned layer enthalpy and the canonical environmental account. No impact, stellar or radiogenic source is introduced. Canonical publication checks that the remaining mechanical energy still matches the material's available potential.

The heat placement follows the squared shear-profile integral, proportional to `[(H-z_low)³-(H-z_high)³]/3`, with equal endpoint shares. Total heat is the accepted discrete potential loss; the profile provides normalized placement within that amount. The floating-point allocation remainder closes in the largest physical share, preventing a vanishing sliver from receiving negative heat. Mechanical debits and cumulative work use compensated arithmetic. Kinetic energy is not claimed: inertia is outside this creeping-flow closure.

The thermal and work stages complete one unpublished candidate at the same epoch revision and clock. They do not advance time twice. The primary terrain publisher receives the same owned masses and derives their height changes without amplification, copied continents or a secondary world.

## Admission, exclusions and unresolved science

- The material has one **fixed incompressible density**. Solid and liquid reference densities must be equal, so thermal phase changes do not silently alter gravitational geometry without their own work account. The existing default formation reference has unequal solid/liquid densities and is deliberately incompatible; it has not been converted or enabled silently.
- Only entirely liquid columns communicate in this initial closure. Solid and mixed/mushy columns are excluded and counted explicitly in the trial's `ExcludedColumnCount`. This is **model coverage**, not a calibrated prediction of coherent crust strength, percolation or impermeability. Darcy/mush flow, fracture opening, intrusion, extrusion and deposition require their own physical interface/rheology model.
- Default maximum Re is 0.1; configurable admission cannot exceed 1. Unsupported inertia rejects the trial. Velocity is never clipped to make a high-Re process look like creeping flow.
- Reference screening requires shared depth/center distance <=0.01, surface slope <=0.05 and head contrast/shared depth <=0.25. These are declared conservative reference screens, not universal scientific transition constants.
- No shallow fluid model is a substitute for a convecting full-depth magma ocean, pressure/composition-dependent crystallization or differentiated continental crust. Rotation, buoyant overturn, a turbulent momentum/energy field and crust mechanics remain unresolved here.
- Earth-sized, metre-deep liquid over coarse planetary cells moves too little in seconds to produce orbital lava channels or mountains. The default roughly seven-metre formation world and its dry physical clock are still inadequate for the requested geological demo. No gravity/time/relief amplification or artificial moving streaks have been introduced to conceal that.
- Archives/production UX and visual acceptance for this reference are not claimed. The default app remains unchanged by these draft APIs; replacement of the physical magma reference and appropriate resolution/time scales is the next substantive primary task.

## Validation at this checkpoint

All **71** locally executed column, formation, energy-ledger, coupled-epoch, environmental-energy and epoch regressions passed in process, with zero-warning Release builds. This includes eight new flow/work cases and a primary publication case where fluxes are generated from pressure rather than supplied after initialization. Tests cover initial funding, exact numerical rest, downhill transport, inverse-viscosity response, matched gravitational loss/heat, conserved mass, exact time, replay, temporal refinement, regime/timestep rejection, cancellation, excluded freezing/mush, immutable gravity/density ownership and the existing unmodified thermal/water behavior.

Local VSTest worker sockets are unavailable, so this is an in-process test invocation, not a claimed local VSTest run. Full GitHub build/.NET/WASM/browser CI, release/versioning, deployment, exact-SHA smoke and human visual acceptance remain distinct gates.
