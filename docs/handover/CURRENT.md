# PlanetForge — active continuation state

**Workstream:** PF-EARTH-20261013-A. One continuous canonical formation world; scientific/rendering vertical slices.

## User acceptance and priorities

PF 0.0.45.0 **is visually rejected**: Safari captures at T+0/5/30/120 s show a smooth orange sphere becoming gray, with a distracting stellar highlight. The continuous flow and minimal UI work; this is not convincing planet formation. The user explicitly prioritizes visual credibility over performance for the next slice. No fake geological texture, convection, exaggerated terrain or scripted era transitions.

## Verified production and current candidate

- Production source `4bcdbc613b0b01741a843a0e76cbc7f04f0e73f8`, **PF 0.0.45.0**; Pages **38455427294** passed build/deploy/all six exact-SHA live checks. Main documentation is `6ae2edebed3fa071ec4c57f8d0dfb77e51e78051`.
- **PF 0.0.46.0 candidate**, branch `feature/pf-molten-regional-material`: phase is reconstructed at the radiating face, rather than assigning the warmer volume's melt fraction to the emitting skin. The diagnostic owns no mass/energy. Initial shell heat now has a declared seeded, bounded multi-scale distribution at 1,536 physical columns; every owned initial volume remains fully molten, 1950–3400 K, with exactly the same funded 2200 K mean enthalpy. This is an initial-condition hypothesis, not simulated impacts or convection.
- Shared orbit/local optics use two crystal/liquid BRDFs, dark silicate optical hypotheses and one fixed exposure. **Corrected radiometric units:** blackbody solar irradiance and Planck surface emission now share the same radiance reference. Previously the solar/1361 scaling overlit cold rock by roughly an order of magnitude. Per-channel camera response allows hot pale highlights. Compact Wendland reconstruction removes singular cell-centre hot plateaus. Local fragments sample their geographic positions, not one constant anchor value.
- Primary camera initially faces an actual warm region. Startup controls remain disabled until renderer initialization; renderer/thermal-module URLs change with the release. Play/pause/step/restart still drive one world. Yielding one-/five-second candidate batches keep the browser responsive without intermediate world publication; exact final layer enthalpies match the synchronous method, and cancellation retains the previous world.

## Validation and gates

- **CI recovery (next commit):** First full PR #225 run `38464077990` passed WASM/.NET and browser shards 2/3 but four legacy geology/water camera assertions failed in shard 1. Cause: versioned `planetRenderer.js?v=0.0.46.0` and the overlay's unversioned import created two independent ESM renderer instances. A stable `planetCameraState.js` now binds one authoritative camera from the live renderer (including cleanup on dispose); both overlay and renderer re-exports read it, with explicit versioned/unversioned browser comparison. The updated release also busts the water-overlay import cache, avoiding a stale Safari module after upgrade. **Await new CI**; do not merge or claim deployment on this change alone.

- Local domain **309**, application **165**, infrastructure **264** (738 total), including energy/mass/all-molten bounds for 18 seeds, exact seeded replay, emitting-skin interpretation, 32/64/128-layer refinement and asynchronous enthalpy/cancellation tests. Final scientific revalidation passed; Release WASM publish succeeded.
- The nonuniform 180-second reference must contain a fully solid emitting skin beside remaining melt, decreasing face differences on refinement, maximum 64/128 face difference under the existing 10 K screening envelope and reconstructed solidus-depth difference under 2 mm. This screening is not calibrated Earth accuracy or a proof of every spatial/time limit. Do not reuse the earlier uniform-field 0.52 K figure as this candidate's result.
- Previous local candidates passed five formation browser tests; a missing Restart handler found during editing was repaired. Final formation/optics pass (including simultaneous dark skin and residual emission) and full CI are required. New cold-start regression holds renderer import and verifies disabled controls. Production smoke requires the new version and exact source SHA.
- Candidate is **not live or visually accepted** until full CI, merge, exact-SHA Pages/live tests, and human visual inspection. Inspect main PC/portrait/orbit/local captures and reject numerical grid artifacts. Request seed **24061984**, T+0/30/180/300 s at the same angle, then rotate/zoom. No reference flag is needed.

## Physical limits and next meaningful visual milestone

The reference remains a dry/airless ~7 m conducting shell, not whole-depth calibrated Earth evolution. Regional heat/phase differences are genuine state; morphology is still smooth and there are no simulated fractures, crust rafts, magma currents, volcanoes or mountains. Do not claim changing glow completes the visual milestone. Optical constants, blackbody stellar spectrum, initial heat spectrum and camera exposure are explicit hypotheses.

Continue toward physically carried persistent crust structure and molten/solid contrast on the main globe. Implement missing physical transport/mechanics before depicting flows or cracks. Atmosphere/steam, snow/ice, routed water, climate environments and ecology remain subsequent coupled slices, reusing Hero/hydrology/climate. Preserve one ongoing simulation and minimal UI.

Detailed earlier evidence: [molten main release](PF-EARTH-20261013-A-20261013-molten-main.md), [solidification](PF-EARTH-20261013-A-20261013-solidification.md), [archived history](PF-EARTH-20261013-A-20261013-pre-vertical-history.md). Read CONTINUE and independently verify GitHub before resuming.
