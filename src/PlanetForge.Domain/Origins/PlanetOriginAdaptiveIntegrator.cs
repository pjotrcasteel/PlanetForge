namespace PlanetForge.Domain.Origins;

/// <summary>
/// Deterministic limits for one atomically published, caller-driven sequence
/// of physical substeps. The integrator provides numerical subdivision, not
/// precipitation kinetics, radiation laws or planetary initial conditions.
/// </summary>
public sealed record PlanetOriginAdaptiveIntegrationOptions(
    PlanetOriginDuration MaximumSubstep,
    PlanetOriginDuration MinimumSubstep,
    int MaximumAcceptedSubsteps = 1024,
    int MaximumRefinementsPerAttempt = 24,
    PlanetOriginCondensationSafetyConditions? SaturationSafety = null)
{
    internal void Validate(PlanetEnvironmentHeatCapacityParameters heatCapacities)
    {
        if (MinimumSubstep.IsZero || MaximumSubstep.IsZero ||
            PlanetOriginAdaptiveIntegrator.Compare(MinimumSubstep, MaximumSubstep) > 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumSubstep),
                "Positive integration limits require minimum duration to be no greater than the maximum.");
        }

        if (MaximumAcceptedSubsteps <= 0 || MaximumRefinementsPerAttempt < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumAcceptedSubsteps),
                "Integration requires positive publication capacity and nonnegative refinement capacity.");
        }

        SaturationSafety?.Validate(heatCapacities);
    }
}

/// <summary>
/// Re-evaluates an external, deterministic physics proposal against each
/// accepted immutable candidate ledger and physical interval. Invalid trial
/// energy or threshold crossings trigger bounded halving; failures never
/// publish the preceding successful candidates. No physical rate law is
/// invented here and no automatic liquid water is produced.
/// </summary>
internal static class PlanetOriginAdaptiveIntegrator
{
    public static PlanetOriginEnergyLedger Integrate(
        PlanetOriginEnergyLedger initial,
        PlanetOriginDuration duration,
        PlanetOriginAdaptiveIntegrationOptions options,
        Func<PlanetOriginEnergyLedger, PlanetOriginDuration, PlanetOriginEnergyStep> propose,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(propose);
        cancellationToken.ThrowIfCancellationRequested();
        options.Validate(initial.HeatCapacities);

        var remaining = duration;
        var minimum = options.MinimumSubstep;
        var maximum = options.MaximumSubstep;
        if (duration.IsZero || Compare(remaining, minimum) < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(duration),
                "The requested physical interval must be at least the minimum substep.");
        }

        var current = initial;
        var accepted = 0;

        while (!remaining.IsZero)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (accepted == options.MaximumAcceptedSubsteps)
            {
                throw new InvalidOperationException("Adaptive integration exceeded its maximum accepted substep count.");
            }

            var trial = Min(maximum, remaining);
            // Exact remaining years + sub-year microseconds, with no overall
            // long-microsecond conversion or geological-range overflow.
            // Preserve a representable final interval instead of creating
            // an inadmissibly short remainder.
            if (Compare(remaining, trial) > 0 && Compare(Subtract(remaining, trial), minimum) < 0)
            {
                trial = Subtract(remaining, minimum);
            }

