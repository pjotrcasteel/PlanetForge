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
            PlanetOriginAdaptiveIntegrator.Ticks(MinimumSubstep) > PlanetOriginAdaptiveIntegrator.Ticks(MaximumSubstep))
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

        var remaining = Ticks(duration);
        if (remaining == 0 || remaining < Ticks(options.MinimumSubstep))
        {
            throw new ArgumentOutOfRangeException(nameof(duration),
                "The requested elapsed physical interval must be at least the minimum substep.");
        }

        var minimum = Ticks(options.MinimumSubstep);
        var maximum = Ticks(options.MaximumSubstep);
        var current = initial;
        var accepted = 0;

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (accepted == options.MaximumAcceptedSubsteps)
            {
                throw new InvalidOperationException("Adaptive integration exceeded its maximum accepted substep count.");
            }

            var trialTicks = Math.Min(maximum, remaining);
            // Reserve a representable final interval instead of leaving a
            // fractional remainder shorter than the explicit minimum.
            if (remaining > trialTicks && remaining - trialTicks < minimum)
            {
                trialTicks = remaining - minimum;
            }

            var retries = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (trialTicks < minimum)
                {
                    throw new InvalidOperationException(
                        "Adaptive integration cannot advance without crossing its minimum physical timestep.");
                }

                var trialDuration = FromTicks(trialTicks);
                var proposal = propose(current, trialDuration) ??
                    throw new InvalidOperationException("The physical process must supply a non-null coupled energy step.");
                if (proposal.Duration != trialDuration)
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
                    remaining -= trialTicks;
                    accepted++;
                    break;
                }
                catch (InvalidOperationException) when (trialTicks > minimum && retries < options.MaximumRefinementsPerAttempt)
                {
                    // Only invalid physical candidates are retried. Incorrect
                    // units, durations or nonfinite inputs still fail loudly.
                    trialTicks = Math.Max(minimum, trialTicks / 2);
                    retries++;
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return current;
    }

    internal static long Ticks(PlanetOriginDuration duration)
    {
        if (duration.WholeYears > long.MaxValue / PlanetOriginDuration.MicrosecondsPerJulianYear)
        {
            throw new ArgumentOutOfRangeException(nameof(duration),
                "The chosen adaptive physical interval exceeds bounded integer microsecond resolution.");
        }

        var yearsTicks = duration.WholeYears * PlanetOriginDuration.MicrosecondsPerJulianYear;
        if (duration.RemainingMicroseconds > long.MaxValue - yearsTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(duration),
                "The combined geological years and fractional time exceed integer microsecond resolution.");
        }

        return yearsTicks + duration.RemainingMicroseconds;
    }

    private static PlanetOriginDuration FromTicks(long microseconds) =>
        new(microseconds / PlanetOriginDuration.MicrosecondsPerJulianYear,
            microseconds % PlanetOriginDuration.MicrosecondsPerJulianYear);

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
