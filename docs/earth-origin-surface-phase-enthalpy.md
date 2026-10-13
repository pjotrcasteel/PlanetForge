# Earth-origin surface phase enthalpy — M5j (non-publishing thermodynamics)

## What was missing

The existing hot-rock iPhone reference uses a single *sensible-energy* `SurfaceRock` reservoir and maps its temperature to an orange tint. It cannot faithfully represent surface-solidification heat because the existing rock surface account does not include fusion latent energy. A “lava / crust fraction” threshold in the shader would violate the physics contract.

M5j introduces a **strict phase-enthalpy model over the same M5i area-weighted cubed-sphere surface cells**. It reuses the existing `PlanetRockEnthalpyModel` rather than inventing separate solidification equations.

## Conservation and canonical provenance

- `PlanetOriginSurfaceRockPhaseField.FromSolidCanonicalSurface` partitions only the active mass and stored sensible joules of the **same canonical energy ledger** at a safely fully solid state. Sensible energy and enthalpy agree below solidus under the present zero-K reference. The material's heat capacity MUST match the canonical heat capacity.
- If the canonical surface is hotter than the supplied material solidus, initialization is **rejected**: the legacy sensible-only ledger has no corresponding fusion-latent budget. In particular, **do not use this initializer on the 1600 K hot-rock test**; a dedicated atomic migration from mantle/environment accounts is still required.
- Each cell carries exactly one conserved stored energy (`StoredEnthalpyJoules`) plus its unchanged material mass and true spherical area. Temperature, partial melt fraction and fully crystallized state are **derived** by inverting that enthalpy using material heat capacity, solidus, liquidus and latent heat.
- `Propose` consumes physical conduction, absorbed starlight and emitted infrared **in W/m²**, integrated over an exact physical duration. Its candidate's total energy equals the old total + imported conduction + stellar energy − emitted infrared (within bounded floating-point tolerance).
- Cooling from above the liquidus through the mushy interval releases fusion latent enthalpy before fully solid sensible cooling. Heating traverses the same path in reverse. Rates and energies are never clipped to force crystallization.
- Invalid/unfunded transformations, empty material, mismatched cp, negative stores and cancellation are rejected without modifying the original canonical world or the proposed field.

## What this does *not* claim

This is an **offline physics trial**, not a second planet, second clock, or new renderer material. It is not yet an accepted coupled `PlanetOriginEpochState` update: the canonical `SurfaceRock` reservoir still interprets its joules as sensible heat. Publishing these enthalpy-derived temperatures before the corresponding canonical ledger semantics are updated would produce inconsistent results.

The provided solidus, liquidus and heat capacity must eventually vary with composition and pressure and be calibrated against relevant mineral and magma reference data. The effective linear mushy interval is a *documented approximation*, not a realistic differentiated crust column.

**Rock crystallization is not crust formation.** A persistent primordial crust requires mechanical/chemical segregation and mass transfer, possibly recycling/remelting; neither elevation nor plate motion can be inferred from this model.

## Integration gate

After independent CI of M5i/#191 and M5j, the subsequent milestone will provide a single atomic commit path:

1. Transition canonical `SurfaceRock` from sensible energy to explicit enthalpy **without adding initial latent energy**; fund any existing hot-state latent energy by a balanced transfer from an existing reservoir or initialize the whole system consistently.
2. Attach regional material cells to the immutable `PlanetOriginEpochState` with a sum-of-cells = canonical energy budget invariant.
3. Integrate bounded thermal transport and radiation, with adaptive substeps and cancellation, and debit credits to the mantle and stellar/radiative ledgers exactly once.
4. Publish the accepted same-revision local temperature/phase field to the existing main globe and LOD 0–3. Only then should visible melt/solid patches respond to the simulation.
5. Implement material stratification, crystallization/differentiation and persistent crust before labeling the visual as **primary crust**.
6. Keep steam/volatile cooling dependent on validated radiative–convective opacity work (#180); no airless rate is allowed for a steam atmosphere.

No new tabs, fabricated heat timelines or random lava patterns.
