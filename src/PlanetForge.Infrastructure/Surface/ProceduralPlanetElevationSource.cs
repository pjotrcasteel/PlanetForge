using PlanetForge.Domain.Surface;

namespace PlanetForge.Infrastructure.Surface;

public sealed class ProceduralPlanetElevationSource : IPlanetElevationSource
{
    private const double MaximumLandElevationMeters = 8_400.0;
    private const double MaximumOceanDepthMeters = 7_600.0;
    private const int PlateCount = 18;
    private const int ContinentalPlateCount = 7;
    private const int PlateSeedSalt = 0x13579BDF;
    private const int CrustSeedSalt = 0x02468ACE;
    private const int WarpSeedSalt = 0x37A4F91D;
    private const int RegionalSeedSalt = 0x2C1B3C6D;
    private const int DetailSeedSalt = 0x6D2B79F5;
    private const int MotionSeedSalt = 0x51ED270B;
    private const int BoundarySeedSalt = 0x4F1BBCDC;
    private const int ProvinceSeedSalt = 0x61C88647;
    private const int UplandSeedSalt = 0x1B873593;
    private const int BasinSeedSalt = 0x7F4A7C15;
    private const int CoastSeedSalt = 0x3C6EF372;
    private const int ShelfSeedSalt = 0x5A827999;

    private readonly IPlanetTerrainDeformationStore? terrainDeformationStore;

    public ProceduralPlanetElevationSource()
    {
    }

    public ProceduralPlanetElevationSource(IPlanetTerrainDeformationStore terrainDeformationStore) => this.terrainDeformationStore = terrainDeformationStore;

    public double SampleElevationMeters(PlanetVector direction, int seed)
    {
        var normalizedElevation = SamplePlateWorld(direction, seed);
        var baseElevationMeters = normalizedElevation >= 0.0
            ? normalizedElevation * MaximumLandElevationMeters
            : normalizedElevation * MaximumOceanDepthMeters;
        return baseElevationMeters + (terrainDeformationStore?.SampleElevationDeltaMeters(direction, seed) ?? 0.0);
    }

    private static double SamplePlateWorld(PlanetVector direction, int seed)
    {
        // World-Machine-style staged synthesis: broad form first, geology second, then terrain character.
        // Plate ownership is deliberately never used as the visible surface.
        var macroDirection = WarpDirection(direction, seed, 0.18, 0.72);
        var continentalPotential = SampleContinentalPotential(macroDirection, seed);
        var continentalWarp = FractalNoise(macroDirection, seed ^ RegionalSeedSalt, 0.72, 4, 2.01, 0.52);
        var continentalDetail = FractalNoise(WarpDirection(direction, seed ^ CoastSeedSalt, 0.055, 2.2), seed ^ CoastSeedSalt, 2.8, 4, 2.08, 0.49);
        var crustField = continentalPotential + (continentalWarp * 0.23) + (continentalDetail * 0.07);
        var normalized = (crustField * 0.34) - 0.37;

        var continentalWeight = SmoothStep(-0.06, 0.14, normalized);
        var provinceDirection = WarpDirection(direction, seed ^ ProvinceSeedSalt, 0.10, 1.55);
        var province = FractalNoise(provinceDirection, seed ^ ProvinceSeedSalt, 1.45, 4, 2.02, 0.52);
        var rollingRelief = FractalNoise(provinceDirection, seed ^ DetailSeedSalt, 4.6, 4, 2.10, 0.47);
        var basin = FractalNoise(provinceDirection, seed ^ BasinSeedSalt, 2.4, 4, 2.07, 0.50);
        normalized += continentalWeight * ((province * 0.050) + (rollingRelief * 0.020) - (Math.Max(0.0, -basin) * 0.025));

        // Tectonics provide narrow uplift/rift masks. They influence geology but never define continent silhouettes.
        var nearest = FindNearestPlates(macroDirection, seed);
        var primaryContinental = IsContinentalPlate(seed, nearest.PrimaryIndex);
        var secondaryContinental = IsContinentalPlate(seed, nearest.SecondaryIndex);
        var boundaryNoise = FractalNoise(direction, seed ^ BoundarySeedSalt, 3.9, 4, 2.07, 0.50);
        var boundaryDistance = Math.Max(0.0, nearest.PrimaryDot - nearest.SecondaryDot);
        var boundaryInfluence = 1.0 - SmoothStep(0.006, 0.060, Math.Max(0.0, boundaryDistance + (boundaryNoise * 0.012)));
        var tectonics = SampleTectonicRelief(direction, seed, nearest, primaryContinental, secondaryContinental, boundaryInfluence, boundaryNoise);
        normalized += tectonics * continentalWeight * 0.72;

        // Geomorphic hierarchy: ranges are broad envelopes cut by finer ridges/valleys instead of plate-sized slabs.
        var rangeEnvelope = Math.Max(0.0, RidgedNoise(provinceDirection, seed ^ UplandSeedSalt, 2.1, 3, 2.03, 0.50));
        var ridgeSystem = RidgedNoise(WarpDirection(direction, seed ^ UplandSeedSalt, 0.045, 5.0), seed ^ UplandSeedSalt, 7.5, 4, 2.08, 0.48);
        var drainage = FractalNoise(WarpDirection(direction, seed ^ BasinSeedSalt, 0.035, 8.0), seed ^ BasinSeedSalt, 13.0, 3, 2.05, 0.48);
        var mountainMask = continentalWeight * SmoothStep(0.18, 0.72, rangeEnvelope + (boundaryInfluence * 0.65));
        var ridges = Math.Max(0.0, ridgeSystem) * mountainMask;
        var valleys = Math.Max(0.0, -drainage) * mountainMask;
        normalized += ridges * 0.095;
        normalized -= valleys * 0.040;

        // Sea level intersects the already-built terrain. Near-shore variation is subtle and cannot create stair-step coasts.
        var coastEnvelope = 1.0 - SmoothStep(0.0, 0.10, Math.Abs(normalized));
        var coastDetail = FractalNoise(WarpDirection(direction, seed ^ CoastSeedSalt, 0.035, 5.5), seed ^ CoastSeedSalt, 9.0, 3, 2.13, 0.47);
        normalized += coastDetail * coastEnvelope * 0.018;

        if (normalized < 0.0)
        {
            var shelf = 1.0 - SmoothStep(0.0, 0.18, -normalized);
            var abyssal = FractalNoise(direction, seed ^ ShelfSeedSalt, 3.1, 3, 2.09, 0.48);
            normalized += abyssal * Lerp(0.018, 0.006, shelf);
            normalized -= (1.0 - shelf) * 0.035;
        }

        return Math.Clamp(normalized, -1.0, 1.0);
    }

