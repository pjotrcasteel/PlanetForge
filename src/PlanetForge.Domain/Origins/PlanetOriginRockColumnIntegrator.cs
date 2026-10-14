namespace PlanetForge.Domain.Origins;

public readonly record struct PlanetOriginRockColumnIntegrationOptions(double MaximumStepSeconds, int MaximumSubsteps)
{
    /// <summary>Optional step-doubling bound in specific-enthalpy/cp units (Kelvin). Zero retains fixed-cap integration.</summary>
    public double MaximumThermalErrorKelvin { get; init; }

    public void Validate()
    {
        if (!double.IsFinite(MaximumStepSeconds) || MaximumStepSeconds < 0.000001 ||
            MaximumStepSeconds >= PlanetThermalEnergyBudget.JulianYearSeconds || MaximumSubsteps <= 0 ||
            !double.IsFinite(MaximumThermalErrorKelvin) || MaximumThermalErrorKelvin < 0.0)
            throw new ArgumentOutOfRangeException(nameof(MaximumStepSeconds));
    }
}

/// <summary>
/// Exact-time immutable depth-column integration. Admission is re-evaluated
/// from the current physical layer state; no intermediate world is published.
/// The caller controls the accuracy cap, not a desired artistic cooling time.
/// </summary>
public static class PlanetOriginRockColumnIntegrator
{
    public static PlanetOriginCanonicalRockCells Advance(
        PlanetOriginCanonicalRockCells initial, PlanetOriginDuration duration,
        PlanetOriginRegionalAirlessParameters boundary, PlanetOriginRockColumnIntegrationOptions options,
        CancellationToken cancellationToken = default)
    {
        var candidate = initial;
        foreach (var accepted in EnumerateAccepted(initial, duration, boundary, options, cancellationToken))
            if (accepted is not null) candidate = accepted;
        return candidate;
    }

