namespace PlanetForge.Domain.Hydrology;

/// <summary>
/// One elevation sample with its represented physical surface area.
/// Areas may differ because a cubed-sphere grid is not equal-area.
/// </summary>
public readonly record struct PlanetElevationAreaSample(double ElevationMeters, double AreaSquareMeters);

/// <summary>
/// Immutable terrain-volume curve. This mathematical foundation is deliberately
/// independent of a renderer, map projection, game era, or water-phase calculator.
/// Given liquid-water volume, it finds the datum that fills the sampled terrain.
/// </summary>
public sealed class PlanetHypsometry
{
    private readonly double[] elevations;
    private readonly double[] cumulativeAreas;
    private readonly double[] cumulativeAreaElevations;

    public PlanetHypsometry(IReadOnlyList<PlanetElevationAreaSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            throw new ArgumentException("A hypsometry requires at least one elevation sample.", nameof(samples));
        }

        var ordered = samples
            .OrderBy(sample => sample.ElevationMeters)
            .ToArray();
        elevations = new double[ordered.Length];
        cumulativeAreas = new double[ordered.Length + 1];
        cumulativeAreaElevations = new double[ordered.Length + 1];

        for (var index = 0; index < ordered.Length; index++)
        {
            var sample = ordered[index];
            if (!double.IsFinite(sample.ElevationMeters) || !double.IsFinite(sample.AreaSquareMeters) || sample.AreaSquareMeters <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(samples), "Elevation and strictly positive surface area must be finite.");
            }

            elevations[index] = sample.ElevationMeters;
            cumulativeAreas[index + 1] = cumulativeAreas[index] + sample.AreaSquareMeters;
            cumulativeAreaElevations[index + 1] = cumulativeAreaElevations[index] + sample.AreaSquareMeters * sample.ElevationMeters;

            if (!double.IsFinite(cumulativeAreas[index + 1]) || !double.IsFinite(cumulativeAreaElevations[index + 1]))
            {
                throw new ArgumentOutOfRangeException(nameof(samples), "Accumulated terrain areas must remain finite.");
            }
        }

        TotalAreaSquareMeters = cumulativeAreas[^1];
    }

    public double TotalAreaSquareMeters { get; }

    public double MinimumElevationMeters => elevations[0];

    public double MaximumElevationMeters => elevations[^1];

    public double GetSubmergedVolumeCubicMeters(double seaLevelMeters)
    {
        if (!double.IsFinite(seaLevelMeters))
        {
            throw new ArgumentOutOfRangeException(nameof(seaLevelMeters), "Sea level must be finite.");
        }

        var floodedCount = CountBelow(seaLevelMeters);
        var volume = seaLevelMeters * cumulativeAreas[floodedCount] - cumulativeAreaElevations[floodedCount];
        return Math.Max(volume, 0.0);
    }

    public double GetFloodedAreaFraction(double seaLevelMeters)
    {
        if (!double.IsFinite(seaLevelMeters))
        {
            throw new ArgumentOutOfRangeException(nameof(seaLevelMeters), "Sea level must be finite.");
        }

        return cumulativeAreas[CountBelow(seaLevelMeters)] / TotalAreaSquareMeters;
    }

    public double SolveSeaLevelMeters(double liquidVolumeCubicMeters)
    {
        if (!double.IsFinite(liquidVolumeCubicMeters) || liquidVolumeCubicMeters < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(liquidVolumeCubicMeters), "Liquid volume must be finite and nonnegative.");
        }

        // No liquid is not the same as a full ocean basin at its minimum elevation.
        if (liquidVolumeCubicMeters == 0.0)
        {
            return double.BitDecrement(MinimumElevationMeters);
        }

        var lower = MinimumElevationMeters;
        var upper = MaximumElevationMeters + liquidVolumeCubicMeters / TotalAreaSquareMeters;
        if (!double.IsFinite(upper))
        {
            throw new ArgumentOutOfRangeException(nameof(liquidVolumeCubicMeters), "Resulting sea level exceeds the finite physical range.");
        }

        // Flooded volume is monotone in sea level. Each probe is O(log N)
        // over precomputed area/elevation prefixes rather than re-sampling rock.
        for (var iteration = 0; iteration < 72; iteration++)
        {
            var midpoint = lower + (upper - lower) * 0.5;
            if (GetSubmergedVolumeCubicMeters(midpoint) < liquidVolumeCubicMeters)
            {
                lower = midpoint;
            }
            else
            {
                upper = midpoint;
            }
        }

        return lower + (upper - lower) * 0.5;
    }

    private int CountBelow(double seaLevelMeters)
    {
        var lower = 0;
        var upper = elevations.Length;
        while (lower < upper)
        {
            var midpoint = lower + (upper - lower) / 2;
            if (elevations[midpoint] < seaLevelMeters)
            {
                lower = midpoint + 1;
            }
            else
            {
                upper = midpoint;
            }
        }

        return lower;
    }
}