    private static double SampleContinentalPotential(PlanetVector direction, int seed)
    {
        var strongest = -1.0;
        var second = -1.0;

        for (var index = 0; index < PlateCount; index++)
        {
            if (!IsContinentalPlate(seed, index))
            {
                continue;
            }

            var center = PlateCenter(seed, index);
            var dot = PlanetVector.Dot(direction, center);
            var size = Lerp(0.28, 0.54, ToUnitRange(HashValue(index, seed ^ CrustSeedSalt, PlateSeedSalt, seed)));
            var edge = Lerp(0.82, 0.46, size);
            var influence = SmoothStep(edge - 0.18, edge + 0.12, dot);
            if (influence > strongest)
            {
                second = strongest;
                strongest = influence;
            }
            else if (influence > second)
            {
                second = influence;
            }
        }

        var merged = strongest + (Math.Max(0.0, second) * 0.42);
        return (merged * 2.0) - 1.0;
    }

    private static double SampleNaturalTerrain(PlanetVector direction, int seed, double elevation, double continentalWeight, double boundaryInfluence)
    {
        if (continentalWeight <= 0.001)
        {
            var abyssal = FractalNoise(direction, seed ^ BasinSeedSalt, 3.4, 3, 2.05, 0.48);
            return abyssal * 0.022;
        }

        var provinceDirection = WarpDirection(direction, seed ^ ProvinceSeedSalt, 0.11, 2.4);
        var province = FractalNoise(provinceDirection, seed ^ ProvinceSeedSalt, 1.35, 4, 2.01, 0.54);
        var uplands = RidgedNoise(provinceDirection, seed ^ UplandSeedSalt, 3.8, 4, 2.07, 0.49);
        var basinField = FractalNoise(provinceDirection, seed ^ BasinSeedSalt, 2.7, 4, 2.09, 0.50);
        var oldHighlands = Math.Max(0.0, uplands) * SmoothStep(0.02, 0.55, province);
        var basins = Math.Max(0.0, -basinField) * SmoothStep(-0.45, 0.25, province);
        var plains = 1.0 - Math.Clamp(Math.Abs(province) * 1.7, 0.0, 1.0);
        var coastDistance = Math.Abs(elevation);
        var coastalEnvelope = 1.0 - SmoothStep(0.015, 0.18, coastDistance);
        var coastDetail = FractalNoise(direction, seed ^ CoastSeedSalt, 7.0, 4, 2.17, 0.48);

        var relief = province * 0.075;
        relief += oldHighlands * 0.105;
        relief -= basins * 0.085;
        relief += plains * basinField * 0.018;
        relief += coastDetail * coastalEnvelope * 0.045;
        relief *= Lerp(0.82, 1.0, 1.0 - boundaryInfluence);
        return relief * continentalWeight;
    }