    /// <summary>Same controller and accepted substeps as Advance, with cooperative scheduling of accepted/rejected work and no intermediate publication.</summary>
    public static async Task<PlanetOriginCanonicalRockCells> AdvanceAsync(
        PlanetOriginCanonicalRockCells initial, PlanetOriginDuration duration,
        PlanetOriginRegionalAirlessParameters boundary, PlanetOriginRockColumnIntegrationOptions options,
        CancellationToken cancellationToken = default)
    {
        var candidate = initial;
        var workCount = 0;
        foreach (var accepted in EnumerateAccepted(initial, duration, boundary, options, cancellationToken))
        {
            if (accepted is not null) candidate = accepted;
            if (++workCount % 4 == 0) await Task.Delay(1, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return candidate;
    }

    private static IEnumerable<PlanetOriginCanonicalRockCells?> EnumerateAccepted(
        PlanetOriginCanonicalRockCells initial, PlanetOriginDuration duration,
        PlanetOriginRegionalAirlessParameters boundary, PlanetOriginRockColumnIntegrationOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(initial);
        options.Validate();
        boundary.Validate();
        if (initial.Columns is null) throw new InvalidOperationException("Depth integration requires canonical columns.");
        if (duration.IsZero || duration.WholeYears != 0)
            throw new ArgumentOutOfRangeException(nameof(duration), "This bounded column reference accepts positive sub-year intervals.");
        var remaining = duration.RemainingMicroseconds;
        var candidate = initial;
        var adaptiveCap = options.MaximumStepSeconds;
        var trials = 0;
        if (options.MaximumThermalErrorKelvin > 0.0 && initial.MechanicalReference is not null)
            throw new InvalidOperationException("Thermal step-doubling does not yet measure mechanical mass/work error.");
        for (var count = 0; remaining > 0; count++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count >= options.MaximumSubsteps)
                throw new InvalidOperationException("Column integration exhausted its explicit substep budget; nothing was published.");
            var bound = candidate.Columns!.MaximumStableStepSeconds(boundary, candidate.Epoch.Thermal, cancellationToken);
            if (candidate.MechanicalReference is { } mechanics)
                bound = Math.Min(bound, candidate.Columns.MaximumViscousMeltStepSeconds(mechanics, cancellationToken));
            // A margin protects against floating-point admission rounding; flooring
            // cannot exceed the bound and the final remainder reaches exact time.
            var seconds = Math.Min(adaptiveCap, bound * 0.8);
            var ticks = Math.Min(remaining, checked((long)Math.Floor(seconds * 1_000_000.0)));
            if (ticks < 1) throw new InvalidOperationException("Required column timestep is below the supported microsecond resolution.");
            if (options.MaximumThermalErrorKelvin == 0.0)
            {
                var step = new PlanetOriginDuration(0, ticks);
                candidate = candidate.MechanicalReference is null ? candidate.Advance(step, boundary, cancellationToken) :
                    candidate.AdvanceWithViscousMeltFlow(step, boundary, cancellationToken);
                remaining -= ticks;
                yield return candidate;
                continue;
            }
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++trials > options.MaximumSubsteps)
                    throw new InvalidOperationException(
                        $"Thermal error control exhausted its trial budget (accepted={count}, remainingMicroseconds={remaining}); nothing was published.");
                // Reserve a two-tick final interval; a one-tick remainder cannot be step-doubled.
                if (remaining - ticks == 1 && ticks > 2) ticks--;
                if (ticks < 2) throw new InvalidOperationException(
                    $"Thermal error control requires two supported microsecond intervals (remaining={remaining}, trial={ticks}).");
                var trial = TryRefinedThermalStep(candidate, ticks, boundary, cancellationToken);
                if (trial is null) { ticks = ReduceTrialTicks(ticks); yield return null; continue; }
                var (refined, error) = trial.Value;
                if (error > options.MaximumThermalErrorKelvin) { ticks = ReduceTrialTicks(ticks); yield return null; continue; }
                candidate = refined;
                remaining -= ticks;
                adaptiveCap = Math.Min(options.MaximumStepSeconds, ticks / 1_000_000.0 *
                    (error < options.MaximumThermalErrorKelvin * 0.25 ? 2.0 : 1.0));
                break;
            }
            yield return candidate;
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    // Flooring an odd three-tick trial directly to one skipped the smallest
    // admissible two-tick estimate, rejecting otherwise supported trajectories.
    private static long ReduceTrialTicks(long ticks) => ticks > 2 ? Math.Max(2, ticks / 2) : 1;

    private static (PlanetOriginCanonicalRockCells Refined, double Error)? TryRefinedThermalStep(
        PlanetOriginCanonicalRockCells candidate, long ticks, PlanetOriginRegionalAirlessParameters boundary, CancellationToken cancellationToken)
    {
        try
        {
            var full = candidate.Advance(new(0, ticks), boundary, cancellationToken);
            var firstTicks = ticks / 2;
            var half = candidate.Advance(new(0, firstTicks), boundary, cancellationToken);
            var refined = half.Advance(new(0, ticks - firstTicks), boundary, cancellationToken);
            return (refined, ThermalErrorKelvin(full.Columns!, refined.Columns!));
        }
        catch (PlanetOriginRockColumnConvergenceException) { return null; }
    }

    private static double ThermalErrorKelvin(PlanetOriginRockColumnField coarse, PlanetOriginRockColumnField refined)
    {
        var maximum = 0.0;
        var cp = refined.Material.SpecificHeatCapacityJoulesPerKilogramKelvin;
        for (var index = 0; index < refined.Columns.Count; index++)
        {
            var first = coarse.Columns[index];
            var second = refined.Columns[index];
            for (var depth = 0; depth < second.Layers.Count; depth++)
            {
                var left = first.Layers[depth];
                var right = second.Layers[depth];
                var error = Math.Abs(left.StoredEnthalpyJoules / left.MassKilograms - right.StoredEnthalpyJoules / right.MassKilograms) / cp;
                if (!double.IsFinite(error)) throw new InvalidOperationException("Thermal error norm must be finite.");
                maximum = Math.Max(maximum, error);
            }
        }
        return maximum;
    }

}
