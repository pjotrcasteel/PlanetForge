using PlanetForge.Domain.Surface;

namespace PlanetForge.Infrastructure.Surface;

public sealed class ProceduralPlanetElevationSource : IPlanetElevationSource
{
    private const double MaximumLandElevationMeters = 7_500.0;
    private const double MaximumOceanDepthMeters = 7_500.0;
    private const int PlateCount = 18;
    private const int ContinentalPlateCount = 7;
    private const int PlateSeedSalt = 0x13579BDF;
    private const int CrustSeedSalt = 0x2468ACE;
    private const int WarpSeedSalt = 0x37A4F91D;
    private const int RegionalSeedSalt = 0x2C1B3C6D;
    private const int DetailSeedSalt = 0x6D2B79F5;

    private readonly IPlanetTerrainDeformationStore? terrainDeformationStore;

    public ProceduralPlanetElevationSource()
    {
    }

    public ProceduralPlanetElevationSource(IPlanetTerrainDeformationStore terrainDeformationStore)
    {
        this.terrainDeformationStore = terrainDeformationStore;
    }

    public double SampleElevationMeters(PlanetVector direction, int seed)
    {
        var sample = SamplePlateWorld(direction, seed);
        var baseElevationMeters = sample.NormalizedElevation >= 0.0
            ? sample.NormalizedElevation * MaximumLandElevationMeters
            : sample.NormalizedElevation * MaximumOceanDepthMeters;
        return baseElevationMeters + (terrainDeformationStore?.SampleElevationDeltaMeters(direction, seed) ?? 0.0);
    }

    private static PlateWorldSample SamplePlateWorld(PlanetVector direction, int seed)
    {
        var warpedDirection = WarpDirection(direction, seed);
        var nearest = FindNearestPlates(warpedDirection, seed);
        var primaryContinental = IsContinentalPlate(seed, nearest.PrimaryIndex);
        var secondaryContinental = IsContinentalPlate(seed, nearest.SecondaryIndex);
        var boundary = SmoothStep(0.0, 0.115, nearest.PrimaryDot - nearest.SecondaryDot);
        var regional = FractalNoise(direction, seed ^ RegionalSeedSalt, 2.15, 4, 2.08, 0.50);
        var detail = FractalNoise(direction, seed ^ DetailSeedSalt, 8.5, 3, 2.17, 0.44);

        var primaryBase = primaryContinental ? ContinentalBase(seed, nearest.PrimaryIndex) : OceanicBase(seed, nearest.PrimaryIndex);
        var secondaryBase = secondaryContinental ? ContinentalBase(seed, nearest.SecondaryIndex) : OceanicBase(seed, nearest.SecondaryIndex);
        var edgeBlend = 1.0 - boundary;
        var crust = Lerp(primaryBase, secondaryBase, edgeBlend * 0.36);

        var continentalInterior = primaryContinental ? boundary : 0.0;
        var oceanicInterior = primaryContinental ? 0.0 : boundary;
        var normalized = crust;
        normalized += regional * Lerp(0.075, 0.16, continentalInterior);
        normalized += detail * Lerp(0.018, 0.038, continentalInterior);
        normalized -= Math.Max(0.0, -regional) * oceanicInterior * 0.055;

        return new PlateWorldSample(Math.Clamp(normalized, -1.0, 1.0), primaryContinental);
    }

    private static PlanetVector WarpDirection(PlanetVector direction, int seed)
    {
        var x = FractalNoise(direction, seed ^ WarpSeedSalt, 1.55, 3, 2.03, 0.52);
        var y = FractalNoise(new PlanetVector(direction.Y, direction.Z, direction.X), seed ^ RegionalSeedSalt, 1.55, 3, 2.03, 0.52);
        var z = FractalNoise(new PlanetVector(direction.Z, direction.X, direction.Y), seed ^ DetailSeedSalt, 1.55, 3, 2.03, 0.52);
        return PlanetVector.Normalize(direction + (new PlanetVector(x, y, z) * 0.105));
    }

    private static NearestPlatePair FindNearestPlates(PlanetVector direction, int seed)
    {
        var primaryIndex = -1;
        var secondaryIndex = -1;
        var primaryDot = double.NegativeInfinity;
        var secondaryDot = double.NegativeInfinity;

        for (var index = 0; index < PlateCount; index++)
        {
            var dot = PlanetVector.Dot(direction, PlateCenter(seed, index));
            if (dot > primaryDot)
            {
                secondaryIndex = primaryIndex;
                secondaryDot = primaryDot;
                primaryIndex = index;
                primaryDot = dot;
            }
            else if (dot > secondaryDot)
            {
                secondaryIndex = index;
                secondaryDot = dot;
            }
        }

        return new NearestPlatePair(primaryIndex, secondaryIndex, primaryDot, secondaryDot);
    }

    private static PlanetVector PlateCenter(int seed, int index) => SeedDirection(seed ^ PlateSeedSalt, index, 8_191 + (index * 137));

    private static bool IsContinentalPlate(int seed, int plateIndex)
    {
        var rankedIndex = PositiveModulo((plateIndex * 11) + PositiveModulo(seed ^ CrustSeedSalt, PlateCount), PlateCount);
        return rankedIndex < ContinentalPlateCount;
    }

    private static double ContinentalBase(int seed, int plateIndex)
    {
        var variation = ToUnitRange(HashValue(plateIndex, CrustSeedSalt, seed, seed ^ RegionalSeedSalt));
        return Lerp(0.12, 0.34, variation);
    }

    private static double OceanicBase(int seed, int plateIndex)
    {
        var variation = ToUnitRange(HashValue(plateIndex, seed, CrustSeedSalt, seed ^ DetailSeedSalt));
        return -Lerp(0.28, 0.62, variation);
    }

    private static PlanetVector SeedDirection(int seed, int index, int salt)
    {
        var x = HashValue((index * 31) + 17, salt, (index * 7) - 11, seed);
        var y = HashValue((index * 43) - 5, salt ^ 0x5A5A5A5A, (index * 13) + 3, seed ^ DetailSeedSalt);
        var z = HashValue((index * 59) + 9, salt ^ PlateSeedSalt, (index * 19) - 7, seed ^ RegionalSeedSalt);
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

    private static int PositiveModulo(int value, int modulus)
    {
        var remainder = value % modulus;
        return remainder < 0 ? remainder + modulus : remainder;
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

    private readonly record struct NearestPlatePair(int PrimaryIndex, int SecondaryIndex, double PrimaryDot, double SecondaryDot);

    private readonly record struct PlateWorldSample(double NormalizedElevation, bool Continental);
}