# PF 0.0.46.0 — iPhone Safari visual acceptance failure

**Date:** 2026-10-13 · Workstream `PF-EARTH-20261013-A`  
**Production feature source:** `9e0b250f751815cd7585dad667380195f82d9622` · **version:** PF 0.0.46.0  
**Validation:** full PR CI 38468827454 green; Pages 38471352704 build/deploy/six production tests green. **Human visual acceptance: rejected.**

## Observed screenshots

The user submitted six actual iPhone Safari captures (visible address `pjotrcasteel.github.io`, footer `PF 0.0.46.0 · 9e0b250`) for a continuous molten-start simulation:

| Physical clock | Main-planet observation |
|---|---|
| T+00:00:00 | Near-featureless round, highly reflective-looking golden-yellow body; broad yellow-white luminous blobs |
| T+00:00:01 | Status becomes `Surface crystallizing`, practically no physical morphology change visible |
| T+00:00:06 | Yellow regions dim, smooth blurred orange sphere remains |
| T+00:00:36 | Orange-brown globe with diffuse, discrete yellow hotspots; no structural features |
| T+00:02:36 | Very dark, rounded sphere with faint red/orange patches; still smooth |
| T+00:03:06 | Almost black sphere, faint orange blobs at edge; no convincing new crust or atmosphere |

Play/Step/speed/Restart controls are legible and page advances. Screenshot evidence is about **actual rendered appearance**, not proof of scientific conservation or numerical performance. Uploaded screenshots are attached in the originating chat, not committed as project assets; do not invent an image file path.

## Root cause and scope boundary

`PlanetMagmaOceanReference` seeds only 1e19 kg of surface material over an Earth-sized area, i.e. approximately 6.5 m at 3000 kg/m³. The 64-layer `PlanetOriginRockColumnImplicitSolver` advances mostly independent radial conductive columns coupled to a very massive but weakly conductive interior; the airless Stefan–Boltzmann upper boundary radiates strongly. Seeded initial temperature contrast is genuine conserved enthalpy but **not** simulated fluid motion or crust formation.

The material shader receives phase, temperature, Fresnel/GGX reflection and fixed exposure, so it can only display a glowing smooth globe with darkening soft hot regions. Existing `ReconstructSolidusDepthMeters` and `SolidSurfaceThicknessMeters` describe *thermal* and phase-derived solidification, not mechanically mobile crust, tectonic relief, or magma-surface deformation. No atmospheric water, clouds or coupled greenhouse exist in the default formation world.

## Required corrective direction

- Preserve the truthful conservation and shared renderer from 0.0.46.0, but stop using the thin dry reference as the scientific stand-in for a full magma-ocean planet.
- Build a parameterized finite deep liquid-rock reservoir and physically evaluated convective/advective transport, with auditable formulas and validity ranges, conservative coupling to the existing enthalpy/clock, acceptance tests for cancellation/replay/mass/energy.
- Let first coherent crust and surface geometry evolve from finite crystallized mass, phase properties, stress or buoyancy; no shader-painted rifts or present-day Hero terrain.
- Coupled volatile delivery/steam/radiative-convective model (#180) must change the actual cooling, not just atmosphere visuals; water and oceans remain downstream only when supported.
- Compare same seed, camera and real physical timeline across PC and Safari LOD 0–3, and explicitly request visual sign-off. Pixel variation tests alone cannot approve the experience.

**Relevant planning:** [#184 primary-crust genesis](https://github.com/pjotrcasteel/PlanetForge/issues/184), [#180 hot steam cooling](https://github.com/pjotrcasteel/PlanetForge/issues/180), [#196 ten-milestone roadmap](https://github.com/pjotrcasteel/PlanetForge/issues/196).

**Release rule:** No visual release or “accepted molten planet” claim until substantial coupled real-world state produces meaningful main-globe changes and exact-SHA Pages smoke passes. No timer-based scenery or invented geography.
