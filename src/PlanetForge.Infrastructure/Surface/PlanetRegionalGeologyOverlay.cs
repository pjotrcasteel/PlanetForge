using PlanetForge.Domain.Surface;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Read-only, geographically anchored sample of a previously evolved regional height grid.
/// Converts unit sphere directions to the same local tangent plane used when the region was
/// sampled, then bilinearly interpolates the physical evolved-minus-original elevation.
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
        var top = Lerp(elevationDeltaMeters[y0 * width + x0], elevationDeltaMeters[y0 * width + x0 + 1], tx);
        var bottom = Lerp(elevationDeltaMeters[(y0 + 1) * width + x0], elevationDeltaMeters[(y0 + 1) * width + x0 + 1], tx);

        // Isolated research regions have no neighbor solution yet. Explicitly fade them
        // to canonical bedrock at the border, preventing hard tile edges while retaining
        // the original heights in areas we cannot yet guarantee cross-tile consistency.
        var marginX = Math.Min(x, width - 1.0 - x);
        var marginY = Math.Min(y, height - 1.0 - y);
        var fade = SmoothStep(marginX / BoundaryFadeCells) * SmoothStep(marginY / BoundaryFadeCells);
        return (Lerp(top, bottom, ty), fade);
    }

    private static double Lerp(double left, double right, double weight) => left + ((right - left) * weight);

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
