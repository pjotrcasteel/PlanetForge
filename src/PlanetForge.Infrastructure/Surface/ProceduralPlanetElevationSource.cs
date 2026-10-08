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
        var fields = SampleTerrainFields(direction, seed);
        return fields.ElevationMeters + (terrainDeformationStore?.SampleElevationDeltaMeters(direction, seed) ?? 0.0);
    }

    // The same canonical geological fields drive the globe, local terrain and the diagnostic laboratory.
    // Display layers must never recreate a different terrain algorithm.
    public PlanetTerrainFieldSample SampleTerrainFields(PlanetVector direction, int seed)
    {
        var macroDirection = WarpDirection(direction, seed ^ WarpSeedSalt, 0.29, 1.16);
        var continental = FractalNoise(macroDirection, seed ^ CrustSeedSalt, 1.22, 4, 2.06, 0.53) * 0.74;
        continental += FractalNoise(macroDirection, seed ^ RegionalSeedSalt, 2.95, 3, 2.09, 0.51) * 0.29;
        continental += FractalNoise(direction, seed ^ CoastSeedSalt, 6.8, 3, 2.09, 0.46) * 0.075;

        // Continental topology is derived from coherent spherical fields, not radial plate ownership.
        // The boundaries below shape mountain belts, never the outline of a continent.
        var crust = (continental * 0.80) - 0.025;
        var landMask = SmoothStep(-0.095, 0.19, crust);
        var province = FractalNoise(macroDirection, seed ^ ProvinceSeedSalt, 2.5, 3, 2.06, 0.49);
        var basin = FractalNoise(direction, seed ^ BasinSeedSalt, 3.8, 3, 2.11, 0.49);
        var nearest = FindNearestPlates(macroDirection, seed);
        var boundaryNoise = FractalNoise(direction, seed ^ BoundarySeedSalt, 4.2, 3, 2.05, 0.49);
        var separation = Math.Max(0.0, nearest.PrimaryDot - nearest.SecondaryDot);
        var belt = 1.0 - SmoothStep(0.007, 0.075, Math.Max(0.0, separation + boundaryNoise * 0.013));
        var plateUplift = SampleTectonicRelief(direction, seed, nearest,
            IsContinentalPlate(seed, nearest.PrimaryIndex),
            IsContinentalPlate(seed, nearest.SecondaryIndex), belt, boundaryNoise);

        // Old ranges are broad provinces; active ranges follow narrow tectonic belts.
        // Finer ridges modulate those envelopes but cannot define the continents.
        var rangeEnvelope = RidgedNoise(macroDirection, seed ^ UplandSeedSalt, 2.85, 3, 2.09, 0.50);
        var oldRange = SmoothStep(0.06, 0.56, rangeEnvelope) * SmoothStep(-0.12, 0.38, province);
        // Spatially connected, curved orogenic belts span thousands of kilometres.
        // Unlike isolated noise peaks, these arcs have longitudinal continuity and finite widths.
        var orogeny = SampleOrogenicSystems(macroDirection, seed, boundaryNoise);
        var mountainBelt = Math.Max(orogeny, Math.Max(oldRange * 0.43, belt * 0.48));
        var narrowRidges = RidgedNoise(direction, seed ^ DetailSeedSalt, 13.0, 4, 2.09, 0.47);
        var ridgeStrength = Math.Max(0.0, narrowRidges);
        var summitStructure = ridgeStrength * ridgeStrength;
        var uplift = landMask * ((plateUplift * 0.52) + (mountainBelt * (0.12 + summitStructure * 0.37)));
        var rolling = FractalNoise(direction, seed ^ DetailSeedSalt, 5.2, 3, 2.1, 0.48);
        var terrain = landMask * ((province * 0.083) + (rolling * 0.036) - (Math.Max(0.0, -basin) * 0.055));

        // Branching bedrock gullies break up long ridges. Flow accumulation, hydraulic erosion and
        // sediment transport are intentionally reserved for the geological evolution pipeline.
        var valleyField = RidgedNoise(direction, seed ^ BasinSeedSalt, 18.0, 3, 2.05, 0.49);
        var incisions = Math.Pow(Math.Max(0.0, valleyField), 3.0) * mountainBelt * landMask * 0.075;
        var impacts = SampleImpactRelief(direction, seed);
        var coastFine = FractalNoise(direction, seed ^ CoastSeedSalt, 13.0, 2, 2.11, 0.48);
        var shoreMask = 1.0 - SmoothStep(0.015, 0.10, Math.Abs(crust));
        var normalized = crust + uplift + terrain - incisions + impacts + (coastFine * shoreMask * 0.024);

        if (normalized < 0.0)
        {
            // Continental shelves grade into abyssal plains without plate-shaped walls.
            var shelf = 1.0 - SmoothStep(0.0, 0.19, -normalized);
            var seafloor = FractalNoise(direction, seed ^ ShelfSeedSalt, 5.5, 2, 2.07, 0.48);
            normalized += (seafloor * 0.025) - ((1.0 - shelf) * 0.045);
        }

        normalized = Math.Clamp(normalized, -1.0, 1.0);
        var elevation = normalized >= 0.0 ? normalized * MaximumLandElevationMeters : normalized * MaximumOceanDepthMeters;
        return new PlanetTerrainFieldSample(continental, plateUplift * belt, mountainBelt, elevation);
    }

    private static double SampleOrogenicSystems(PlanetVector direction, int seed, double distortion)
    {
        // A deterministic catalogue of geodesic arcs defines old mountain belts independently
        // from the plate assignment. Plate stresses still influence the separate uplift field.
        var strongest = 0.0;

        for (var index = 0; index < 11; index++)
        {
            var start = SeedDirection(seed ^ UplandSeedSalt, index, 70_117 + (index * 193));
            var pole = SeedDirection(seed ^ ProvinceSeedSalt, index, 230_019 + (index * 317));
            var tangent = PlanetVector.Cross(pole, start);

            if (tangent.Length < 0.000001)
            {
                tangent = PlanetVector.Cross(Math.Abs(start.Y) < 0.9 ? PlanetVector.UnitY : PlanetVector.UnitX, start);
            }

            tangent = PlanetVector.Normalize(tangent);
            var arcNormal = PlanetVector.Normalize(PlanetVector.Cross(start, tangent));
            var halfSpan = Lerp(0.28, 0.71, ToUnitRange(HashValue(index, seed, UplandSeedSalt, ProvinceSeedSalt)));
            var center = PlanetVector.Normalize((start * Math.Cos(halfSpan * 0.5)) + (tangent * Math.Sin(halfSpan * 0.5)));
            var breadth = Lerp(0.035, 0.085, ToUnitRange(HashValue(index, BasinSeedSalt, seed, DetailSeedSalt)));
            var arcDistance = Math.Asin(Math.Clamp(Math.Abs(PlanetVector.Dot(direction, arcNormal)), 0.0, 1.0));
            var warpedDistance = Math.Max(0.0, arcDistance + (distortion * breadth * 0.35));
            var crossSection = 1.0 - SmoothStep(breadth * 0.10, breadth, warpedDistance);
            var along = PlanetVector.Dot(direction, center);
            var lengthMask = SmoothStep(Math.Cos(halfSpan), Math.Cos(halfSpan * 0.70), along);
            var influence = crossSection * lengthMask;
            strongest = Math.Max(strongest, influence);
        }

        return strongest;
    }

    private static double SampleImpactRelief(PlanetVector direction, int seed)
    {
        var relief = 0.0;
        for (var index = 0; index < 9; index++)
        {
            var center = SeedDirection(seed ^ RegionalSeedSalt, index, 303_019 + (index * 911));
            var dot = Math.Clamp(PlanetVector.Dot(direction, center), -1.0, 1.0);
            var radius = Lerp(0.045, 0.18, ToUnitRange(HashValue(index, seed, CrustSeedSalt, 3_571)));
            if (dot < Math.Cos(radius * 1.30))
            {
                continue;
            }

            var distance = Math.Acos(dot) / radius;
            var floor = 1.0 - SmoothStep(0.15, 0.95, distance);
            var rim = 1.0 - SmoothStep(0.0, 0.17, Math.Abs(distance - 1.0));
            relief += (rim * 0.022) - (floor * 0.045);
        }

        return relief;
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