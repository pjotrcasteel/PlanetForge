using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Surface;

[TestClass]
public sealed class PlanetSurfaceLodSelectorTests
{
    [TestMethod]
    public void Select_NearSurface_ReachesHigherDetailThanFarOrbit()
    {
        var selector = new PlanetSurfaceLodSelector(new PlanetSurfaceLodOptions(7, 220.0, 0.02));
        var direction = new PlanetVector(0.0, 0.0, 1.0);
        var far = selector.Select(new PlanetSurfaceView(direction, 5.0, 1080, Math.PI / 4.0));
        var near = selector.Select(new PlanetSurfaceView(direction, 1.2, 1080, Math.PI / 4.0));

        Assert.IsGreaterThan(far.Max(tile => tile.Level), near.Max(tile => tile.Level));
    }

    [TestMethod]
    public void Select_OppositeRootFace_IsHorizonCulled()
    {
        var selector = new PlanetSurfaceLodSelector(new PlanetSurfaceLodOptions(0, 10_000.0, 0.0));

        var tiles = selector.Select(new PlanetSurfaceView(new PlanetVector(0.0, 0.0, 1.0), 2.0, 1080, Math.PI / 4.0));

        Assert.IsFalse(tiles.Any(tile => tile.Face == CubeFace.NegativeZ));
        Assert.IsTrue(tiles.Any(tile => tile.Face == CubeFace.PositiveZ));
    }

    [TestMethod]
    public void Select_NeverExceedsConfiguredMaximumLevel()
    {
        const int maxLevel = 4;
        var selector = new PlanetSurfaceLodSelector(new PlanetSurfaceLodOptions(maxLevel, 1.0, 0.02));

        var tiles = selector.Select(new PlanetSurfaceView(new PlanetVector(0.0, 0.0, 1.0), 1.05, 2160, Math.PI / 4.0));

        Assert.IsTrue(tiles.Count > 0);
        Assert.IsTrue(tiles.All(tile => tile.Level <= maxLevel));
    }

    [TestMethod]
    public void Select_CameraInsidePlanet_Throws()
    {
        var selector = new PlanetSurfaceLodSelector(PlanetSurfaceLodOptions.Default);
        var view = new PlanetSurfaceView(new PlanetVector(0.0, 0.0, 1.0), 1.0, 1080, Math.PI / 4.0);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => selector.Select(view));
    }
}
