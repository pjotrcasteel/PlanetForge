using PlanetForge.Domain.Surface;

namespace PlanetForge.Infrastructure.Surface;

public sealed class ProceduralPlanetElevationSource : IPlanetElevationSource
{
    private readonly IPlanetTerrainDeformationStore? terrainDeformationStore;

    public ProceduralPlanetElevationSource()
    {
    }

    public ProceduralPlanetElevationSource(IPlanetTerrainDeformationStore terrainDeformationStore)
    {
        this.terrainDeformationStore = terrainDeformationStore;
    }

    private const double MaximumLandElevationMeters = 9_000.0;
    private const double MaximumOceanDepthMeters = 7_000.0;
    private const int ContinentalProvinceCount = 5;
    private const int OceanBasinCount = 4;
    private const int PlateCount = 11;
    private const int PlateauCount = 5;
    private const int BasinCount = 6;
    private const int ContinentalSeedSalt = 0x18A45D39;
    private const int OceanBasinSeedSalt = 0x2F6D4C1B;
    private const int ContinentalNoiseSeedSalt = 0x3B2E19A7;
    private const int MacroWarpSeedSaltX = 0x145A21C3;
    private const int MacroWarpSeedSaltY = 0x265C3D17;
    private const int MacroWarpSeedSaltZ = 0x31B74A29;
    private const int PlateSeedSalt = 0x46C21D35;
    private const int PlateWarpSeedSalt = 0x5A137C49;
    private const int PlateBoundarySeedSalt = 0x63D24B15;
    private const int RegionalSeedSalt = 0x2C1B3C6D;
    private const int RidgeSeedSalt = 0x51ED270B;
    private const int DetailSeedSalt = 0x6D2B79F5;
    private const int PlateauSeedSalt = 0x13579BDF;
    private const int BasinSeedSalt = 0x02468ACE;
    private const int MountainCrestSeedSalt = 0x17C5A3D1;
    private const int ValleySeedSalt = 0x29D48B63;
    private const int ValleyBranchSeedSalt = 0x3E1A72C5;
    private const int CanyonSeedSalt = 0x4C6F21B7;
    private const int CanyonWarpSeedSalt = 0x5D2B83E9;
    private const int ScarpSeedSalt = 0x68A14F2D;
    private const int CanyonSystemCount = 4;

    public double SampleElevationMeters(PlanetVector direction, int seed)
    {
        var warpedDirection = WarpDirection(direction, seed);
        var continental = SampleContinentalField(warpedDirection, seed);
        var landMask = SmoothStep(-0.08, 0.18, continental);
        var regional = FractalNoise(warpedDirection, seed ^ RegionalSeedSalt, 2.1, 4, 2.11, 0.48);
        var detail = FractalNoise(warpedDirection, seed ^ DetailSeedSalt, 13.0, 3, 2.17, 0.44);
        var plateBoundaryUplift = SamplePlateBoundaryUplift(warpedDirection, seed, landMask);
        var plateaus = SampleSphericalRegions(warpedDirection, seed, PlateauSeedSalt, PlateauCount, 0.84, 0.965) * landMask;
        var basins = SampleSphericalRegions(warpedDirection, seed, BasinSeedSalt, BasinCount, 0.86, 0.982) * landMask;
        var uplandRelief = Math.Max(0.0, regional) * Lerp(0.42, 1.0, ToUnitRange(detail)) * landMask;
        var continentalInterior = SampleContinentalInterior(warpedDirection, seed, continental);
        var mountainShape = SampleMountainShape(warpedDirection, seed, plateBoundaryUplift);
        var escarpmentShape = SampleEscarpments(warpedDirection, seed, plateaus);
        var valleyIncision = SampleAncientValleys(warpedDirection, seed, continental, landMask);
        var canyonIncision = SampleCanyonSystems(warpedDirection, seed, continental, landMask);

        var rawElevation =
            (continental * 0.72) +
            (regional * 0.10 * landMask) +
            (plateBoundaryUplift * 0.73) +
            (mountainShape * 0.31) +
            (uplandRelief * 0.08) +
            (plateaus * 0.14) +
            (escarpmentShape * 0.15) +
            (continentalInterior * 0.16) -
            (basins * 0.18) -
            (valleyIncision * 0.11) -
            (canyonIncision * 0.17) +
            (detail * 0.025) -
            0.01;

        var normalizedElevation = Math.Tanh(rawElevation * 1.05);
        var baseElevationMeters = normalizedElevation >= 0.0
            ? normalizedElevation * MaximumLandElevationMeters
            : normalizedElevation * MaximumOceanDepthMeters;
        return baseElevationMeters + (terrainDeformationStore?.SampleElevationDeltaMeters(direction, seed) ?? 0.0);
    }

