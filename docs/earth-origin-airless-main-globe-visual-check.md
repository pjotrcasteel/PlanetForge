# Opt-in main-globe thermal reference — M5h

## Discoverable main-planet entry (0.0.39.0)

On the normal `https://pjotrcasteel.github.io/PlanetForge/` homepage, tap **HOT ROCK TEST** next to the existing progression button. This works even if the geological research view is active; its current research overlay is detached before the physical thermal reference is published. The main globe stays in place. **ADVANCE 1 HOUR** executes the accepted adaptive cooling, **RESET** rewinds the thermal reference and **BACK** returns to the legacy scene without changing the existing planet seed.

The old URL `?visualTest=1&airlessReference=1` remains supported, but iOS users no longer need to preserve a query string or manually navigate to a special address. The visible version badge is **0.0.39.0**; Git SHA still identifies the exact deployed commit. Both local browser tests and the live GitHub Pages smoke must explicitly enter the new reference, advance it, and exit—not merely load the default Snowball experiment.


This is the first **user-testable, scientific-boundary-controlled** thermal evolution on the existing 3D PlanetForge planet. Open the same site with:

`?visualTest=1&airlessReference=1`

Select **ADVANCE 1 HOUR** and observe the actual active surface-rock temperature, bulk enthalpy-derived mantle melt fraction, elapsed physical time, and thermal incandescence on the existing orbital/local WebGL terrain. **RESET** restarts the exact initial state; the default Frozen Climate Experiment and its existing saves are not modified.

## Scientifically honest scope

- The scenario is explicitly **AIRLESS ROCK · CONTROLLED THERMAL REFERENCE**. It is not a Hadean-Earth atmosphere, proof of first crust, an origin-of-life milestone or a historical reconstruction.
- The **canonical `PlanetOriginEpochState`** owns interior enthalpy/melt and the surface sensible store. Actual surface temperature derives from the latter (initially 1600 K); the rock interior begins at 1750 K and is partly molten, but that *bulk mantle fraction* is not a spatial lava map.
- Water and gas inventories are exactly zero; no rain, oceans, steam, snow or vegetation may appear. The existing baseline mesh is reused for LOD 0–3; **its geographic relief is placeholder/research terrain**, not simulated primordial crust.
- Every user advance integrates 3600 physical seconds of Fourier conduction from the bulk interior to surface rock plus actual surface Stefan–Boltzmann emission and stellar absorption into the same conservative ledger. The adaptive stepper re-evaluates rates every 60 seconds and rejects physically inadmissible candidates. No fake cooling curves.
- Initial parameters are explicit **numerical references**, not uniquely measured primordial Earth conditions: mantle 4e24 kg, 1200 J/(kg K), 400 kJ/kg latent heat, effective solidus/liquidus 1300/1900 K; active surface-rock material 1e19 kg with specific heat 850 J/(kg K), conductivity 3 W/(m K), assumed effective conduction boundary 25 m, Bond albedo 0.15, IR emissivity 0.9. The stellar flux and world radius come from the existing planet's physical state.
- Shader incandescence is **a display tone map of the one uniform conserved surface-rock temperature**, not simulated local magma patches, sediment, combustion, or a thermal input. Underlying rendering uses the same actual terrain data, seed and camera.
- The epoch's absolute 4.54 Ga reference clock is retained internally for compatibility, but only **elapsed physical reference time** is shown in this test. No assertion of a particular early-Earth cooling chronology is made.

## Acceptance

- Clicking ADVANCE changes the projected actual surface-rock temperature and visible blackbody-inspired incandescence; revision/time advance consistently and mass/energy remain conserved.
- RESTART restores exactly the initial energy/temperature and visual state; replay reproduces the same values.
- No extra tabs or new terrain generators. Both the main globe and local view accept the thermal tone mapping without false geographical lava or vegetation.
- Browser screenshot/visible WebGL pixel, shader error, clock, cooling, reset and legacy default tests guard desktop and iPhone-compatible paths.
- This is **not** the final molten-to-crust visual milestone. Steam/volatile physics (#180) and conservative primary-crust genesis (#184) remain prerequisites for a defensible *Earth-origin* scenario.

## Test

After GitHub Actions succeeds and GitHub Pages deploys, visit:
`https://pjotrcasteel.github.io/PlanetForge/?visualTest=1&airlessReference=1`.

Compare the initial incandescent bedrock with a few accepted one-hour steps. The exact Celsius/Kelvin readouts and absence of water/steam are more important than the amount of visible color change. If this opt-in test is unstable on iPhone Safari, retain the tested default simulation and fix the issue before proceeding.
