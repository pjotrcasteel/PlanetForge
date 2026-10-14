# Depth-resolved lateral heat and melt transactions

Workstream `PF-EARTH-20261013-A`, candidate 2026-10-14. This is a transport/publication prerequisite, not a visually accepted magma-ocean release.

## Lateral heat

`PlanetOriginRockColumnField.ProposeLateralHeat` advances Fourier flux through actual shared spherical faces:

`Q_ij = k * edge_arc_length * vertical_overlap / centre_arc_distance * (T_i - T_j) * dt`.

Each undirected cube-sphere face is visited once, including face seams. Layers are intervals above the original basal datum. Density contraction changes their physical widths, so a layer may contact two layers in its neighbor; matching depth indices is incorrect. A two-pointer overlap sweep resolves those contacts. The supplied active depth cuts out a physical near-surface extent in each column, rather than mixing its entire aggregate energy.

Each Q is subtracted from one owned layer and added to its peer. No mass changes. The sensible heat capacity `m cp` supplies a conservative lower bound through the fusion interval. Each layer requires `dt * sum(G_faces) / (m cp) <= 0.5`; instability rejects the candidate without flux clipping. The bounded column integrator now includes lateral incidence in its timestep bound. Vertical exchange is proposed first and lateral exchange second, with the second operator checking its actual updated geometry. This is first-order operator splitting; temporal refinement remains required.

New layers retain compensated enthalpy addition. Global surface enthalpy must close within the established relative tolerance, and the unchanged thin-shell geometry limit is checked again after lateral evolution. Cancellation never publishes partial results.

## Explicit molten-material transactions

`PlanetOriginMeltFaceTransfer` addresses an existing donor and recipient layer across a real shared spherical face, with an explicitly supplied integrated mass. `ProposeMeltTransfers` carries donor-specific sensible plus latent enthalpy:

`E_carried = m_transfer * E_donor / m_donor`.

Withdrawals use the unchanged original donor state. Incoming material cannot conceal an overdraw. The numerical transport admission permits at most one quarter of each original donor layer's mass per transaction batch; callers must subdivide larger physical intervals. Both layers must be fully molten and physically overlap. Single-phase transactions cannot silently advect crystals, penetrate solid rock, or model an unresolved porous mush. Extrusion onto a solid surface requires a separate interface/deposition model.

Every mass debit has an identical recipient credit, as does its transported enthalpy. Layer heat capacity and volume are recomputed from the new owned mass. Recipient height growth and donor height loss therefore reach `PlanetOriginRockColumnElevationSource` without artistic amplification, copied geography or a second terrain state. The thin-shell validity limit still applies.

`PlanetOriginCanonicalRockCells.AdvanceWithMeltTransfers` composes one thermal interval and the explicit material transaction into a single unpublished canonical candidate. Existing `PlanetExperience.PublishRegionalRock` publishes the matching epoch, material atlas and terrain atomically. Tests verify that the primary shared globe terrain receives the actual mass-derived elevation, while the original world remains published until the final commit.

## Scientific limits and next primary slice

This API **does not generate a flow field**. A physically supplied transaction is not a solved pressure gradient, buoyant current, velocity or lava channel. The thermal/material account conserves mass and enthalpy; it is not a full mechanical-energy conservation claim. Gravitational potential, momentum, surface work, viscous dissipation and mechanically coherent crust remain unmodeled. A flow solver and corresponding mechanical work account must be supplied before default physical mass flow is enabled. The transaction test inputs are controlled numerical tests, not a seeded animation or an additional production scenario.

The default formation reference still contains a roughly seven-metre surface shell. Its spatial cells cannot resolve metre-scale lava channels at planet-wide sampling. Do not compensate by scaling gravity, viscosity, time, fracture widths or relief to make a video. Replacing that inadequate magma-ocean reference, supporting its declared physical time scale and constructing physically sourced crust morphology remain the next substantial work. Existing production controls, canonical-world ownership and orbit/local projection are preserved.

## Validation

- Uniform lateral equilibrium and unchanged layer masses.
- Cube-seam exchange with a shallow active extent and untouched deep layers.
- Different physical layer widths contacting different neighbor depth indices.
- Matched-time temporal refinement, deterministic replay and closed canonical heat accounts.
- Rejection of excessive incidence, invalid inputs and cancellation without mutation.
- Actual melt mass/enthalpy transfer, unchanged donor temperature under upwind material removal, and real volume/height change.
- Original-donor overdraw despite incoming material, non-neighbor/non-overlapping addresses, solid/mush rejection and deterministic replay.
- Primary experience publication of the same epoch/atlas/terrain, with exact physical time and conserved aggregate rock mass.

Full CI/browser acceptance and human visual acceptance are separate gates. No deployment or demo-ready lava/crust morphology is claimed by these transport tests.
