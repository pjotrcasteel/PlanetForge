# Layer/material transport checkpoint — 2026-10-14

Workstream `PF-EARTH-20261013-A`. Branch `feature/pf-layer-material-transport` includes the fully CI-validated material projection correction from PR #228, plus conservative lateral depth-layer heat exchange and explicit single-phase melt mass/enthalpy transactions.

See [the scientific/ownership contract](../earth-origin-layer-material-transport.md) and [CURRENT](CURRENT.md). Local Release builds passed with no warnings/errors; all 30 depth-column/formation cases passed in process. The original numerical/solidification reference cases, new conservative transport cases, yielding/cancellation behavior and actual main-world mass-to-terrain publication were executed. This is not a standard local VSTest run: worker sockets are unavailable in this environment. Full GitHub CI remains separate.

A supplied melt transaction changes actual owned material and the same main-globe terrain. It does not supply a flow velocity, pressure model, gravity/work account or coherent crust. Default production still has no naturally generated lava flow, mountain growth, steam, ocean or life. PF 0.0.47.0 remains visually rejected and there is no new device-test request.

The next physical work must establish finite mechanical potential/work accounting and a momentum/pressure closure appropriate to the material/grid/physical time scale, replace the inadequate thin-surface magma reference, and then produce inspectable evolving surface morphology. Do not convert these regression transactions into seeded fake animation or amplify real millimetre-scale displacement into mountains.
