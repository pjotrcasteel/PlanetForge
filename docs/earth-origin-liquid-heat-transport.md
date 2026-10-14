# Connected-liquid heat transport — PF-EARTH-20261013-A

## Physical hypothesis

The original formation shell conducts internally even when liquid. Heating its bottom with a mantle-boundary Nusselt closure does not correct that missing transport path. This candidate adds an opt-in effective-conductivity closure to owned depth layers. It supplies no resolved velocity, lateral lava current, crust strength, fracture, mountain, volatile, or atmospheric inventory.

The source is [Monteux, Andrault & Samuel (2016), DOI 10.1016/j.epsl.2016.05.010](https://doi.org/10.1016/j.epsl.2016.05.010), equations 2–5 and 18; [author-hosted paper](https://www.ipgp.fr/~samuel/henriIPGP/Publications_files/Monteux_etal_2016.pdf). That paper parameterizes convective heat transport using an effective conductivity, superadiabatic temperature departure and Rayleigh/Prandtl scaling. It distinguishes soft and hard turbulent regimes. Our shallow, constant-material, fully-liquid-only adaptation is a separate hypothesis, not a reproduction of its pressure-dependent whole-mantle model.

## Implemented closure and conservation

A contiguous run of fully molten cells defines a physical liquid depth L. Mixed or solid layers interrupt that run. Its top cell temperature defines the reference adiabat, T_ad(z)=T_top exp(alpha g (z-z_top)/cp). At an interface, enhancement requires both an unstable local gradient and positive departure of the lower cell from that reference profile.

Ra=alpha g cp rho² DeltaT L³/(k eta), Pr=cp eta/k. Enhancement is disabled below Ra=1708. Otherwise the effective convective multiplier is 0.089 Ra^(1/3) below 1e19, or 0.22 Ra^(2/7) Pr^(-1/7) above it, with the declared aspect ratio one. Only the superadiabatic fraction of the local gradient is enhanced; molecular conductivity remains. The published scaling's regime switch is not smoothed or claimed globally monotonic in viscosity across that switch.

Reference eta=10 Pa s, alpha=3e-5/K, g=9.81 m/s². Density, conductivity and heat capacity come from the actual column material. Coefficients and physical widths are frozen at the beginning of each accepted thermal substep. Every internal interface has one shared heat flux, debited from one owned layer and credited to the other. The emitting face and finite-interior boundary retain their molecular conductive resistance. Boundary transport does not bypass a solid/mixed layer or add a heat source. Phase cutoff is lagged within a substep and must be tested against timestep refinement.

The funded upper-shell factory retains the existing seeded heat hypothesis, reallocating surface mass and heat from the original interior inventory. Quartic depth grading supports the surface boundary. The primary formation default remains unchanged.

## Numerical corrections and admission

The implicit solver now represents temperature departures from a shared column reference. This avoids cancellation when subtracting nearly equal absolute temperatures at a stiff liquid interface; accepted heat still updates the same enthalpy ledger.

Optional step doubling compares one full trial with two half trials. The error norm is the maximum owned specific-enthalpy discrepancy divided by the material's minimum cp, in Kelvin. Only the refined pair is accepted. Rejected trials do not advance the published clock, revision, mass or ledgers. Newton convergence failures are typed and may trigger bounded timestep halving; invalid physical parameters and accounting failures are not swallowed. Cancellation and a finite trial budget remain gates. A final one-microsecond remainder is avoided where possible; error estimation itself requires a splittable interval of at least two microseconds.

This is a local error estimate, not a bound on cumulative trajectory error. It does not estimate mass/work errors for mechanical flows, and rejects combining that thermal estimator with the mechanical-flow reference. The two transport models are not silently composed into a turbulent velocity model.

## Evidence and outstanding gates

Local builds pass with zero warnings/errors. The in-process runner passes 14 targeted application cases and 72 broader domain/infrastructure cases, including the unchanged primary formation path and the new coupled-reference publication (86 total). Standard local VSTest sockets are unavailable; full GitHub CI is a separate gate.

- Uniform one-kilometre references at 32/64/128 layers and 0.02/0.01/0.005-second caps complete the one-second test. At 0.005 s, face temperatures are approximately 2050.73/2181.30/2191.89 K. The 64-to-128 outgoing-energy difference is about 1.9%, with decreasing depth and timestep differences. This validates that specific initial liquid test, not a long-time crust front.
- A seeded near-liquidus one-second case with 0.1-K local error control agrees with the 0.0005-second reference within the tested face and outgoing-energy bounds. Stable uniform liquid has no invented convection; increasing viscosity reduces enhancement in the tested soft regime; cooled mixed/solid skin interrupts connectivity. Conservation, cancellation, budget rejection and canonical globe publication are tested.
- Exploratory seeded level-one integrations to four physical hours generate owned solid surface layers alongside liquid cells. The 64-layer 0.5-K and 0.1-K trajectories both have 11 solid emitting cells and six liquid emitting cells at four hours, with about 0.014 m maximum contiguous solid thickness. At one hour their first-solid classifications differ: onset timing is not converged. These counts are exploratory, not a released visual result or mechanically coherent crust.
- The earlier fixed 0.05-second seeded three-minute run crystallized cool cells prematurely compared with error-controlled runs. Its apparent crystallization is not acceptance evidence. A stringent 128-layer seeded initial transient can require timesteps below the estimator's supported clock resolution; rejected trials must not be presented as validated evolution.

Before primary activation: establish crust-front and onset convergence, integrate error-controlled asynchronous stepping without changing sync/replay semantics, inspect actual main-renderer orbit/local appearance, and complete full CI. A cold emitting skin or thermal solid thickness alone is not a crust-fracture or lava-channel model. The user's PF 0.0.47.0 visual rejection remains in force.

## 2026-10-14 follow-up: scheduling, depth and crystal-bearing fluid

The coupled 64/128-layer references now refine a fixed 64-layer parent mesh. Parent mass and enthalpy are partitioned conservatively; the former sixteenfold reduction of the surface cell is replaced by twofold subdivision. Integer trial reduction also tests two microseconds before declaring the trajectory unsupported. The nested seeded one-second case completes at 0.1-K local tolerance. Earlier direct-quartic rejection evidence remains historical and is not erased.

`AdvanceAsync` now shares the synchronous adaptive controller, yielding on accepted/rejected work without resetting caps or advancing an extra physical clock. The primary application's async path uses it. Exact request-matched replay and atomic cancellation are checked.

An optional suspension branch uses eta=eta_m[(phi rho_s+(1-phi)rho_l)/(phi rho_s-0.67(1-phi)rho_l)]^2.5, capped at 1e21 Pa s. Nonpositive denominator marks the jammed/nonfluid domain. This is the suspension part of the cited paper's equation 7, not its complete solid-viscosity model. Interfaces use the larger endpoint viscosity; mixture density, latent capacity and phase expansion follow the existing material law. The model currently excludes solid density below liquid density from its buoyancy approximation. These are declared approximations, not calibrated crust strength or a velocity solver.

The phase-barrier fixture now checks owned mixed material explicitly; a cold emitting face above a fully liquid volume is not proof of solid material. A 64-layer suspension trajectory completes four hours with partial crystallization but no fully solid surface cells. The 128-layer four-hour request still exhausts the research trial budget and rejects without publication. See the [dated evidence](handover/PF-EARTH-20261013-A-20261014-adaptive-async-depth.md). Primary activation and visual acceptance remain outstanding.
