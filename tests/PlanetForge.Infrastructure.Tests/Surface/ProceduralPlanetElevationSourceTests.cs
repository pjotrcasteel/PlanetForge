using PlanetForge.Domain.Surface;
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