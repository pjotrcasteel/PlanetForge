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
}
