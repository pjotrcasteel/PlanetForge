using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Tests.Surface;

[TestClass]
public sealed class PlanetSurfacePointTests
{
    [TestMethod]
    public void WorldPositionMeters_UsesPhysicalRadiusAndElevation()
    {
        var point = new PlanetSurfacePoint(PlanetVector.UnitZ, 1_250.0);

        var world = point.WorldPositionMeters(6_371_000.0);

        Assert.AreEqual(6_372_250.0, world.Length, 0.000001);
    }

    [TestMethod]
    public void WorldPositionMeters_ElevationBelowPlanetCenter_Throws()
    {
        var point = new PlanetSurfacePoint(PlanetVector.UnitZ, -10.0);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => point.WorldPositionMeters(5.0));
    }
}
