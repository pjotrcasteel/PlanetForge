using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Tests.Surface;

[TestClass]
public sealed class PlanetTileGeometryTests
{
    [TestMethod]
    public void CalculateBounds_PositiveZRoot_IsCenteredOnPositiveZ()
    {
        var bounds = PlanetTileGeometry.CalculateBounds(new PlanetTileId(CubeFace.PositiveZ, 0, 0, 0));

        Assert.AreEqual(0.0, bounds.CenterDirection.X, 0.0000000001);
        Assert.AreEqual(0.0, bounds.CenterDirection.Y, 0.0000000001);
        Assert.AreEqual(1.0, bounds.CenterDirection.Z, 0.0000000001);
        Assert.IsGreaterThan(0.0, bounds.AngularRadiusRadians);
        Assert.IsLessThan(Math.PI / 2.0, bounds.AngularRadiusRadians);
    }

    [TestMethod]
    public void CalculateBounds_Child_HasSmallerAngularRadiusThanParent()
    {
        var parent = PlanetTileGeometry.CalculateBounds(new PlanetTileId(CubeFace.PositiveZ, 0, 0, 0));
        var child = PlanetTileGeometry.CalculateBounds(new PlanetTileId(CubeFace.PositiveZ, 1, 0, 0));

        Assert.IsLessThan(parent.AngularRadiusRadians, child.AngularRadiusRadians);
    }
}
