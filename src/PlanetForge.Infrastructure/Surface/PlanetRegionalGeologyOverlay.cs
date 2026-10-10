using PlanetForge.Domain.Surface;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Read-only, geographically anchored sample of a previously evolved regional height grid.
/// Converts unit sphere directions to the same local tangent plane used when the region was
/// sampled, then interpolates physical evolved-minus-original elevation with shape-preserving C1 cubics.
/// No hydrology or erosion computation occurs in SampleDeltaMeters.
/// </summary>
public sealed class PlanetRegionalGeologyOverlay
{
    private const double BoundaryFadeCells = 3.0;
    private readonly PlanetVector center;
    private readonly PlanetVector east;
    private readonly PlanetVector north;
    private readonly double planetRadiusMeters;
    private readonly double cellSpacingMeters;
    private readonly int width;
    private readonly int height;
    private readonly float[] elevationDeltaMeters;

    private PlanetRegionalGeologyOverlay(
        int seed, string regionKey, PlanetVector center, PlanetVector east, PlanetVector north,
        double planetRadiusMeters, int width, int height, double cellSpacingMeters, float[] deltaMeters)
    {
        Seed = seed;
        RegionKey = regionKey;
        this.center = center;
        this.east = east;
        this.north = north;
        this.planetRadiusMeters = planetRadiusMeters;
        this.cellSpacingMeters = cellSpacingMeters;
        this.width = width;
        this.height = height;
        elevationDeltaMeters = deltaMeters;
    }

    // A conservative spherical cap encloses the entire gnomonic rectangle.
    // Only for spatial prefiltering; the true rectangular support and fade
    // are still determined by SampleWeightedDeltaMeters.
    internal PlanetVector CenterDirection => center;
    internal double SupportChordRadius
    {
        get
        {
            var halfX = ((width - 1) * 0.5) * cellSpacingMeters / planetRadiusMeters;
            var halfY = ((height - 1) * 0.5) * cellSpacingMeters / planetRadiusMeters;
            var radial = Math.Sqrt(1.0 + halfX * halfX + halfY * halfY);
            return Math.Sqrt(2.0 * (1.0 - 1.0 / radial));
        }
    }

    public int Seed { get; }
    public string RegionKey { get; }
    public int Width => width;
    public int Height => height;
    public double CellSpacingMeters => cellSpacingMeters;

    public static PlanetRegionalGeologyOverlay Create(
        PlanetRegionalGeologySnapshot original, PlanetRegionalGeologySnapshot evolved,
        PlanetVector regionCenterDirection, double planetRadiusMeters)
    {
        var before = PlanetRegionalGeologyEvolution.Restore(original);
        var after = PlanetRegionalGeologyEvolution.Restore(evolved);
        if (before.Seed != after.Seed || before.RegionKey != after.RegionKey ||
            before.Width != after.Width || before.Height != after.Height ||
            before.CellSpacingMeters != after.CellSpacingMeters || after.Iteration < before.Iteration)
        {
            throw new ArgumentException("The regional snapshots must have the same seed, geographical identity, resolution and chronological order.");
        }

        if (!double.IsFinite(planetRadiusMeters) || planetRadiusMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(planetRadiusMeters));
        }

        var normalizedCenter = PlanetVector.Normalize(regionCenterDirection);
        var frame = PlanetLocalFrame.Create(normalizedCenter, planetRadiusMeters, 0);
        var deltas = new float[before.ElevationMeters.Length];
        for (var i = 0; i < deltas.Length; i++)
        {
            deltas[i] = after.ElevationMeters[i] - before.ElevationMeters[i];
        }

