# M6a — spherical lateral surface-rock heat exchange

The regional enthalpy field introduced in M5k is an owned partition of the existing canonical `SurfaceRock` reservoir. This increment adds **internal, optional, physically dimensioned heat transport**. It is not magma flow, mantle convection or primary crust.

## Discretization

For every unique face-to-face neighbor pair on `PlanetSurfaceGridLayout`, including across cube-face seams:

- Derive the shared spherical edge length from the **actual cubed-sphere vertex arc**.
- Derive neighbor separation from the physical great-circle center distance.
- Compute a conductive edge exchange, in joules, using `k [W/(m K)] × activeLayerThickness [m] × edgeLength [m] / centerDistance [m] × (T_A − T_B) [K] × elapsedSeconds [s]`.
- Debit that exchange from the warmer cell and credit the same amount to the cooler cell; a pair is processed exactly once. Cell temperatures come exclusively from rock sensible+latent enthalpy inversion, not shaders.
- Check the proposed explicit step against the conservative minimum sensible heat capacity `m × cp`: `dt × Σ(conductance_i) / (m_i × cp) <= 0.5`. Reject unstable durations; do **not** silently clamp fluxes or mutate source cells.
- Check finite nonnegative enthalpy, deterministic replay, cancellation and closure of the global field's original energy.

`PlanetOriginCanonicalRockCells.Advance` optionally applies this process **after** the airless stellar/interior/IR boundary proposal and **before** the epoch's atomic commit. Lateral transport has no entry in the external global energy ledger because it moves energy only inside one reservoir. Zero lateral conductivity retains the M5k reference and all older tests unchanged.

## Limitations and next physics

The first operator uses a prescribed, effective shell thickness and thermal conductivity. It is a bounded testable transport law, not a calibrated early-Earth convection or magma-ocean circulation closure. At 384 spherical cells on an Earth-sized world, realistic solid-rock conduction between cell centers separated by hundreds of kilometres is **very slow**. It cannot legitimately make lava redistribute rapidly, form mountains, or change the planet to a scripted crust texture.

Next steps: adaptive scheduler splitting for multi-step geological time, calibrated vertical mantle/surface exchange including reverse transfer, material transport/differentiation, actual primary-crust mass and thickness from solidification, and documented regime validity. Do not enable an atmosphere or rain with the airless test law. Further visual milestones must remain tied to the same canonical globe and accepted geological revision.