            var retries = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Compare(trial, minimum) < 0)
                {
                    throw new InvalidOperationException(
                        "Adaptive integration cannot advance without crossing its minimum physical timestep.");
                }

                var proposal = propose(current, trial) ??
                    throw new InvalidOperationException("The physical process must supply a non-null coupled energy step.");
                if (proposal.Duration != trial)
                {
                    throw new ArgumentException(
                        "A physical process must propose rates for the exact timestep requested by the integrator.",
                        nameof(propose));
                }

                if (options.SaturationSafety is not null)
                {
                    // Never allow the caller to bypass the same idealized
                    // condensation checks while adaptive refinement is on.
                    proposal = proposal with { CondensationSafety = options.SaturationSafety };
                }

                try
                {
                    var candidate = current.Advance(proposal, cancellationToken);
                    if (options.SaturationSafety is not null)
                    {
                        VerifyNoSaturationBoundaryCrossing(current, candidate, options.SaturationSafety);
                    }

                    current = candidate;
                    remaining = Subtract(remaining, trial);
                    accepted++;
                    break;
                }
                catch (InvalidOperationException) when (Compare(trial, minimum) > 0 && retries < options.MaximumRefinementsPerAttempt)
                {
                    // Only invalid physical candidates are retried. Incorrect
                    // units, durations or nonfinite inputs still fail loudly.
                    trial = Max(minimum, Half(trial));
                    retries++;
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return current;
    }

    /// <summary>
    /// Exact chronological comparisons avoid overflowing a signed 64-bit
    /// microsecond count when integrating more than ~292 Julian years.
    /// </summary>
    internal static int Compare(PlanetOriginDuration left, PlanetOriginDuration right)
    {
        var years = left.WholeYears.CompareTo(right.WholeYears);
        return years == 0 ? left.RemainingMicroseconds.CompareTo(right.RemainingMicroseconds) : years;
    }

    private static PlanetOriginDuration Min(PlanetOriginDuration left, PlanetOriginDuration right) =>
        Compare(left, right) <= 0 ? left : right;

    private static PlanetOriginDuration Max(PlanetOriginDuration left, PlanetOriginDuration right) =>
        Compare(left, right) >= 0 ? left : right;

    private static PlanetOriginDuration Subtract(PlanetOriginDuration left, PlanetOriginDuration right)
    {
        if (Compare(left, right) < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(right), "Cannot subtract more physical time than remains.");
        }

        var years = left.WholeYears - right.WholeYears;
        var microseconds = left.RemainingMicroseconds - right.RemainingMicroseconds;
        if (microseconds < 0)
        {
            years--;
            microseconds += PlanetOriginDuration.MicrosecondsPerJulianYear;
        }

        return new PlanetOriginDuration(years, microseconds);
    }

    private static PlanetOriginDuration Half(PlanetOriginDuration duration)
    {
        // One odd whole year becomes one year's worth of microseconds in
        // the fractional part BEFORE halving, preserving microsecond
        // precision without converting the entire duration to ticks.
        var years = duration.WholeYears / 2;
        var remainder = ((duration.WholeYears % 2) * PlanetOriginDuration.MicrosecondsPerJulianYear +
            duration.RemainingMicroseconds) / 2;
        return new PlanetOriginDuration(years, remainder);
    }

    private static void VerifyNoSaturationBoundaryCrossing(
        PlanetOriginEnergyLedger before,
        PlanetOriginEnergyLedger after,
        PlanetOriginCondensationSafetyConditions conditions)
    {
        var startMass = before.Environment.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor);
        var endMass = after.Environment.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor);
        if (startMass == 0.0 && endMass == 0.0)
        {
            return;
        }

        var start = Evaluate(before, conditions);
        var finish = Evaluate(after, conditions);
        var startDifference = start.WaterVaporPartialPressurePascals - start.WaterSaturationPressurePascals!.Value;
        var endDifference = finish.WaterVaporPartialPressurePascals - finish.WaterSaturationPressurePascals!.Value;
        var startTolerance = start.WaterSaturationPressurePascals.Value * 1e-10;
        var endTolerance = finish.WaterSaturationPressurePascals.Value * 1e-10;
        if ((startDifference < -startTolerance && endDifference > endTolerance) ||
            (startDifference > startTolerance && endDifference < -endTolerance))
        {
            throw new InvalidOperationException(
                "The physical step crossed the atmospheric saturation boundary without a resolved phase equilibrium.");
        }
    }

    private static PlanetMixedAtmosphereSaturationSnapshot Evaluate(
        PlanetOriginEnergyLedger energy,
        PlanetOriginCondensationSafetyConditions conditions)
    {
        conditions.Validate(energy.HeatCapacities);
        var temperature = energy.Temperatures.AtmosphereTemperatureKelvin ??
            throw new InvalidOperationException("Saturation-aware physical integration requires atmospheric matter.");
        var snapshot = PlanetMixedAtmosphereSaturation.Evaluate(
            energy.Environment.Water, conditions.Atmosphere, temperature);

        if (snapshot.Regime != PlanetLiquidVaporSaturationRegime.LiquidVaporIapws ||
            snapshot.TotalSurfacePressurePascals > conditions.MaximumValidatedSurfacePressurePascals)
        {
            throw new InvalidOperationException(
                "Adaptive liquid-vapour physics exceeds the supported temperature or pressure envelope.");
        }

        return snapshot;
    }
}
