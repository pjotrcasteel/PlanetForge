using PlanetForge.Domain.Surface;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Chooses a useful physical mountain region for geological inspection.
/// A coarse global map cannot distinguish an exposed mountain catchment from
/// the single steep rim of a broad crater. Candidate regions are compared
/// using actual 128 km canonical rock samples, with the best-fit plane removed
/// so a simple uniform slope does not masquerade as rugged bedrock.
/// </summary>
public static class PlanetHeroSiteSelector
{
    public sealed record Candidate(PlanetVector Direction, double MountainBelt);
    public sealed record Selection(PlanetVector Center, double ReliefMeters, double NonPlanarReliefMeters);

    public static Selection Select(IPlanetElevationSource elevationSource, int seed,
        IReadOnlyList<Candidate> candidates, double spanMeters = 128_000.0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(elevationSource);
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0 || !double.IsFinite(spanMeters) || spanMeters <= 0.0 ||
            spanMeters >= PlanetHeroRegionBuilder.ReferencePlanetRadiusMeters * 0.1)
        {
            throw new ArgumentException("A physical candidate region and valid span are required.");
        }

        const int width = 9;
        var bestScore = double.NegativeInfinity;
        Selection? chosen = null;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var center = PlanetVector.Normalize(candidate.Direction);
            var frame = PlanetLocalFrame.Create(center, PlanetHeroRegionBuilder.ReferencePlanetRadiusMeters, 0.0);
            var samples = new double[width * width];
            var spacing = spanMeters / (width - 1);
            var min = double.PositiveInfinity;
            var max = double.NegativeInfinity;
            var mean = 0.0;
            var eastMoment = 0.0;
            var northMoment = 0.0;
            for (var y = 0; y < width; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var east = (x - 4) * spacing;
                    var north = (y - 4) * spacing;
                    var direction = PlanetVector.Normalize(center *
                        PlanetHeroRegionBuilder.ReferencePlanetRadiusMeters + frame.East * east + frame.North * north);
                    var sample = elevationSource.SampleElevationMeters(direction, seed);
                    if (!double.IsFinite(sample))
                    {
                        throw new InvalidOperationException("Canonical mountain sampling returned nonfinite rock.");
                    }

                    samples[y * width + x] = sample;
                    min = Math.Min(min, sample);
                    max = Math.Max(max, sample);
                    mean += sample;
                    eastMoment += (x - 4) * sample;
                    northMoment += (y - 4) * sample;
                }
            }

            mean /= samples.Length;
            // Sum_{x=-4..4} x² = 60; nine rows (or columns).
            var eastSlope = eastMoment / 540.0;
            var northSlope = northMoment / 540.0;
            var residualSum = 0.0;
            for (var y = 0; y < width; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var planarHeight = mean + (x - 4) * eastSlope + (y - 4) * northSlope;
                    var residual = samples[y * width + x] - planarHeight;
                    residualSum += residual * residual;
                }
            }

            var ruggedness = Math.Sqrt(residualSum / samples.Length);
            // Choose interesting mountain folds and real catchment texture,
            // not an isolated high pixel, flat plateau or a sloping plane.
            // The coarse belt field is only a modest geographical preference.
            var score = 2.0 * ruggedness + 0.20 * (max - min) +
                90.0 * Math.Clamp(candidate.MountainBelt, 0.0, 1.0) -
                0.10 * Math.Max(0.0, 500.0 - mean);
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            chosen = new Selection(center, max - min, ruggedness);
        }

        return chosen!;
    }
}
