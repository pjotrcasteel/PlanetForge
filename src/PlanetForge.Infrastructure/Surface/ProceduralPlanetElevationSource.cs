using PlanetForge.Domain.Surface;

namespace PlanetForge.Infrastructure.Surface;

public sealed class ProceduralPlanetElevationSource : IPlanetElevationSource
{
    private const double MaximumLandElevationMeters = 9_000.0;
    private const double MaximumOceanDepthMeters = 7_000.0;
    private const int MountainBeltCount = 7;
    private const int PlateauCount = 4;
    private const int BasinCount = 5;
    private const int RegionalSeedSalt = 0x2C1B3C6D;
    private const int RidgeSeedSalt = 0x51ED270B;
    private const int DetailSeedSalt = 0x6D2B79F5;
    private const int PlateauSeedSalt = 0x13579BDF;
    private const int BasinSeedSalt = 0x02468ACE;
    private const int MountainBreakSeedSalt = 0x4F1BBCDC;
    private const int MountainWarpSeedSalt = 0x37A4F91D;

    public double SampleElevationMeters(PlanetVector direction, int seed)
    {
        var continental = FractalNoise(direction, seed, 0.72, 5, 2.03, 0.52);
        var regional = FractalNoise(direction, seed ^ RegionalSeedSalt, 2.4, 4, 2.11, 0.48);
        var detail = FractalNoise(direction, seed ^ DetailSeedSalt, 14.0, 3, 2.17, 0.44);
        var landMask = SmoothStep(-0.30, 0.30, continental);
        var mountainBelts = SampleMountainBelts(direction, seed, landMask);
        var plateaus = SampleSphericalRegions(direction, seed, PlateauSeedSalt, PlateauCount, 0.82, 0.965) * landMask;
        var basins = SampleSphericalRegions(direction, seed, BasinSeedSalt, BasinCount, 0.88, 0.985) * landMask;
        var normalizedElevation = Math.Clamp(
            (continental * 0.61) +
            (regional * 0.13) +
            (mountainBelts * 0.58) +
            (plateaus * 0.11) -
            (basins * 0.14) +
            (detail * 0.035) -
            0.055,
            -1.0,
            1.0);
        return normalizedElevation >= 0.0 ? normalizedElevation * MaximumLandElevationMeters : normalizedElevation * MaximumOceanDepthMeters;
    }

    private static double SampleMountainBelts(PlanetVector direction, int seed, double landMask)
    {
        var strongestBelt = 0.0;

        for (var index = 0; index < MountainBeltCount; index++)
        {
            var normal = SeedDirection(seed ^ RegionalSeedSalt, index, 4_517 + (index * 73));
            var anchor = SeedDirection(seed ^ DetailSeedSalt, index, 9_277 + (index * 97));
            var projectedAnchor = anchor - (normal * PlanetVector.Dot(anchor, normal));
            if (projectedAnchor.Length <= 0.000001)
            {
                continue;
            }

            var arcCenter = PlanetVector.Normalize(projectedAnchor);
            var signedDistance = PlanetVector.Dot(direction, normal);
            var warp = FractalNoise(direction, seed ^ MountainWarpSeedSalt ^ (index * 761), 3.4, 3, 2.07, 0.50) * 0.032;
            var warpedSignedDistance = signedDistance + warp;
            var distanceFromRange = Math.Abs(warpedSignedDistance);
            var arcExtent = SmoothStep(-0.18, 0.78, PlanetVector.Dot(direction, arcCenter));
            var foothills = 1.0 - SmoothStep(0.035, 0.145, distanceFromRange);
            var centralCrest = 1.0 - SmoothStep(0.006, 0.052, distanceFromRange);
            var secondaryRidges = SampleSecondaryRidges(warpedSignedDistance);
            var ridgeNoise = ToUnitRange(RidgedNoise(direction, seed ^ RidgeSeedSalt ^ (index * 1_297), 11.0, 4, 2.06, 0.50));
            var breakNoise = ToUnitRange(FractalNoise(direction, seed ^ MountainBreakSeedSalt ^ (index * 977), 4.6, 3, 2.13, 0.52));
            var continuity = SmoothStep(0.20, 0.68, breakNoise);
            var crestHeight = centralCrest * (0.58 + (ridgeNoise * 0.52));
            var shoulderHeight = foothills * (0.20 + (ridgeNoise * 0.16));
            var parallelHeight = secondaryRidges * (0.16 + (ridgeNoise * 0.16));
            var range = (crestHeight + shoulderHeight + parallelHeight) * Lerp(0.42, 1.0, continuity) * arcExtent;
            strongestBelt = Math.Max(strongestBelt, range);
        }

        return strongestBelt * landMask * landMask;
    }

    private static double SampleSecondaryRidges(double signedDistance)
    {
        var first = 1.0 - SmoothStep(0.010, 0.027, Math.Abs(signedDistance - 0.062));
        var second = 1.0 - SmoothStep(0.010, 0.027, Math.Abs(signedDistance + 0.062));
        return Math.Max(first, second);
    }

    private static double SampleSphericalRegions(PlanetVector direction, int seed, int seedSalt, int count, double edgeDot, double coreDot)
    {
        var strongestRegion = 0.0;

        for (var index = 0; index < count; index++)
        {
            var center = SeedDirection(seed ^ seedSalt, index, seedSalt + (index * 137));
            strongestRegion = Math.Max(strongestRegion, SmoothStep(edgeDot, coreDot, PlanetVector.Dot(direction, center)));
        }

        return strongestRegion;
    }

    private static PlanetVector SeedDirection(int seed, int index, int salt)
    {
        var x = HashValue((index * 31) + 17, salt, (index * 7) - 11, seed);
        var y = HashValue((index * 43) - 5, salt ^ 0x5A5A5A5A, (index * 13) + 3, seed ^ DetailSeedSalt);
        var z = HashValue((index * 59) + 9, salt ^ PlateauSeedSalt, (index * 19) - 7, seed ^ RidgeSeedSalt);
        var vector = new PlanetVector(x, y, z);
        return vector.Length <= 0.000001 ? PlanetVector.UnitX : PlanetVector.Normalize(vector);
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

    private static double ToUnitRange(double value) => Math.Clamp((value + 1.0) * 0.5, 0.0, 1.0);

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3.0 - (2.0 * t));
    }
}