    private static double SampleTectonicRelief(
        PlanetVector direction,
        int seed,
        NearestPlatePair nearest,
        bool primaryContinental,
        bool secondaryContinental,
        double boundaryInfluence,
        double boundaryNoise)
    {
        if (boundaryInfluence <= 0.001)
        {
            return 0.0;
        }

        var primaryCenter = PlateCenter(seed, nearest.PrimaryIndex);
        var secondaryCenter = PlateCenter(seed, nearest.SecondaryIndex);
        var boundaryNormal = PlanetVector.Normalize(primaryCenter - secondaryCenter);
        var primaryMotion = PlateMotion(seed, nearest.PrimaryIndex, primaryCenter);
        var secondaryMotion = PlateMotion(seed, nearest.SecondaryIndex, secondaryCenter);
        var relativeMotion = primaryMotion - secondaryMotion;
        var convergence = PlanetVector.Dot(relativeMotion, boundaryNormal);
        var tangent = PlanetVector.Cross(direction, boundaryNormal);
        var tangentLength = tangent.Length;
        var transform = tangentLength <= 0.000001 ? 0.0 : Math.Abs(PlanetVector.Dot(relativeMotion, tangent / tangentLength));
        var ridgeTexture = RidgedNoise(direction, seed ^ BoundarySeedSalt ^ (nearest.PrimaryIndex * 977) ^ (nearest.SecondaryIndex * 1297), 9.0, 4, 2.05, 0.50);
        var brokenRange = Lerp(0.55, 1.0, ToUnitRange(boundaryNoise)) * Lerp(0.68, 1.15, ToUnitRange(ridgeTexture));
        var envelope = boundaryInfluence * boundaryInfluence;

        if (convergence > 0.025)
        {
            if (primaryContinental && secondaryContinental)
            {
                return envelope * brokenRange * Lerp(0.16, 0.34, Math.Clamp(convergence * 2.8, 0.0, 1.0));
            }

            if (primaryContinental != secondaryContinental)
            {
                var continentalSide = primaryContinental ? 1.0 : -1.0;
                var coastalRange = envelope * brokenRange * Lerp(0.10, 0.24, Math.Clamp(convergence * 2.6, 0.0, 1.0));
                var trench = envelope * Lerp(0.08, 0.18, Math.Clamp(convergence * 2.6, 0.0, 1.0));
                return (coastalRange * (primaryContinental ? 1.0 : 0.45)) - (trench * (continentalSide < 0.0 ? 1.0 : 0.55));
            }

            return -envelope * Lerp(0.05, 0.13, Math.Clamp(convergence * 2.4, 0.0, 1.0));
        }

        if (convergence < -0.025)
        {
            var divergence = Math.Clamp(-convergence * 2.8, 0.0, 1.0);
            if (!primaryContinental && !secondaryContinental)
            {
                return envelope * brokenRange * Lerp(0.035, 0.095, divergence);
            }

            return -envelope * Lerp(0.025, 0.075, divergence);
        }

        return (ridgeTexture * 0.025) * envelope * Math.Clamp(transform * 3.5, 0.0, 1.0);
    }

    private static PlanetVector PlateMotion(int seed, int plateIndex, PlanetVector center)
    {
        var pole = SeedDirection(seed ^ MotionSeedSalt, plateIndex, 15_013 + (plateIndex * 211));
        var tangent = PlanetVector.Cross(pole, center);
        if (tangent.Length <= 0.000001)
        {
            tangent = PlanetVector.Cross(PlanetVector.UnitY, center);
        }

        var speed = Lerp(0.35, 1.0, ToUnitRange(HashValue(plateIndex, MotionSeedSalt, seed, seed ^ BoundarySeedSalt)));
        return PlanetVector.Normalize(tangent) * speed;
    }

    private static PlanetVector WarpDirection(PlanetVector direction, int seed, double strength, double frequency)
    {
        var x = FractalNoise(direction, seed ^ WarpSeedSalt, frequency, 4, 2.03, 0.52);
        var y = FractalNoise(new PlanetVector(direction.Y, direction.Z, direction.X), seed ^ RegionalSeedSalt, frequency, 4, 2.03, 0.52);
        var z = FractalNoise(new PlanetVector(direction.Z, direction.X, direction.Y), seed ^ DetailSeedSalt, frequency, 4, 2.03, 0.52);
        return PlanetVector.Normalize(direction + (new PlanetVector(x, y, z) * strength));
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
        return Lerp(0.075, 0.235, variation);
    }

    private static double OceanicBase(int seed, int plateIndex)
    {
        var variation = ToUnitRange(HashValue(plateIndex, seed, CrustSeedSalt, seed ^ DetailSeedSalt));
        return -Lerp(0.25, 0.56, variation);
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

    private static double RidgedNoise(PlanetVector direction, int seed, double frequency, int octaves, double lacunarity, double persistence)
    {
        var amplitude = 1.0;
        var sum = 0.0;
        var normalization = 0.0;

        for (var octave = 0; octave < octaves; octave++)
        {
            var noise = ValueNoise(direction.X * frequency, direction.Y * frequency, direction.Z * frequency, seed + (octave * 1297));
            var ridge = 1.0 - Math.Abs(noise);
            sum += ((ridge * ridge * 2.0) - 1.0) * amplitude;
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
}