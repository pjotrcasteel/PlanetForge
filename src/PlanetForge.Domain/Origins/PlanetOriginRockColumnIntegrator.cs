namespace PlanetForge.Domain.Origins;

public readonly record struct PlanetOriginRockColumnIntegrationOptions(double MaximumStepSeconds, int MaximumSubsteps)
{
    public void Validate()
    {
        if (!double.IsFinite(MaximumStepSeconds) || MaximumStepSeconds < 0.000001 ||
            MaximumStepSeconds >= PlanetThermalEnergyBudget.JulianYearSeconds || MaximumSubsteps <= 0)
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
        ArgumentNullException.ThrowIfNull(initial);
        options.Validate();
        boundary.Validate();
        if (initial.Columns is null) throw new InvalidOperationException("Depth integration requires canonical columns.");
        if (duration.IsZero || duration.WholeYears != 0)
            throw new ArgumentOutOfRangeException(nameof(duration), "This bounded column reference accepts positive sub-year intervals.");
        var remaining = duration.RemainingMicroseconds;
        var candidate = initial;
        for (var count = 0; remaining > 0; count++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count >= options.MaximumSubsteps)
                throw new InvalidOperationException("Column integration exhausted its explicit substep budget; nothing was published.");
            var bound = candidate.Columns!.MaximumStableStepSeconds(boundary, candidate.Epoch.Thermal, cancellationToken);
            // A margin protects against floating-point admission rounding; flooring
            // cannot exceed the bound and the final remainder reaches exact time.
            var seconds = Math.Min(options.MaximumStepSeconds, bound * 0.8);
            var ticks = Math.Min(remaining, checked((long)Math.Floor(seconds * 1_000_000.0)));
            if (ticks < 1) throw new InvalidOperationException("Required column timestep is below the supported microsecond resolution.");
            candidate = candidate.Advance(new PlanetOriginDuration(0, ticks), boundary, cancellationToken);
            remaining -= ticks;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return candidate;
    }
}
