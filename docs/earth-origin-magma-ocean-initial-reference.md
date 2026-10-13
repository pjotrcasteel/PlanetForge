# Canonical magma-ocean initial-surface reference (PF 0.0.41.0)

This **opt-in main-globe reference** is a documented, hypothetical *fully molten starting surface layer* intended to address user feedback that the previous regional reference looked like dry terrain with one bright spot. It is not an Earth-history chronology, calibrated global Hadean magma ocean, crust material model, mantle circulation solver or steam atmosphere.

## Input and canonical funding

- Launch: `?visualTest=1&magmaOcean=1` on the existing main planet; the earlier `?regionalRock=1` contrast test and default Snowball remain separate.
- Initial mantle bulk-temperature reference: 2400 K; fully molten active surface layer: **2200 K uniform funding** prior to lateral redistribution.
- Effective rock phase material: sensible `cp = 850 J/(kg K)`, fusion enthalpy `400000 J/kg`, solidus 1300 K and liquidus 1900 K. These are declared reference parameters, not universal composition-dependent rock phase curves.
- The 1e19 kg **active surface shell** is physically funded once in the existing canonical `SurfaceRock` environmental account; the rock material is split into fixed spherical cells by physical area. At Earth radius this mass equals about 20,000 kg/m² (~7 m at 3000 kg/m³). It **does not imply** only seven metres of Earth's mantle are molten or that the entire magma-ocean depth is already modeled. The underlying interior has a separate energy budget.
- Explicit smooth regional initial-temperature contrast is created by **redistributing 8% of existing surface-shell enthalpy** over broad, deterministic spherical harmonics. It does not add a localized impact or independently synthesize energy in the renderer. Assert the minimum cell melt fraction remains 100% at initial time.
- No initial water or atmosphere is populated; the existing dry/airless radiative boundary and same canonical revision/energy ledger advance the experiment in 300 s accepted steps. Rates are explicitly limited to that reference regime.

## Globe, shaders and limitations

The orbital 3D and local LOD phases read the same canonical projected per-cell temperature and melt fraction atlas. At a virtually fully molten material cell the shader **replaces dry-bedrock albedo** with a temperature-driven molten surface appearance; a partially molten cell retains mixed solid/liquid appearance. Minor fixed material mottling is optical presentation only, not a second heat field, magma current, crack network, crust, plate, lava-flow path or geological history.

The next genuinely physical steps are mantle/surface heat-exchange calibration, spatial convection/advection as warranted, coupled crystallization/material transfer into **persistent primary crust**, and pressure/steam radiation/volatile transport. Crust, mountains, steam, clouds, precipitation and water do not appear merely because the surface shade changes.

## Acceptance and release

- Deterministic exact initial enthalpy, all cells liquid, nonzero regional thermal contrast, same-water/no-atmosphere, ledger conservation and 1-hour cooling tests.
- Original regional contrast and Snowball regressions remain green; WebGL2/local/orbital projection validation and 390 px iPhone controls remain intact.
- Do not claim on-phone visual quality until GitHub Actions CI passes, main merges, GitHub Pages exact SHA/version and live smoke pass **and the user inspects T+0/T+1h**.
- Workstream continuity: `PF-EARTH-20261013-A`, [active handover](handover/CURRENT.md), [roadmap #196](https://github.com/pjotrcasteel/PlanetForge/issues/196).
