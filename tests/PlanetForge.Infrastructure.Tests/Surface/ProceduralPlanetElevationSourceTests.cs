using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class ProceduralPlanetElevationSourceTests
{
    [TestMethod]
    public void SampleElevationMeters_SameDirectionAndSeed_IsDeterministic()
    {
        var source = new ProceduralPlanetElevationSource();
        var direction = PlanetVector.Normalize(new PlanetVector(0.31, -0.47, 0.83));

        var first = source.SampleElevationMeters(direction, 42);
        var second = source.SampleElevationMeters(direction, 42);

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void SampleTerrainFields_GeneratedElevation_MatchesCanonicalElevationSource()
    {
        var source = new ProceduralPlanetElevationSource();
        foreach (var seed in new[] { PlanetSeedCatalog.ShowcaseSeed, 346147916, 579460630 })
        {
            foreach (var direction in FibonacciDirections(64))
            {
                var fields = source.SampleTerrainFields(direction, seed);
                Assert.AreEqual(fields.ElevationMeters, source.SampleElevationMeters(direction, seed));
                Assert.IsTrue(double.IsFinite(fields.ContinentalPotential));
                Assert.IsTrue(double.IsFinite(fields.TectonicUplift));
                Assert.IsTrue(double.IsFinite(fields.MountainBelt));
            }
        }
    }

    [TestMethod]
    public void SampleElevationMeters_DifferentSeed_ProducesDifferentElevation()
    {
        var source = new ProceduralPlanetElevationSource();
        var direction = PlanetVector.Normalize(new PlanetVector(0.31, -0.47, 0.83));

        var first = source.SampleElevationMeters(direction, 42);
        var second = source.SampleElevationMeters(direction, 43);

        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void SampleElevationMeters_IntegritySeedGallery_ProducesDistinctWorldFingerprints()
    {
        var source = new ProceduralPlanetElevationSource();
        var directions = FibonacciDirections(512).ToArray();
        var fingerprints = PlanetSeedCatalog.IntegritySeeds
            .Select(seed => string.Join(",", directions.Select(direction => (int)Math.Round(source.SampleElevationMeters(direction, seed) / 25.0))))
            .ToArray();

        Assert.AreEqual(PlanetSeedCatalog.IntegritySeeds.Count, fingerprints.Distinct().Count());
    }

    [TestMethod]
    public void SampleElevationMeters_NearbyDirections_RemainContinuous()
    {
        var source = new ProceduralPlanetElevationSource();
        var firstDirection = PlanetVector.Normalize(new PlanetVector(0.31, -0.47, 0.83));
        var secondDirection = PlanetVector.Normalize(firstDirection + new PlanetVector(0.0000001, -0.0000001, 0.0000001));

        var first = source.SampleElevationMeters(firstDirection, 42);
        var second = source.SampleElevationMeters(secondDirection, 42);

        Assert.IsLessThan(10.0, Math.Abs(first - second));
    }

    [TestMethod]
    public void SampleElevationMeters_ReturnsEarthScaleReliefRange()
    {
        var source = new ProceduralPlanetElevationSource();
        PlanetVector[] directions =
        [
            PlanetVector.UnitX,
            PlanetVector.UnitY,
            PlanetVector.UnitZ,
            PlanetVector.Normalize(new PlanetVector(1.0, 1.0, 1.0)),
            PlanetVector.Normalize(new PlanetVector(-1.0, 0.25, 0.4)),
        ];

        foreach (var direction in directions)
        {
            var elevationMeters = source.SampleElevationMeters(direction, 42);
            Assert.IsGreaterThanOrEqualTo(-7_000.0, elevationMeters);
            Assert.IsLessThanOrEqualTo(9_000.0, elevationMeters);
        }
    }

    [TestMethod]
    public void SampleElevationMeters_GlobalSample_ContainsMountainsBasinsAndOceans()
    {
        var source = new ProceduralPlanetElevationSource();
        var elevations = FibonacciDirections(2_048).Select(direction => source.SampleElevationMeters(direction, 42)).ToArray();
        var landFraction = elevations.Count(elevation => elevation > 0.0) / (double)elevations.Length;

        Assert.IsGreaterThan(1_000.0, elevations.Max());
        Assert.IsLessThan(-2_000.0, elevations.Min());
        Assert.IsGreaterThan(0.15, landFraction);
        Assert.IsLessThan(0.55, landFraction);
    }

    [TestMethod]
    public void SampleElevationMeters_GlobalSample_ContainsVariedHighlandStructure()
    {
        var source = new ProceduralPlanetElevationSource();
        var highlands = FibonacciDirections(1_024)
            .Select(direction => source.SampleElevationMeters(direction, 42))
            .Where(elevation => elevation > 1_500.0)
            .ToArray();
        var elevationBands = highlands.Select(elevation => (int)Math.Floor(elevation / 500.0)).Distinct().Count();

        Assert.IsGreaterThan(8, highlands.Length);
        Assert.IsGreaterThanOrEqualTo(3, elevationBands);
        Assert.IsGreaterThan(700.0, highlands.Max() - highlands.Min());
    }


    [TestMethod]
    public void SampleElevationMeters_IntegritySeedGallery_AvoidsSingleFlatHemisphere()
    {
        var source = new ProceduralPlanetElevationSource();
        var directions = FibonacciDirections(2_048).ToArray();

        foreach (var seed in PlanetSeedCatalog.IntegritySeeds)
        {
            var elevations = directions.Select(direction => source.SampleElevationMeters(direction, seed)).ToArray();
            var landFraction = elevations.Count(elevation => elevation > 0.0) / (double)elevations.Length;
            var roundedBands = elevations.Select(elevation => (int)Math.Round(elevation / 250.0)).Distinct().Count();

            Assert.IsGreaterThan(0.10, landFraction, $"Seed {seed} generated almost no land.");
            Assert.IsLessThan(0.65, landFraction, $"Seed {seed} generated almost no ocean.");
            Assert.IsGreaterThanOrEqualTo(12, roundedBands, $"Seed {seed} has insufficient terrain variation.");
        }
    }

    [TestMethod]
    public void SampleElevationMeters_GlobalSample_HasTectonicMountainRelief()
    {
        var source = new ProceduralPlanetElevationSource();
        var elevations = FibonacciDirections(4_096).Select(direction => source.SampleElevationMeters(direction, 42)).ToArray();
        var highMountains = elevations.Count(elevation => elevation > 2_500.0);

        Assert.IsGreaterThan(0, highMountains);
        Assert.IsGreaterThan(3_000.0, elevations.Max());
    }


    [TestMethod]
    public void SampleTerrainFields_OrogenicBelts_FormRegionalMountainSystems()
    {
        var source = new ProceduralPlanetElevationSource();
        var directions = FibonacciDirections(4_096).ToArray();

        foreach (var seed in new[] { PlanetSeedCatalog.ShowcaseSeed, 346147916, 579460630 })
        {
            var samples = directions.Select(direction => source.SampleTerrainFields(direction, seed)).ToArray();
            var mountainSamples = samples.Where(sample => sample.MountainBelt > 0.60 && sample.ElevationMeters > 0.0).ToArray();
            Assert.IsGreaterThan(10, mountainSamples.Length, $"Seed {seed} lacks sufficiently broad orogenic mountain systems.");
            Assert.IsGreaterThan(1_200.0, mountainSamples.Max(sample => sample.ElevationMeters) -
                mountainSamples.Min(sample => sample.ElevationMeters), $"Seed {seed} lacks regional mountain relief.");
        }
    }

    [TestMethod]
    public void SampleElevationMeters_ContinentalLand_HasRegionalReliefHierarchy()
    {
        var source = new ProceduralPlanetElevationSource();
        var elevations = FibonacciDirections(8_192).Select(direction => source.SampleElevationMeters(direction, PlanetSeedCatalog.ShowcaseSeed)).Where(elevation => elevation > 100.0).ToArray();
        var lowlands = elevations.Count(elevation => elevation < 1_000.0);
        var uplands = elevations.Count(elevation => elevation >= 1_000.0 && elevation < 2_500.0);
        var mountains = elevations.Count(elevation => elevation >= 2_500.0);

        Assert.IsGreaterThan(50, lowlands);
        Assert.IsGreaterThan(20, uplands);
        Assert.IsGreaterThan(0, mountains);
        Assert.IsGreaterThan(2_000.0, elevations.Max() - elevations.Min());
    }

    private static IEnumerable<PlanetVector> FibonacciDirections(int count)
    {
        var goldenRatio = (1.0 + Math.Sqrt(5.0)) / 2.0;

        for (var index = 0; index < count; index++)
        {
            var y = 1.0 - (2.0 * (index + 0.5) / count);
            var radius = Math.Sqrt(Math.Max(0.0, 1.0 - (y * y)));
            var angle = 2.0 * Math.PI * index / goldenRatio;
            yield return new PlanetVector(radius * Math.Cos(angle), y, radius * Math.Sin(angle));
        }
    }
}