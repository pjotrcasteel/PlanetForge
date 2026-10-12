# Earth origin — mixed-atmosphere pressure and water saturation diagnostic

This is the **model-only** next dependency after the conserved volatile
water/enthalpy integration in M4b (#168). It does not change the playable
Snowball Climate Experiment, terrain provider, physical oceans or LOD 0–3.

## Scientific limits and what is actually calculated

`PlanetMixedAtmosphereSaturation.Evaluate` reads the **existing**
`PlanetWaterInventory.AtmosphericVapor` mass. A caller supplies the mass and
representative molar mass of noncondensable atmospheric gases, global planet
radius and surface gravity, and an **explicit effective liquid–vapor
equilibration temperature**. It does not infer that temperature from a mantle
state, orbital shader, lapse rate or the atmosphere's current well-mixed heat
capacity. None of those temperatures is automatically a cloud-condensation
temperature.

The first hydrostatic global column approximation uses

- `A = 4πR²`, `p_total = g (m_dry + m_H2O)/A`;
- ideal-gas mole fractions, `x_H2O = (m_H2O/M_H2O) / ((m_H2O/M_H2O) + (m_dry/M_dry))`;
- `p_H2O = x_H2O × p_total` and `p_dry = p_total - p_H2O`.

**Partial pressure must use mole fraction, not the species' mass fraction.**
The dry-gas mean molar mass is required precisely because early Earth must
not silently inherit a modern nitrogen-rich atmosphere. It is one lumped
gas with a caller-selected composition proxy, not a constituent chemistry
model.

Only from **273.16 to 647.096 K**, the existing
`PlanetWaterVaporSaturation` IAPWS pure-water liquid–vapour curve is
evaluated. The saturated-vapour *diagnostic* then solves for the remaining
atmospheric H2O mass via bounded bisection, recomputing the total hydrostatic
surface pressure and mole fraction after each proposed removal. It must not
assume the original pressure remains fixed while steam condenses. If the
initial partial pressure is below saturation, the proposed condensate is zero.

Below the liquid triple point the diagnostic returns
`BelowLiquidTriplePoint` with **no condensation or ice proposal**; above
the water critical temperature it returns
`AboveWaterCriticalTemperature` with **no liquid–vapor saturation
proposal**. Pressures are still reported in both regimes as bounded
hydrostatic estimates, but never extrapolate the IAPWS saturation formula.

## Verified numerical checks

The tests cover:

- parity with the earlier pure-steam hydrostatic approximation;
- changing dry-gas mass and representative molar mass, retaining the
  *same* source H2O mass;
- re-evaluation of the proposed remaining steam column near equilibrium;
- no imaginary water in a dry atmosphere;
- explicit rejection outside formula validity;
- validation of finite radius, gravity, atmosphere mass, molar mass and
  effective saturation temperature;
- no inventory mutation, ocean creation, or implicit phase changes.

## Non-goals before automated condensation

The global column assumes hydrostatic support and an ideal, homogeneously
mixed gas. It does **not** model fugacity and saturation enhancement from
noncondensables at high pressure, a vertical temperature profile, cloud
condensation nuclei, radiative convective stability, supersaturation kinetics,
water droplet formation, rainfall/runoff, surface pressure effects on liquid
water chemical potential, hydrodynamic escape or Hadean gas composition.

High-temperature and high-pressure early steam can violate the ideal-gas
assumptions even where IAPWS's pure-water temperature curve exists. Before
using this diagnostic to **advance** a coupled epoch, add composition/provenance,
regime-dependent equation of state and chemical potential corrections,
pressure-aware thermodynamics, latent and sensible energy, bounded
physical rates, adaptive substepping, and strict one-epoch atomic publication.

Only after those gates should condensed H2O be routed to the **existing
canonical physical surface**, basin hypsometry and hydrology system. Do not
add separate ocean or snow shader masks.

Related: `docs/earth-origin-scientific-foundation.md`,
`docs/earth-origin-thermal-coupling-contract.md`, #143, #167.
