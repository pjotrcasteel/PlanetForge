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
    public void SampleElevationMeters_IntegritySeedGallery_PreservesPlanetScaleRelief()
    {
        var source = new ProceduralPlanetElevationSource();
        var directions = FibonacciDirections(384).ToArray();

        foreach (var seed in PlanetSeedCatalog.IntegritySeeds)
        {
            var elevations = directions.Select(direction => source.SampleElevationMeters(direction, seed)).ToArray();
            var landFraction = elevations.Count(elevation => elevation > 0.0) / (double)elevations.Length;

            Assert.IsGreaterThan(0.12, landFraction, $"Seed {seed} has too little exposed land.");
            Assert.IsLessThan(0.55, landFraction, $"Seed {seed} has too much exposed land.");
            Assert.IsGreaterThan(3_000.0, elevations.Max(), $"Seed {seed} lacks continental highlands.");
            Assert.IsLessThan(-1_500.0, elevations.Min(), $"Seed {seed} lacks a meaningful ocean basin.");
            Assert.IsGreaterThan(5_000.0, elevations.Max() - elevations.Min(), $"Seed {seed} lacks planet-scale relief.");
        }
    }

    [TestMethod]
    public void SampleElevationMeters_ContinentalScale_NearbySamplesAreMoreCorrelatedThanAntipodes()
    {
        var source = new ProceduralPlanetElevationSource();
        var directions = FibonacciDirections(256).ToArray();
        var nearbyDelta = directions.Average(direction =>
        {
            var nearby = OffsetDirection(direction, 0.035);
            return Math.Abs(source.SampleElevationMeters(direction, PlanetSeedCatalog.ShowcaseSeed) -
                source.SampleElevationMeters(nearby, PlanetSeedCatalog.ShowcaseSeed));
        });
        var antipodalDelta = directions.Average(direction =>
        {
            var opposite = direction * -1.0;
            return Math.Abs(source.SampleElevationMeters(direction, PlanetSeedCatalog.ShowcaseSeed) -
                source.SampleElevationMeters(opposite, PlanetSeedCatalog.ShowcaseSeed));
        });

        Assert.IsLessThan(antipodalDelta * 0.40, nearbyDelta);
    }

    [TestMethod]
    public void SampleElevationMeters_IntegritySeedGallery_ContainsLocalTerrainRelief()
    {
        var source = new ProceduralPlanetElevationSource();
        var directions = FibonacciDirections(384).ToArray();

        foreach (var seed in PlanetSeedCatalog.IntegritySeeds)
        {
            var localRelief = directions
                .Select(direction =>
                {
                    var nearby = OffsetDirection(direction, 0.012);
                    var first = source.SampleElevationMeters(direction, seed);
                    var second = source.SampleElevationMeters(nearby, seed);
                    return Math.Abs(first - second);
                })
                .OrderBy(value => value)
                .ToArray();
            var upperDecile = localRelief[(int)Math.Floor(localRelief.Length * 0.90)];
            var strongTransitions = localRelief.Count(value => value > 220.0);

            Assert.IsGreaterThan(140.0, upperDecile, $"Seed {seed} is too smooth at regional scale.");
            Assert.IsGreaterThan(8, strongTransitions, $"Seed {seed} lacks enough ridges, scarps or canyon walls.");
        }
    }

    [TestMethod]
    public void SampleElevationMeters_ShowcaseSeed_HasSharpHighlandTransitions()
    {
        var source = new ProceduralPlanetElevationSource();
        var samples = FibonacciDirections(1_024)
            .Select(direction =>
            {
                var elevation = source.SampleElevationMeters(direction, PlanetSeedCatalog.ShowcaseSeed);
                var nearby = OffsetDirection(direction, 0.006);
                var nearbyElevation = source.SampleElevationMeters(nearby, PlanetSeedCatalog.ShowcaseSeed);
                return (Elevation: elevation, Relief: Math.Abs(elevation - nearbyElevation));
            })
            .Where(sample => sample.Elevation > 900.0)
            .ToArray();

        Assert.IsGreaterThan(20, samples.Length);
        Assert.IsGreaterThan(400.0, samples.Max(sample => sample.Relief));
        Assert.IsGreaterThan(5, samples.Count(sample => sample.Relief > 180.0));
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

        Assert.IsGreaterThan(4_500.0, elevations.Max());
        Assert.IsLessThan(-2_000.0, elevations.Min());
        Assert.IsGreaterThan(0.15, landFraction);
        Assert.IsLessThan(0.45, landFraction);
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

        Assert.IsGreaterThan(12, highlands.Length);
        Assert.IsGreaterThanOrEqualTo(6, elevationBands);
        Assert.IsGreaterThan(2_500.0, highlands.Max() - highlands.Min());
    }

    private static PlanetVector OffsetDirection(PlanetVector direction, double amount)
    {
        var reference = Math.Abs(direction.Y) < 0.9 ? PlanetVector.UnitY : PlanetVector.UnitX;
        var tangent = PlanetVector.Normalize(PlanetVector.Cross(direction, reference));
        return PlanetVector.Normalize(direction + (tangent * amount));
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