    private static PlanetVector WarpDirection(PlanetVector direction, int seed)
    {
        var warp = new PlanetVector(
            FractalNoise(direction, seed ^ MacroWarpSeedSaltX, 0.72, 3, 2.03, 0.50),
            FractalNoise(direction, seed ^ MacroWarpSeedSaltY, 0.72, 3, 2.03, 0.50),
            FractalNoise(direction, seed ^ MacroWarpSeedSaltZ, 0.72, 3, 2.03, 0.50));
        var warped = direction + (warp * 0.14);
        return warped.Length <= 0.000001 ? direction : PlanetVector.Normalize(warped);
    }

    private static double SampleContinentalField(PlanetVector direction, int seed)
    {
        var macroNoise = FractalNoise(direction, seed ^ ContinentalNoiseSeedSalt, 0.48, 4, 2.02, 0.53);
        var provinces = SampleContinentalProvinces(direction, seed);
        var oceanBasins = SampleOceanBasins(direction, seed);
        return (macroNoise * 0.52) + (provinces * 0.58) - (oceanBasins * 0.38) - 0.20;
    }

    private static double SampleContinentalProvinces(PlanetVector direction, int seed)
    {
        var strongestProvince = 0.0;

        for (var index = 0; index < ContinentalProvinceCount; index++)
        {
            var center = SeedDirection(seed ^ ContinentalSeedSalt, index, 5_279 + (index * 181));
            var orientation = SeedDirection(seed ^ RegionalSeedSalt, index, 8_171 + (index * 239));
            var tangent = orientation - (center * PlanetVector.Dot(orientation, center));
            if (tangent.Length <= 0.000001)
            {
                var reference = Math.Abs(center.Y) < 0.9 ? PlanetVector.UnitY : PlanetVector.UnitX;
                tangent = PlanetVector.Cross(center, reference);
            }

            tangent = PlanetVector.Normalize(tangent);
            var edgeDot = Lerp(0.80, 0.88, UnitHash((index * 17) + 3, (index * 29) + 7, 11, seed ^ ContinentalSeedSalt));
            var coreDot = Math.Min(
                0.975,
                edgeDot + Lerp(0.075, 0.12, UnitHash((index * 13) + 5, 23, (index * 31) + 9, seed ^ RegionalSeedSalt)));
            var boundaryWarp = FractalNoise(
                direction,
                seed ^ ContinentalSeedSalt ^ ((index * 1_571) + 313),
                2.1,
                3,
                2.07,
                0.50) * 0.055;
            var firstOffset = Lerp(0.30, 0.52, UnitHash(index, 41, 17, seed ^ DetailSeedSalt));
            var secondOffset = Lerp(0.20, 0.38, UnitHash(index, 59, 29, seed ^ PlateauSeedSalt));
            var firstLobe = PlanetVector.Normalize(center + (tangent * firstOffset));
            var secondLobe = PlanetVector.Normalize(center - (tangent * secondOffset));

            var primary = SmoothStep(edgeDot, coreDot, PlanetVector.Dot(direction, center) + boundaryWarp);
            var secondary = SmoothStep(edgeDot - 0.015, coreDot, PlanetVector.Dot(direction, firstLobe) + (boundaryWarp * 0.80)) * 0.92;
            var tertiary = SmoothStep(
                edgeDot + 0.01,
                Math.Min(0.985, coreDot + 0.01),
                PlanetVector.Dot(direction, secondLobe) + (boundaryWarp * 0.70)) * 0.78;
            strongestProvince = Math.Max(strongestProvince, Math.Max(primary, Math.Max(secondary, tertiary)));
        }

        return strongestProvince;
    }

    private static double SampleOceanBasins(PlanetVector direction, int seed)
    {
        var strongestBasin = 0.0;

        for (var index = 0; index < OceanBasinCount; index++)
        {
            var center = SeedDirection(seed ^ OceanBasinSeedSalt, index, 1_103 + (index * 193));
            var edgeDot = Lerp(0.78, 0.88, UnitHash((index * 19) + 1, 7, (index * 37) + 3, seed ^ OceanBasinSeedSalt));
            var coreDot = Math.Min(0.975, edgeDot + 0.10);
            var boundaryWarp = FractalNoise(
                direction,
                seed ^ OceanBasinSeedSalt ^ ((index * 1_877) + 521),
                1.7,
                3,
                2.09,
                0.51) * 0.045;
            var basin = SmoothStep(edgeDot, coreDot, PlanetVector.Dot(direction, center) + boundaryWarp);
            strongestBasin = Math.Max(strongestBasin, basin);
        }

        return strongestBasin;
    }

