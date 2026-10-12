# Earth-origin M5c — exact geological adaptive intervals

The source clock has always stored whole Julian years separately from sub-year microseconds. The previous adaptive integrator **converted the entire interval back to `long` microseconds**, unintentionally limiting one geological process advancement to approximately **292.27 Julian years**. That prevented the future magma/cooling/tectonics driver from integrating a million-year horizon efficiently even when its selected physical substeps were valid.

## Correction

The existing `PlanetOriginDuration(WholeYears, RemainingMicroseconds)` remains the sole representation of physical elapsed time. The adaptive scheduler now:

- Compares exact `(years, microseconds)` pairs rather than making an overflowable 64-bit total.
- Subtracts time with one microsecond-to-year borrow, keeping a normalized canonical remainder.
- Halves trial durations using quotient/remainder arithmetic: odd years convert to at most one Julian year's microseconds **before** halving. No absolute-geological timestamp or double-precision conversion controls clock advancement.
- Retains the **minimum physical substep**, maximum accepted steps, maximum refinements, atomic rejection and cancellation rules.
- Preserves a representable final interval when the initial maximum would otherwise leave a remainder below the minimum step.
- Continues passing the **exact proposed duration** to the conservative thermal/water/radiative ledger. Only the caller's declared process and chosen valid physical coefficients determine how much elapsed time can actually be accepted.

Long elapsed time is now representable, **not automatically numerically safe**. A million-year trial with nonzero radiative/phase rates may still be physically wrong and correctly fail/halve/abort. The integrator does not turn a long timestep into a credible atmosphere or crust solver and does not create water.

## Regression evidence

- One million years plus an exact microsecond remainder publishes one conserved revision with no cumulative-microsecond overflow.
- A thousand-year horizon splits into 400, 400 and 200 years plus fractional microseconds exactly.
- A 1000-year overdraw refines to two 500-year candidates, re-evaluating the energy rate from the latest immutable ledger.
- An interval beyond the geological clock's present-day upper bound remains rejected, with no partial epoch publication.
- All prior subsecond, saturation-crossing, cancellation, overdraw and representable-tail tests remain in the suite.

## Scope

No extra UI/diagnostic tab, no new terrain generation, no changes to the current main LOD rendering or Snowball experiment. This removes a concrete timekeeping obstacle on the path to physically credible cooling and initial crust. A bounded radiation/steam/volatile process must still set physically safe substep sizes; long durations alone do not imply calibration.
