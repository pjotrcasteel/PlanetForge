# Pressure/work continuation checkpoint — 2026-10-14

Workstream `PF-EARTH-20261013-A`. Branch `feature/pf-viscous-melt-work` extends the fully CI-validated transport candidate #229 (`37c3f0e558f5e201f4e58a07bcf57cb86da9c27e`, CI 38517993944) and the earlier material correction #228.

The new model **generates** hydrostatic/viscous face currents from owned material height. It transports liquid mass and enthalpy and pays exactly matched dissipative heat from a finite, initially funded excess-potential account. A mass datum preserves exact rest, and the bounded integrator recomputes both thermal and flow admission. Actual pressure-generated changes were published through the existing main-world epoch/atlas/terrain pipeline in tests.

Read [the complete physical/numerical contract](../earth-origin-viscous-melt-work.md). This is a fixed-density, low-Re thin-film reference; it rejects unsupported inertia, excludes solid/mush regimes explicitly and does not implement deep magma-ocean overturn or coherent crust. The default unequal-density seven-metre reference is incompatible and has not been silently replaced. There is no fake surface velocity, visual stage switching or exaggerated terrain.

Local Release builds passed with no warnings/errors. All 71 relevant regression cases passed in process, including old energy/phase/clock behavior, new available-work cases and generated-flow terrain publication. Full candidate GitHub CI is a separate pending gate; production and human visual acceptance have not changed.

Next primary work remains the appropriate finite magma-ocean reference, nontrivial physical time/resolution, and density/phase/mechanical coupling appropriate to crust genesis. Do not interpret this creeping-flow primitive as a visually completed Earth-origin model. The user's PF 0.0.47.0 screenshots remain rejected and no new device check is warranted yet.
