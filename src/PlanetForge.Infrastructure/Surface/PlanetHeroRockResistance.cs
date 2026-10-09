using PlanetForge.Domain.Surface;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Deterministic lithology research field. Hardness affects hydraulic incision
/// but does not add decorative hills, ridges or material-colored river lines.
/// The same spherical location receives identical resistance at any mesh LOD.
/// Erodibility is dimensionless, 1=reference rock and lower=harder bedrock.
/// </summary>
public static class PlanetHeroRockResistance
{
    private const double RadiusMeters = 6_371_000.0;

    public static float SampleErodibility(PlanetVector worldDirection, int seed, double elevationMeters)
    {
        var location = PlanetVector.Normalize(worldDirection) * RadiusMeters;
        var regional = Noise(location.X / 14_000.0, location.Y / 14_000.0, location.Z / 14_000.0, seed ^ 0x2E91);
        var local = Noise(location.X / 2_600.0, location.Y / 2_600.0, location.Z / 2_600.0, seed ^ 0x71A3);
        var layerPhase = (elevationMeters + 450.0 * (regional - 0.5)) * (Math.PI * 2.0 / 680.0);
        var beds = 0.5 + 0.5 * Math.Sin(layerPhase);
        return (float)Math.Clamp(0.48 + 0.68 * (1.0 - regional) +
            0.38 * (1.0 - local) + 0.3 * beds, 0.35, 1.9);
    }

    private static double Noise(double x, double y, double z, int seed)
    {
        var ix = (int)Math.Floor(x);
        var iy = (int)Math.Floor(y);
        var iz = (int)Math.Floor(z);
        var fx = Fade(x - ix);
        var fy = Fade(y - iy);
        var fz = Fade(z - iz);
        var x00 = Lerp(Hash(ix, iy, iz, seed), Hash(ix + 1, iy, iz, seed), fx);
        var x10 = Lerp(Hash(ix, iy + 1, iz, seed), Hash(ix + 1, iy + 1, iz, seed), fx);
        var x01 = Lerp(Hash(ix, iy, iz + 1, seed), Hash(ix + 1, iy, iz + 1, seed), fx);
        var x11 = Lerp(Hash(ix, iy + 1, iz + 1, seed), Hash(ix + 1, iy + 1, iz + 1, seed), fx);
        return Lerp(Lerp(x00, x10, fy), Lerp(x01, x11, fy), fz);
    }

    private static double Fade(double value) => value * value * (3.0 - 2.0 * value);

    private static double Lerp(double a, double b, double ratio) => a + (b - a) * ratio;

    private static double Hash(int x, int y, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)seed ^ ((uint)x * 0x9E3779B9u) ^
                ((uint)y * 0x85EBCA6Bu) ^ ((uint)z * 0xC2B2AE35u);
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return h / (double)uint.MaxValue;
        }
    }
}
