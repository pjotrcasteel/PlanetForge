using PlanetForge.Domain.Surface;

namespace PlanetForge.Infrastructure.Surface;

public sealed class ProceduralPlanetElevationSource : IPlanetElevationSource
{
    private const double MaximumLandElevationMeters = 9_000.0;
    private const double MaximumOceanDepthMeters = 7_000.0;

    public double SampleElevationMeters(PlanetVector direction, int seed)
    {
        var continental = FractalNoise(direction, seed, 0.85, 5, 2.03, 0.52);
        var regional = FractalNoise(direction, seed ^ 0x2C1B3C6D, 3.2, 4, 2.11, 0.48);
        var ridges = RidgedNoise(direction, seed ^ 0x51ED270B, 5.6, 4, 2.07, 0.5);
        var detail = FractalNoise(direction, seed ^ 0x6D2B79F5, 13.0, 3, 2.17, 0.44);
        var landMask = SmoothStep(-0.28, 0.42, continental);
        var mountains = ridges * landMask * landMask;
        var normalizedElevation = Math.Clamp((continental * 0.61) + (regional * 0.17) + (mountains * 0.29) + (detail * 0.06) - 0.04, -1.0, 1.0);
        return normalizedElevation >= 0.0 ? normalizedElevation * MaximumLandElevationMeters : normalizedElevation * MaximumOceanDepthMeters;
    }

    private static double FractalNoise(PlanetVector direction, int seed, double frequency, int octaves, double lacunarity, double persistence)
    {
        var amplitude = 1.0;
        var sum = 0.0;
        var normalization = 0.0;

        for (var octave = 0; octave < octaves; octave++)
        {
            sum += ValueNoise(direction.X * frequency, direction.Y * frequency, direction.Z * frequency, seed + (octave * 1013)) * amplitude;
            normalization += amplitude;
            frequency *= lacunarity;
            amplitude *= persistence;
        }

        return sum / normalization;
    }

    private static double RidgedNoise(PlanetVector direction, int seed, double frequency, int octaves, double lacunarity, double persistence)
    {
        var amplitude = 1.0;
        var sum = 0.0;
        var normalization = 0.0;

        for (var octave = 0; octave < octaves; octave++)
        {
            var noise = ValueNoise(direction.X * frequency, direction.Y * frequency, direction.Z * frequency, seed + (octave * 1297));
            var ridge = 1.0 - Math.Abs(noise);
            ridge = (ridge * ridge * 2.0) - 1.0;
            sum += ridge * amplitude;
            normalization += amplitude;
            frequency *= lacunarity;
            amplitude *= persistence;
        }

        return sum / normalization;
    }

    private static double ValueNoise(double x, double y, double z, int seed)
    {
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var z0 = (int)Math.Floor(z);
        var tx = Fade(x - x0);
        var ty = Fade(y - y0);
        var tz = Fade(z - z0);
        var x00 = Lerp(HashValue(x0, y0, z0, seed), HashValue(x0 + 1, y0, z0, seed), tx);
        var x10 = Lerp(HashValue(x0, y0 + 1, z0, seed), HashValue(x0 + 1, y0 + 1, z0, seed), tx);
        var x01 = Lerp(HashValue(x0, y0, z0 + 1, seed), HashValue(x0 + 1, y0, z0 + 1, seed), tx);
        var x11 = Lerp(HashValue(x0, y0 + 1, z0 + 1, seed), HashValue(x0 + 1, y0 + 1, z0 + 1, seed), tx);
        return Lerp(Lerp(x00, x10, ty), Lerp(x01, x11, ty), tz);
    }

    private static double HashValue(int x, int y, int z, int seed)
    {
        var hash = unchecked((uint)seed);
        hash ^= unchecked((uint)x) * 0x9E3779B9u;
        hash = RotateLeft(hash, 13) * 0x85EBCA6Bu;
        hash ^= unchecked((uint)y) * 0xC2B2AE35u;
        hash = RotateLeft(hash, 11) * 0x27D4EB2Fu;
        hash ^= unchecked((uint)z) * 0x165667B1u;
        hash ^= hash >> 15;
        hash *= 0x2C1B3C6Du;
        hash ^= hash >> 12;
        return ((hash & 0x00FFFFFFu) / 8_388_607.5) - 1.0;
    }

    private static uint RotateLeft(uint value, int count) => (value << count) | (value >> (32 - count));

    private static double Fade(double value) => value * value * value * (value * ((value * 6.0) - 15.0) + 10.0);

    private static double Lerp(double from, double to, double amount) => from + ((to - from) * amount);

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3.0 - (2.0 * t));
    }
}
