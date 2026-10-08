namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Serializable, versioned geological state for one geographically fixed regional grid.
/// Its elevations are the actual evolved bedrock, not a shader detail map.
/// The owning simulation always copies arrays when creating or restoring this snapshot.
/// </summary>
public sealed record PlanetRegionalGeologySnapshot(
    int SchemaVersion,
    int Seed,
    string RegionKey,
    int Width,
    int Height,
    double CellSpacingMeters,
    int Iteration,
    float[] ElevationMeters,
    double CumulativeErodedVolumeCubicMeters,
    double CumulativeDepositedVolumeCubicMeters,
    double CumulativeExportedVolumeCubicMeters);

/// <summary>
/// Pure, deterministic geological evolution. A step is one numerical erosion iteration,
/// NOT a calibrated duration in years. Pass the evolved state back to Advance to continue.
/// No per-vertex or per-frame erosion is performed by this class.
/// </summary>
public static class PlanetRegionalGeologyEvolution
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumIterationsPerCall = 64;

    public static PlanetRegionalGeologySnapshot Initialize(
        int seed, string regionKey, int width, int height, double cellSpacingMeters,
        IReadOnlyList<float> canonicalBedrockMeters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionKey);
        ArgumentNullException.ThrowIfNull(canonicalBedrockMeters);
        // Reuse watershed input validation instead of maintaining a subtly different grid contract.
        _ = PlanetRegionalWatershed.Build(width, height, cellSpacingMeters, canonicalBedrockMeters);
        return new PlanetRegionalGeologySnapshot(CurrentSchemaVersion, seed, regionKey, width, height,
            cellSpacingMeters, 0, canonicalBedrockMeters.ToArray(), 0.0, 0.0, 0.0);
    }

    /// <summary>Creates a defensive deep copy from a persisted snapshot, rejecting incompatible or malformed input.</summary>
    public static PlanetRegionalGeologySnapshot Restore(PlanetRegionalGeologySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.RegionKey);
        if (snapshot.SchemaVersion != CurrentSchemaVersion || snapshot.Iteration < 0)
        {
            throw new ArgumentException("Unsupported geology snapshot version or iteration.", nameof(snapshot));
        }

        if (!double.IsFinite(snapshot.CumulativeErodedVolumeCubicMeters) ||
            !double.IsFinite(snapshot.CumulativeDepositedVolumeCubicMeters) ||
            !double.IsFinite(snapshot.CumulativeExportedVolumeCubicMeters) ||
            snapshot.CumulativeErodedVolumeCubicMeters < 0.0 ||
            snapshot.CumulativeDepositedVolumeCubicMeters < 0.0 ||
            snapshot.CumulativeExportedVolumeCubicMeters < 0.0)
        {
            throw new ArgumentException("Snapshot sediment volumes must be finite and non-negative.", nameof(snapshot));
        }

        _ = PlanetRegionalWatershed.Build(snapshot.Width, snapshot.Height, snapshot.CellSpacingMeters, snapshot.ElevationMeters);
        var balance = snapshot.CumulativeErodedVolumeCubicMeters -
            snapshot.CumulativeDepositedVolumeCubicMeters - snapshot.CumulativeExportedVolumeCubicMeters;
        if (Math.Abs(balance) > Math.Max(1.0, snapshot.CumulativeErodedVolumeCubicMeters * 1e-5))
        {
            throw new ArgumentException("Snapshot violates cumulative sediment conservation.", nameof(snapshot));
        }

        return snapshot with { ElevationMeters = (float[])snapshot.ElevationMeters.Clone() };
    }

    public static PlanetRegionalGeologySnapshot Advance(
        PlanetRegionalGeologySnapshot snapshot, int iterations,
        IReadOnlyList<float>? rainfallCellWeights = null, CancellationToken cancellationToken = default)
    {
        if (iterations is < 1 or > MaximumIterationsPerCall)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "Specify 1–64 numerical iterations.");
        }

        var state = Restore(snapshot);
        if (state.Iteration > int.MaxValue - iterations)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), "Geological iteration count would overflow.");
        }

        for (var step = 0; step < iterations; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var watershed = PlanetRegionalWatershed.Build(state.Width, state.Height,
                state.CellSpacingMeters, state.ElevationMeters, rainfallCellWeights);
            state = state with
            {
                Iteration = state.Iteration + 1,
                ElevationMeters = (float[])watershed.EvolvedElevationMeters.Clone(),
                CumulativeErodedVolumeCubicMeters = state.CumulativeErodedVolumeCubicMeters + watershed.ErodedVolumeCubicMeters,
                CumulativeDepositedVolumeCubicMeters = state.CumulativeDepositedVolumeCubicMeters + watershed.DepositedVolumeCubicMeters,
                CumulativeExportedVolumeCubicMeters = state.CumulativeExportedVolumeCubicMeters + watershed.ExportedVolumeCubicMeters
            };
        }

        return state;
    }
}