    private static double SamplePlateBoundaryUplift(PlanetVector direction, int seed, double landMask)
    {
        var plateWarp = FractalNoise(direction, seed ^ PlateWarpSeedSalt, 1.8, 3, 2.07, 0.50);
        var strongestScore = double.NegativeInfinity;
        var secondScore = double.NegativeInfinity;
        var strongestIndex = -1;
        var secondIndex = -1;

        for (var index = 0; index < PlateCount; index++)
        {
            var center = SeedDirection(seed ^ PlateSeedSalt, index, 3_343 + (index * 149));
            var bias = Lerp(-0.035, 0.035, UnitHash((index * 7) + 3, 19, (index * 23) + 5, seed ^ PlateSeedSalt));
            var warpScale = Lerp(-0.055, 0.055, UnitHash((index * 11) + 7, 31, (index * 29) + 13, seed ^ PlateWarpSeedSalt));
            var score = PlanetVector.Dot(direction, center) + bias + (plateWarp * warpScale);

            if (score > strongestScore)
            {
                secondScore = strongestScore;
                secondIndex = strongestIndex;
                strongestScore = score;
                strongestIndex = index;
            }
            else if (score > secondScore)
            {
                secondScore = score;
                secondIndex = index;
            }
        }

        if (secondIndex < 0)
        {
            return 0.0;
        }

        var boundaryStrength = 1.0 - SmoothStep(0.014, 0.155, strongestScore - secondScore);
        var lowerIndex = Math.Min(strongestIndex, secondIndex);
        var upperIndex = Math.Max(strongestIndex, secondIndex);
        var activity = UnitHash(
            (lowerIndex * 31) + (upperIndex * 17) + 3,
            (upperIndex * 43) + (lowerIndex * 11) + 5,
            (lowerIndex * upperIndex) + 7,
            seed ^ PlateBoundarySeedSalt);
        var convergence = SmoothStep(0.30, 0.82, activity);
        var pairSeed = unchecked(
            seed ^
            PlateBoundarySeedSalt ^
            ((lowerIndex + 1) * 73_856_093) ^
            ((upperIndex + 1) * 19_349_663));
        var ridgeNoise = ToUnitRange(RidgedNoise(direction, pairSeed, 8.5, 4, 2.05, 0.50));
        var segmentation = SmoothStep(
            0.12,
            0.58,
            ToUnitRange(FractalNoise(direction, pairSeed ^ DetailSeedSalt, 2.6, 3, 2.11, 0.51)));

        return boundaryStrength *
            boundaryStrength *
            convergence *
            (0.42 + (ridgeNoise * 0.90)) *
            (0.42 + (segmentation * 0.58)) *
            landMask;
    }

    private static double SampleContinentalInterior(PlanetVector direction, int seed, double continental)
    {
        var core = SmoothStep(0.08, 0.34, continental);
        var stability = ToUnitRange(FractalNoise(direction, seed ^ PlateauSeedSalt, 1.35, 3, 2.05, 0.50));
        return core * Lerp(0.65, 1.0, stability);
    }

    private static double SampleMountainShape(PlanetVector direction, int seed, double plateBoundaryUplift)
    {
        var normalizedBoundary = Math.Clamp(plateBoundaryUplift / 1.35, 0.0, 1.0);
        if (normalizedBoundary <= 0.0)
        {
            return 0.0;
        }

        var crestNoise = ToUnitRange(RidgedNoise(direction, seed ^ MountainCrestSeedSalt, 18.0, 4, 2.04, 0.48));
        var crest = Math.Pow(normalizedBoundary, 1.65) * Lerp(0.68, 1.24, crestNoise);
        var foothills = Math.Sqrt(normalizedBoundary) * (1.0 - (crest * 0.28));
        return Math.Max(0.0, (crest * 0.86) + (foothills * 0.24));
    }

    private static double SampleEscarpments(PlanetVector direction, int seed, double plateaus)
    {
        if (plateaus <= 0.0)
        {
            return 0.0;
        }

        var plateauTop = SmoothStep(0.30, 0.68, plateaus);
        var edge = Math.Clamp((plateauTop - plateaus) * 2.4, -1.0, 1.0);
        var scarpTexture = ToUnitRange(RidgedNoise(direction, seed ^ ScarpSeedSalt, 12.0, 3, 2.08, 0.48));
        return edge * Lerp(0.74, 1.18, scarpTexture);
    }

