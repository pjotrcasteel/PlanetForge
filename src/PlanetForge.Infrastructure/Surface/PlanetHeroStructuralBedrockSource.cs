using PlanetForge.Domain.Surface;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Experimental physical bedrock substrate for the on-demand hero region. Rock ridges
/// follow the local macro-uplift strike, with warped long-fault fabric, secondary beds,
/// and shorter outcrops. This is height geometry fed into hydraulic erosion, not color
/// or normals painted in the renderer. It is not used by the gameplay elevation source.
/// </summary>
public sealed class PlanetHeroStructuralBedrockSource : IPlanetElevationSource
{
    private const double PlanetRadiusMeters = 6_371_000.0;
    private readonly IPlanetElevationSource source;
    private readonly PlanetVector origin;
    private readonly PlanetVector east;
    private readonly PlanetVector north;
    private readonly double acrossX;
    private readonly double acrossY;
    private readonly int originalSeed;

    public PlanetHeroStructuralBedrockSource(IPlanetElevationSource source, PlanetVector regionAnchor, int seed)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        origin = PlanetVector.Normalize(regionAnchor);
        var frame = PlanetLocalFrame.Create(origin, PlanetRadiusMeters, 0.0);
        east = frame.East;
        north = frame.North;
        var step = 5_000.0;
        var eastGradient = HeightAt(step, 0.0, seed) - HeightAt(-step, 0.0, seed);
        var northGradient = HeightAt(0.0, step, seed) - HeightAt(0.0, -step, seed);
        var magnitude = Math.Sqrt(eastGradient * eastGradient + northGradient * northGradient);
        var angle = magnitude > 1e-5
            ? Math.Atan2(northGradient, eastGradient)
            : (seed & 65535) * (2.0 * Math.PI / 65536.0);
        acrossX = Math.Cos(angle);
        acrossY = Math.Sin(angle);
        originalSeed = seed;
    }

    public double SampleElevationMeters(PlanetVector direction, int seed)
    {
        var normalized = PlanetVector.Normalize(direction);
        var canonical = source.SampleElevationMeters(normalized, seed);
        if (seed != originalSeed)
        {
            return canonical;
        }

        var position = (normalized * PlanetRadiusMeters) - (origin * PlanetRadiusMeters);
        var e = PlanetVector.Dot(position, east);
        var n = PlanetVector.Dot(position, north);
        // Compression is across the mountain chain; bedding is elongated along strike.
        var across = (e * acrossX) + (n * acrossY);
        var along = (-e * acrossY) + (n * acrossX);

        // Two smooth domain warps bend regional bedding without concentric rings or
        // periodic sine bands. All coordinates are in real metres, stable across LODs.
        var warpA = (Noise(across / 19_300.0, along / 15_700.0, seed ^ 0x2A3C) - 0.5) * 2_800.0;
        var warpB = (Noise(across / 13_700.0, along / 23_100.0, seed ^ 0x7C91) - 0.5) * 1_900.0;
        var foldedAcross = across + warpA;
        var foldedAlong = along + warpB;

        var mainBed = Ridge(Noise(foldedAcross / 4_300.0, foldedAlong / 13_700.0, seed ^ 0x4C17));
        var secondaryBed = Ridge(Noise(foldedAcross / 1_600.0, foldedAlong / 5_700.0, seed ^ 0x1E3B));
        var exposedOutcrop = Ridge(Noise(foldedAcross / 570.0, foldedAlong / 2_300.0, seed ^ 0x6B29));

        // Signed contributions preserve the highland's elevation identity; only the
        // geological morphology is added. Amplitude is conditional on rocky exposure.
        var exposure = 0.45 + 0.55 * SmoothStep((canonical + 1_400.0) / 3_600.0);
        return canonical + exposure * (310.0 * (mainBed - 0.5) +
            105.0 * (secondaryBed - 0.5) + 24.0 * (exposedOutcrop - 0.5));
    }

    private double HeightAt(double eastMeters, double northMeters, int seed)
    {
        var direction = PlanetVector.Normalize((origin * PlanetRadiusMeters) +
            (east * eastMeters) + (north * northMeters));
        return source.SampleElevationMeters(direction, seed);
    }

    private static double Ridge(double noise) => 1.0 - Math.Abs(2.0 * noise - 1.0);

    private static double SmoothStep(double value)
    {
        var t = Math.Clamp(value, 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    private static double Noise(double x, double y, int seed)
    {
        var ix = (int)Math.Floor(x);
        var iy = (int)Math.Floor(y);
        var fx = SmoothStep(x - ix);
        var fy = SmoothStep(y - iy);
        var left = Mix(Hash(ix, iy, seed), Hash(ix, iy + 1, seed), fy);
        var right = Mix(Hash(ix + 1, iy, seed), Hash(ix + 1, iy + 1, seed), fy);
        return Mix(left, right, fx);
    }

    private static double Mix(double a, double b, double fraction) => a + (b - a) * fraction;

    private static double Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)seed ^ ((uint)x * 0x9E3779B9u) ^ ((uint)y * 0x85EBCA6Bu);
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return h / (double)uint.MaxValue;
        }
    }
}
