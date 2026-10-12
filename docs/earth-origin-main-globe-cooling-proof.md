# Earth-origin main-planet cooling acceptance (M5g)

The immediate goal is not another shader or diagnostics tab. It is demonstrating that **one actual energy-conserving physical thermal process** can advance a published geological epoch and reach the **existing main 3D planet** in a coherent way.

## Full-stack reference

The application integration test creates an explicitly parameterized and initially **partly molten** dry rocky planet:

1. The canonical `PlanetRockEnthalpyModel` stores a hot interior and derives a true latent-enthalpy melt fraction.
2. A separate material-bearing `SurfaceRock` store starts hot, with **no gas, steam, liquid or ice**.
3. `PlanetExperience.PublishOriginEpoch` emits the existing main planet snapshot with zero pressure, a dry ocean surface, stable terrain geometry and the geological clock.
4. `PlanetExperience.AdvanceOriginEpochAdaptive` invokes the **real**, state-recomputed, vacuum-only `PlanetOriginAirlessThermalProcess.Propose` across short accepted physical steps. Fourier heat flux moves energy from interior to surface; Stefan-Boltzmann surface emission loses energy to space; absorbed stellar radiation is credited to the surface. It is **not** a scripted cooling rate.
5. The resulting **single accepted revision** reaches the existing render path. The tests verify the same geological time/revision, lower rock and surface temperatures, decreasing enthalpy melt fraction, conserved H2O, dry sea level, real gas pressure, deterministic replay and no change in canonical terrain geometry/seed.
6. A steam-bearing state categorically rejects the vacuum process, and a canceled integration cannot replace any previously visible frame.

The chosen masses, conductivity, boundary thickness and temperature are explicit **numerical verification fixtures**, not a scientific claim about Earth at 4.54 Ga. No supercritical steam, convection, primitive plate geology, crust nucleation, first rain, first ocean, or surface lava geography is inferred by this acceptance proof.

## Follow-on physics

An actual early-Earth scene requires evidence-bounded steam radiative-convective transfer (#180) and mass/enthalpy-conserving **primary-crust formation with a spatial surface state** (#184). Only validated local crust/temperature should drive incandescent rock materials and relief on the real orbital/local LOD 0–3 planet. Existing main-globe renderer/WebGL/iPhone tests remain gating for any visible change.

This milestone is a **test-proven vertical contract**, not a user-facing Earth-origin launch. It reuses the established opt-in `PlanetExperience` and introduces no other UI, climate timeline, terrain generator or cinematic effects.