    private static double SampleAncientValleys(PlanetVector direction, int seed, double continental, double landMask)
    {
        var interiorSupport = SmoothStep(0.015, 0.34, continental) * landMask;
        if (interiorSupport <= 0.0)
        {
            return 0.0;
        }

        var trunkNoise = ToUnitRange(RidgedNoise(direction, seed ^ ValleySeedSalt, 5.2, 4, 2.05, 0.51));
        var branchNoise = ToUnitRange(RidgedNoise(direction, seed ^ ValleyBranchSeedSalt, 10.8, 3, 2.09, 0.48));
        var trunk = SmoothStep(0.70, 0.94, trunkNoise);
        var branches = SmoothStep(0.77, 0.965, branchNoise);
        var regionalGate = SmoothStep(
            0.22,
            0.68,
            ToUnitRange(FractalNoise(direction, seed ^ RegionalSeedSalt ^ ValleySeedSalt, 2.2, 3, 2.10, 0.50)));
        return ((trunk * 0.78) + (branches * 0.34)) * Lerp(0.48, 1.0, regionalGate) * interiorSupport;
    }

    private static double SampleCanyonSystems(PlanetVector direction, int seed, double continental, double landMask)
    {
        var highlandSupport = SmoothStep(0.02, 0.30, continental) * landMask;
        if (highlandSupport <= 0.0)
        {
            return 0.0;
        }

        var strongest = 0.0;

        for (var index = 0; index < CanyonSystemCount; index++)
        {
            var normal = SeedDirection(seed ^ CanyonSeedSalt, index, 7_913 + (index * 227));
            var anchor = SeedDirection(seed ^ CanyonWarpSeedSalt, index, 10_831 + (index * 311));
            var projectedAnchor = anchor - (normal * PlanetVector.Dot(anchor, normal));
            if (projectedAnchor.Length <= 0.000001)
            {
                continue;
            }

            var arcCenter = PlanetVector.Normalize(projectedAnchor);
            var tangent = PlanetVector.Normalize(PlanetVector.Cross(normal, arcCenter));
            var warp = FractalNoise(
                direction,
                seed ^ CanyonWarpSeedSalt ^ ((index * 1_193) + 337),
                3.1,
                3,
                2.07,
                0.50) * 0.018;
            var distance = Math.Abs(PlanetVector.Dot(direction, normal) + warp);
            var extent = SmoothStep(-0.12, 0.46, PlanetVector.Dot(direction, arcCenter));
            var segmentation = SmoothStep(
                0.24,
                0.62,
                ToUnitRange(FractalNoise(direction, seed ^ CanyonSeedSalt ^ ((index * 887) + 191), 4.0, 3, 2.11, 0.50)));
            var core = 1.0 - SmoothStep(0.004, 0.019, distance);
            var shoulder = 1.0 - SmoothStep(0.018, 0.060, distance);

            var branchNormal = PlanetVector.Normalize(normal + (tangent * Lerp(
                -0.24,
                0.24,
                UnitHash(index * 13, 37, index * 29, seed ^ CanyonWarpSeedSalt))));
            var branchDistance = Math.Abs(PlanetVector.Dot(direction, branchNormal) + (warp * 0.72));
            var branchExtent = SmoothStep(0.02, 0.58, PlanetVector.Dot(direction, arcCenter));
            var branch = (1.0 - SmoothStep(0.004, 0.022, branchDistance)) * branchExtent;

            var system = ((core * 0.82) + (shoulder * 0.28) + (branch * 0.34)) * extent * Lerp(0.46, 1.0, segmentation);
            strongest = Math.Max(strongest, system);
        }

        return strongest * highlandSupport;
    }

    private static double SampleSphericalRegions(
        PlanetVector direction,
        int seed,
        int seedSalt,
        int count,
        double edgeDot,
        double coreDot)
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

    private static double FractalNoise(
        PlanetVector direction,
        int seed,
        double frequency,
        int octaves,
        double lacunarity,
        double persistence)
    {
        var amplitude = 1.0;
        var sum = 0.0;
        var normalization = 0.0;

        for (var octave = 0; octave < octaves; octave++)
        {
            sum += ValueNoise(
                direction.X * frequency,
                direction.Y * frequency,
                direction.Z * frequency,
                seed + (octave * 1_013)) * amplitude;
            normalization += amplitude;
            frequency *= lacunarity;
            amplitude *= persistence;
        }

        return sum / normalization;
    }

    private static double RidgedNoise(
        PlanetVector direction,
        int seed,
        double frequency,
        int octaves,
        double lacunarity,
        double persistence)
    {
        var amplitude = 1.0;
        var sum = 0.0;
        var normalization = 0.0;

        for (var octave = 0; octave < octaves; octave++)
        {
            var noise = ValueNoise(
                direction.X * frequency,
                direction.Y * frequency,
                direction.Z * frequency,
                seed + (octave * 1_297));
            var ridge = 1.0 - Math.Abs(noise);
            ridge = (ridge * ridge * 2.0) - 1.0;
            sum += ridge * amplitude;
            normalization += amplitude;
            frequency *= lacunarity;
            amplitude *= persistence;
        }

        return sum / normalization;
    }

    private static double UnitHash(int x, int y, int z, int seed) => (HashValue(x, y, z, seed) + 1.0) * 0.5;

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