        return new PlanetRegionalGeologyOverlay(before.Seed, before.RegionKey, normalizedCenter,
            frame.East, frame.North, planetRadiusMeters, before.Width, before.Height,
            before.CellSpacingMeters, deltas);
    }

    public double SampleDeltaMeters(PlanetVector surfaceDirection, int seed)
    {
        var sample = SampleWeightedDeltaMeters(surfaceDirection, seed);
        return sample.DeltaMeters * sample.Weight;
    }

    /// <summary>
    /// Returns the unattenuated physical height delta and a smooth support weight.
    /// A geological atlas can blend overlapping peers before applying boundary fade;
    /// isolated regions retain the old zero-at-the-edge behavior.
    /// </summary>
    public (double DeltaMeters, double Weight) SampleWeightedDeltaMeters(PlanetVector surfaceDirection, int seed)
    {
        if (seed != Seed)
        {
            return (0.0, 0.0);
        }

        var direction = PlanetVector.Normalize(surfaceDirection);
        var facing = PlanetVector.Dot(direction, center);
        if (facing <= 0.0)
        {
            return (0.0, 0.0);
        }

        // Gnomonic projection exactly inverts the projection used to sample Terrain Lab:
        // Normalize(center * R + east * x + north * y).
        var tangent = (direction * (planetRadiusMeters / facing)) - (center * planetRadiusMeters);
        var x = PlanetVector.Dot(tangent, east) / cellSpacingMeters + ((width - 1) * 0.5);
        var y = PlanetVector.Dot(tangent, north) / cellSpacingMeters + ((height - 1) * 0.5);
        if (x <= 0.0 || y <= 0.0 || x >= width - 1.0 || y >= height - 1.0)
        {
            return (0.0, 0.0);
        }

        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var tx = x - x0;
        var ty = y - y0;
        // Bilinear delta reconstruction created four-sided planes and abrupt
        // changes of slope at every inherited 1 km / 125 m grid line. The
        // 8 km physical solve inherited these facets before routing could
        // reshape them. A monotone cubic preserves every parent sample while
        // joining the pieces with continuous tangents and no invented extrema.
        Span<double> rows = stackalloc double[4];
        for (var row = -1; row <= 2; row++)
        {
            var sampleY = Math.Clamp(y0 + row, 0, height - 1);
            var offset = sampleY * width;
            rows[row + 1] = InterpolateMonotone(
                elevationDeltaMeters[offset + Math.Clamp(x0 - 1, 0, width - 1)],
                elevationDeltaMeters[offset + x0],
                elevationDeltaMeters[offset + x0 + 1],
                elevationDeltaMeters[offset + Math.Clamp(x0 + 2, 0, width - 1)], tx);
        }

        var inheritedDelta = InterpolateMonotone(rows[0], rows[1], rows[2], rows[3], ty);

        // Isolated research regions have no neighbor solution yet. Explicitly fade them
        // to canonical bedrock at the border, preventing hard tile edges while retaining
        // the original heights in areas we cannot yet guarantee cross-tile consistency.
        var marginX = Math.Min(x, width - 1.0 - x);
        var marginY = Math.Min(y, height - 1.0 - y);
        var fade = SmoothStep(marginX / BoundaryFadeCells) * SmoothStep(marginY / BoundaryFadeCells);
        return (inheritedDelta, fade);
    }

    /// <summary>
    /// Shape-preserving cubic Hermite interpolation over one physical grid
    /// interval. Harmonic mean tangents do not overshoot a monotone channel
    /// cut or invent an isolated rock crest, and shared nodal tangents remain
    /// identical between neighboring cells (C1 except at true extrema).
    /// </summary>
    private static double InterpolateMonotone(double before, double start, double end, double after, double fraction)
    {
        var previousSlope = start - before;
        var intervalSlope = end - start;
        var nextSlope = after - end;
        var leftTangent = HarmonicTangent(previousSlope, intervalSlope);
        var rightTangent = HarmonicTangent(intervalSlope, nextSlope);
        var squared = fraction * fraction;
        var cubed = squared * fraction;
        return (2.0 * cubed - 3.0 * squared + 1.0) * start +
            (cubed - 2.0 * squared + fraction) * leftTangent +
            (-2.0 * cubed + 3.0 * squared) * end +
            (cubed - squared) * rightTangent;
    }

    private static double HarmonicTangent(double first, double second) =>
        first * second <= 0.0 ? 0.0 : 2.0 * first * second / (first + second);

    private static double SmoothStep(double value)
    {
        var t = Math.Clamp(value, 0.0, 1.0);
        return t * t * (3.0 - (2.0 * t));
    }
}

/// <summary>
/// Optional view of already-evolved heights for both cube-sphere and local mesh samplers.
/// Not registered as the default game terrain source until global addressing and neighboring
/// regional sediment exchange are implemented.
/// </summary>
public sealed class PlanetRegionalEvolvedElevationSource(
    IPlanetElevationSource canonicalElevation, PlanetRegionalGeologyOverlay region) : IPlanetElevationSource
{
    public double SampleElevationMeters(PlanetVector direction, int seed) =>
        canonicalElevation.SampleElevationMeters(direction, seed) + region.SampleDeltaMeters(direction, seed);
}
