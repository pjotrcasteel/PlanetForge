# Earth-origin radiative transfer kernel — M5e

The Earth-origin world needs a physically meaningful way to transfer thermal photons through steam **before** selecting an integrated cooling rate. A direct `σT_surface⁴` infrared loss is correct for a black **airless** surface, but incorrectly treats a dense absorbing steam column as transparent.

This milestone provides a narrow, mathematically controlled **single grey, non-scattering, isothermal LTE layer** solver in the Domain package. It introduces no new simulation world, diagnostic tab, renderer material, invented chemical composition, calibration constants, or optical-depth-to-pressure conversion.

## Radiation model

For a plane-parallel layer of supplied vertical absorption optical depth `τ`, LTE atmospheric temperature `T_a`, a black surface `T_s`, and outgoing direction cosine `μ`:

`I_top(μ) = B(T_s) exp(-τ/μ) + B(T_a) [1 - exp(-τ/μ)]`.

The hemispheric surface-to-space transmission is

`t(τ) = 2 ∫₀¹ μ exp(-τ/μ) dμ = 2 E₃(τ)`.

The implementation uses deterministic **16-point Gauss–Legendre hemisphere quadrature**, with the well-known `t(1) = 0.2193839343955204` exponential-integral reference checked by test. The kernel calculates:

- Surface upward emission: `F_surface = σ T_s⁴`.
- Surface photons transmitted to space: `t F_surface`.
- Surface photons absorbed by gas: `(1-t) F_surface`.
- Atmospheric thermal photons leaving the column upward: `(1-t) σ T_a⁴`.
- Atmospheric thermal photons returning to the black surface downward: the same amount.
- Planet's net outgoing thermal flux: `F_TOA = t σ T_s⁴ + (1-t) σ T_a⁴`.
- Surface and atmosphere **separate signed net cooling powers**; their sum equals `4πR² F_TOA` for every supported finite input.

Vacuum `τ = 0` recovers black-surface Stefan–Boltzmann. Large `τ` radiates from the **atmosphere**, not the surface. An isothermal column always radiates `σT⁴`, independent of `τ`; the solver must also allow a hotter gas column to **warm** the surface. Mass is not generated and no energy ledger is mutated by this pure diagnostic calculation.

## Strict validity boundary

**Not a calibrated Hadean radiative-convective atmosphere.** This model assumes grey integrated longwave absorption, zero scattering, an isothermal well-mixed LTE layer, a black surface, no clouds, no vertical/lateral heat transport, no spectral H2O/CO2 absorption windows, no pressure/temperature-dependent gas opacity, no magma-specific emissivity and no radiative heating by stellar shortwave absorption. The supplied `τ` is NOT inferable from water pressure using a free scalar without documented opacities.

Before connecting it to `PlanetOriginEnergyLedger` or `PlanetOriginAdaptiveIntegrator`:

1. Establish optical depth from pressure, temperature, steam/dry-gas composition, and measured/calibrated absorption coefficients or line-by-line/k-distribution radiative data, with explicit applicability bounds.
2. Resolve at least a vertically stratified thermal state where an isothermal approximation is invalid; use a validated convection closure and humidity/condensation profile.
3. Conservatively debit **surface** emission, credit atmosphere absorption/back-radiation, debit atmospheric upward/downward emission and release **only** TOA radiation externally. A single generic `RadiativeExchange` from one account cannot represent this two-reservoir balance correctly.
4. Ensure opacity changes from outgassing, pressure and water phase transitions trigger recomputation on every adaptive step; reject unsupported hot/supercritical steam regimes.
5. Verify source-bound reference cases before calling it a plausible early-Earth greenhouse.

This first kernel proves analytical limiting behaviour and radiative **boundary** accounting; it does **not** determine the Earth's cooling time, first condensation date, crust thickness or ocean stability.

## Acceptance

Tests cover `τ=0`, `τ=1` against the exponential-integral reference, optically thick atmosphere, isothermal invariance, a hotter atmosphere warming a cooler surface, signed power conservation, and invalid/nonfinite physical parameters. It remains unused by the currently playable climate experiment and main planet until a calibrated atmospheric opacity/energy-transfer pipeline exists.

Future work remains in [issue #180](https://github.com/pjotrcasteel/PlanetForge/issues/180) and the canonical first-crust integration in [issue #184](https://github.com/pjotrcasteel/PlanetForge/issues/184).
