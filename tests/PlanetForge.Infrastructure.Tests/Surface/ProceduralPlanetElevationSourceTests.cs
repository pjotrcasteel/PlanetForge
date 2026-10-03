using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class ProceduralPlanetElevationSourceTests
{
    [TestMethod]
    public void Sample_SameDirectionAndSeed_IsDeterministic()
    {
        var source = new ProceduralPlanetElevationSource();
        var direction = PlanetVector.Normalize(new PlanetVector(0.31, -0.47, 0.83));

        var first = source.Sample(direction, 42);
        var second = source.Sample(direction, 42);

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void Sample_DifferentSeed_ProducesDifferentElevation()
    {
        var source = new ProceduralPlanetElevationSource();
        var direction = PlanetVector.Normalize(new PlanetVector(0.31, -0.47, 0.83));

        var first = source.Sample(direction, 42);
        var second = source.Sample(direction, 43);

        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void Sample_NearbyDirections_RemainContinuous()
    {
        var source = new ProceduralPlanetElevationSource();
        var firstDirection = PlanetVector.Normalize(new PlanetVector(0.31, -0.47, 0.83));
        var secondDirection = PlanetVector.Normalize(firstDirection + new PlanetVector(0.0000001, -0.0000001, 0.0000001));

        var first = source.Sample(firstDirection, 42);
        var second = source.Sample(secondDirection, 42);

        Assert.IsLessThan(Math.Abs(first - second), 0.001);
    }

    [TestMethod]
    public void Sample_ReturnsNormalizedElevationRange()
    {
        var source = new ProceduralPlanetElevationSource();
        PlanetVector[] directions =
        [
            new PlanetVector(1.0, 0.0, 0.0),
            new PlanetVector(0.0, 1.0, 0.0),
            new PlanetVector(0.0, 0.0, 1.0),
            PlanetVector.Normalize(new PlanetVector(1.0, 1.0, 1.0)),
            PlanetVector.Normalize(new PlanetVector(-1.0, 0.25, 0.4)),
        ];

        foreach (var direction in directions)
        {
            var elevation = source.Sample(direction, 42);
            Assert.IsGreaterThanOrEqualTo(elevation, -1.0);
            Assert.IsLessThanOrEqualTo(elevation, 1.0);
        }
    }
}
