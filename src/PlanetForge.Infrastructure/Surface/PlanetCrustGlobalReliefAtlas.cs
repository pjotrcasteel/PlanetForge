using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Immutable, indexed geographic atlas of pressure-balanced crust displacement
/// from an explicitly selected forward geological snapshot. The atlas carries
/// literal displacement in metres, not a shader colour, height multiplier or
/// independent continent generator. Shared spherical world coordinates make
/// every globe tile and LOD sample agree on the SAME physical displacement.
///
/// The input is an experimental 1,024-material reference-quadrature model.
/// A compact-kernel interpolant is not conservative moving crust geometry.
/// </summary>
public sealed class PlanetCrustGlobalReliefAtlas
{
    public const int LongitudeSamples = 256;
    public const int LatitudeSamples = 129;
    public const double SupportRadiusRadians = 0.16;

    private const double BackgroundWeight = 0.35;
    private static readonly double MinimumDot = Math.Cos(SupportRadiusRadians);
    private readonly double[] displacement;

    public PlanetCrustGlobalReliefAtlas(PlanetCrustEvolvingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Columns.Count != PlanetCrustParcelCatalog.MaterialSampleCount ||
            snapshot.Columns.Any(column =>
                !double.IsFinite(column.SurfaceDisplacementMeters) ||
                !double.IsFinite(column.GeographicDirection.Length) ||
                Math.Abs(column.GeographicDirection.Length - 1.0) > 1e-8))
        {
            throw new ArgumentException("A complete normalized geological material snapshot is required.", nameof(snapshot));
        }

        World = snapshot.World;
        Epoch = snapshot.Epoch;
        var directions = snapshot.Columns.Select(column => column.GeographicDirection).ToArray();
        var heights = snapshot.Columns.Select(column => column.SurfaceDisplacementMeters).ToArray();
        displacement = new double[LongitudeSamples * LatitudeSamples];

        for (var latitude = 0; latitude < LatitudeSamples; latitude++)
        {
            var radians = Math.PI * (latitude / (double)(LatitudeSamples - 1) - 0.5);
            var parallel = Math.Cos(radians);
            var north = Math.Sin(radians);
            if (latitude is 0 or LatitudeSamples - 1)
            {
                // A pole is one point on the globe, irrespective of longitude.
                var pole = new PlanetVector(0, north, 0);
                var height = InterpolateResearchColumns(pole, directions, heights);
                Array.Fill(displacement, height, latitude * LongitudeSamples, LongitudeSamples);
                continue;
            }

            for (var longitude = 0; longitude < LongitudeSamples; longitude++)
            {
                var angle = Math.Tau * longitude / LongitudeSamples;
                var direction = new PlanetVector(parallel * Math.Cos(angle), north, parallel * Math.Sin(angle));
                displacement[latitude * LongitudeSamples + longitude] =
                    InterpolateResearchColumns(direction, directions, heights);
            }
        }

        MaximumAbsoluteDisplacementMeters = displacement.Max(Math.Abs);
    }

    public PlanetWorldIdentity World { get; }
    public PlanetGeologicalEpoch Epoch { get; }
    public double MaximumAbsoluteDisplacementMeters { get; }

    /// <summary>
    /// Constant-time geographic sampling after atlas construction. Longitude
    /// wraps, latitude clamps, and both poles are longitude-independent.
    /// No per-tile origin, LOD or screen-size variable enters this calculation.
    /// </summary>
    public double SampleDisplacementMeters(PlanetVector geographicDirection)
    {
        var p = PlanetVector.Normalize(geographicDirection);
        var longitude = Math.Atan2(p.Z, p.X);
        if (longitude < 0.0)
        {
            longitude += Math.Tau;
        }

        var x = longitude * LongitudeSamples / Math.Tau;
        var x0 = Math.Min((int)Math.Floor(x), LongitudeSamples - 1);
        var x1 = (x0 + 1) % LongitudeSamples;
        var tx = x - x0;

        var latitude = Math.Asin(Math.Clamp(p.Y, -1.0, 1.0));
        var y = Math.Clamp((latitude / Math.PI + 0.5) * (LatitudeSamples - 1), 0.0, LatitudeSamples - 1);
        var y0 = Math.Min((int)Math.Floor(y), LatitudeSamples - 1);
        var y1 = Math.Min(y0 + 1, LatitudeSamples - 1);
        var ty = y - y0;

        var a = Lerp(displacement[y0 * LongitudeSamples + x0], displacement[y0 * LongitudeSamples + x1], tx);
        var b = Lerp(displacement[y1 * LongitudeSamples + x0], displacement[y1 * LongitudeSamples + x1], tx);
        return Lerp(a, b, ty);
    }

    private static double InterpolateResearchColumns(
        PlanetVector position,
        IReadOnlyList<PlanetVector> directions,
        IReadOnlyList<double> heights)
    {
        var sum = 0.0;
        var weight = BackgroundWeight;
        for (var i = 0; i < directions.Count; i++)
        {
            var dot = PlanetVector.Dot(position, directions[i]);
            if (dot <= MinimumDot)
            {
                continue;
            }

            var t = Math.Clamp((dot - MinimumDot) / (1.0 - MinimumDot), 0.0, 1.0);
            var influence = t * t * (3.0 - 2.0 * t);
            sum += influence * heights[i];
            weight += influence;
        }

        return sum / weight;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}

/// <summary>
/// Changes actual sampled bedrock for just the matching generated planet.
/// Every call flows through the same shared elevation source as orbital and
/// local terrain. This is still a research snapshot, not completed geology.
/// </summary>
public sealed class PlanetCrustGlobalReliefElevationSource(
    IPlanetElevationSource canonical,
    PlanetCrustGlobalReliefAtlas relief) : IPlanetElevationSource
{
    private readonly IPlanetElevationSource canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
    private readonly PlanetCrustGlobalReliefAtlas relief = relief ?? throw new ArgumentNullException(nameof(relief));

    public double SampleElevationMeters(PlanetVector direction, int seed)
    {
        var baseline = canonical.SampleElevationMeters(direction, seed);
        return PlanetWorldIdentity.CreateCurrent(seed) == relief.World
            ? baseline + relief.SampleDisplacementMeters(direction)
            : baseline;
    }
}
